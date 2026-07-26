using System.Text.Json;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using Org.OpenAPITools.Models;
using StackExchange.Redis;

namespace ClubHub.Api.Services;

public interface IPermissionSnapshotCache
{
    Task<PermissionSnapshot> GetOrCreateAsync(
        int userId,
        Func<Task<PermissionSnapshot>> factory,
        CancellationToken cancellationToken = default);

    Task<string?> GetAccountStatusAsync(
        int userId,
        Func<Task<string?>> factory,
        CancellationToken cancellationToken = default);

    Task InvalidateAsync(
        int userId,
        bool requiredForSafety,
        CancellationToken cancellationToken = default);
}

public sealed record PermissionSnapshot(
    int UserId,
    string? AccountStatus,
    IReadOnlyList<AuthRole> Roles);

public sealed class PermissionSnapshotCache : IPermissionSnapshotCache
{
    private const string MissingAccount = "__missing__";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly IRedisDatabase _redis;
    private readonly IRedisKeyBuilder _keys;
    private readonly RedisOptions _options;
    private readonly ILogger<PermissionSnapshotCache> _logger;

    public PermissionSnapshotCache(
        IRedisDatabase redis,
        IRedisKeyBuilder keys,
        IOptions<RedisOptions> options,
        ILogger<PermissionSnapshotCache> logger)
    {
        _redis = redis;
        _keys = keys;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PermissionSnapshot> GetOrCreateAsync(
        int userId,
        Func<Task<PermissionSnapshot>> factory,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) return await factory();

        try
        {
            var cached = await _redis.StringGetAsync(Key(userId), cancellationToken);
            if (cached.HasValue)
            {
                try
                {
                    var snapshot = JsonSerializer.Deserialize<PermissionSnapshot>((string)cached!);
                    if (snapshot is not null && snapshot.UserId == userId) return snapshot;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Discarding damaged permission snapshot for user {UserId}.", userId);
                    await _redis.KeyDeleteAsync(Key(userId), cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis permission snapshot read failed; loading Oracle source.");
        }

        var loaded = await factory();
        try
        {
            await _redis.StringSetAsync(
                Key(userId),
                JsonSerializer.Serialize(loaded),
                Ttl,
                cancellationToken);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis permission snapshot write failed; Oracle result remains authoritative.");
        }

        return loaded;
    }

    public async Task<string?> GetAccountStatusAsync(
        int userId,
        Func<Task<string?>> factory,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) return await factory();

        try
        {
            var cached = await _redis.StringGetAsync(AccountKey(userId), cancellationToken);
            if (cached.HasValue)
            {
                var value = (string)cached!;
                return value == MissingAccount ? null : value;
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis account-status snapshot read failed; loading Oracle source.");
        }

        var loaded = await factory();
        try
        {
            await _redis.StringSetAsync(
                AccountKey(userId),
                loaded ?? MissingAccount,
                Ttl,
                cancellationToken);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis account-status snapshot write failed; Oracle result remains authoritative.");
        }
        return loaded;
    }

    public async Task InvalidateAsync(
        int userId,
        bool requiredForSafety,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) return;

        try
        {
            await _redis.ScriptEvaluateAsync(
                "return redis.call('del', KEYS[1], KEYS[2])",
                [Key(userId), AccountKey(userId)],
                [],
                cancellationToken);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis permission snapshot invalidation failed for user {UserId}.", userId);
            if (requiredForSafety)
            {
                throw new PermissionSnapshotUnavailableException(
                    "Permission snapshot could not be safely invalidated.",
                    ex);
            }
        }
    }

    private bool Enabled => _options.Enabled && _options.Features.PermissionCache;

    private RedisKey Key(int userId) => _keys.Build("permission", "snapshot", userId.ToString());

    private RedisKey AccountKey(int userId) =>
        _keys.Build("permission", "account-status", userId.ToString());
}

public sealed class PermissionSnapshotUnavailableException : Exception
{
    public PermissionSnapshotUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
