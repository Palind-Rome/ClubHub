using System.Diagnostics;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Infrastructure.Redis;

public enum RedisCacheReadStatus
{
    Hit,
    Miss,
    Disabled,
    Unavailable,
    InvalidPayload
}

public enum RedisCacheWriteStatus
{
    Succeeded,
    Disabled,
    Unavailable,
    PayloadTooLarge
}

public readonly record struct RedisCacheReadResult<T>(
    RedisCacheReadStatus Status,
    T? Value,
    bool IsNull);

public interface IRedisCacheService
{
    Task<RedisCacheReadResult<T>> GetAsync<T>(
        RedisKey key,
        CancellationToken cancellationToken = default);

    Task<RedisCacheWriteStatus> SetAsync<T>(
        RedisKey key,
        T? value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    Task<RedisCacheWriteStatus> RemoveAsync(
        RedisKey key,
        CancellationToken cancellationToken = default);

    Task<T?> GetOrCreateAsync<T>(
        RedisKey key,
        Func<CancellationToken, Task<T?>> source,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);
}

public sealed class RedisCacheService : IRedisCacheService
{
    private readonly IRedisDatabase _database;
    private readonly IRedisCacheSerializer _serializer;
    private readonly IRedisTtlPolicy _ttlPolicy;
    private readonly RedisOptions _options;
    private readonly RedisMetrics _metrics;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(
        IRedisDatabase database,
        IRedisCacheSerializer serializer,
        IRedisTtlPolicy ttlPolicy,
        IOptions<RedisOptions> options,
        RedisMetrics metrics,
        ILogger<RedisCacheService> logger)
    {
        _database = database;
        _serializer = serializer;
        _ttlPolicy = ttlPolicy;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<RedisCacheReadResult<T>> GetAsync<T>(
        RedisKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCacheEnabled)
        {
            return new(RedisCacheReadStatus.Disabled, default, false);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var cached = await _database.StringGetAsync(key);
            cancellationToken.ThrowIfCancellationRequested();
            if (!cached.HasValue)
            {
                _metrics.RecordCacheRead("miss", stopwatch.Elapsed.TotalMilliseconds);
                return new(RedisCacheReadStatus.Miss, default, false);
            }

            var payload = (byte[])cached!;
            if (payload.Length > _options.MaxPayloadBytes)
            {
                _metrics.RecordCacheRead("invalid-payload", stopwatch.Elapsed.TotalMilliseconds);
                _logger.LogWarning(
                    "Redis cache payload exceeded the configured {MaxPayloadBytes} byte limit.",
                    _options.MaxPayloadBytes);
                await TryDeleteInvalidPayloadAsync(key);
                return new(RedisCacheReadStatus.InvalidPayload, default, false);
            }

            var parsed = _serializer.Deserialize<T>(payload);
            if (parsed.Status == RedisPayloadReadStatus.Success)
            {
                _metrics.RecordCacheRead("hit", stopwatch.Elapsed.TotalMilliseconds);
                return new(RedisCacheReadStatus.Hit, parsed.Value, parsed.IsNull);
            }

            _metrics.RecordCacheRead("invalid-payload", stopwatch.Elapsed.TotalMilliseconds);
            _logger.LogWarning(
                "Redis cache payload could not be read because its status was {PayloadStatus}.",
                parsed.Status);
            await TryDeleteInvalidPayloadAsync(key);
            return new(RedisCacheReadStatus.InvalidPayload, default, false);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            _metrics.RecordCacheRead("unavailable", stopwatch.Elapsed.TotalMilliseconds);
            _metrics.RecordFailure("cache-read");
            _logger.LogWarning(
                exception,
                "Redis cache read failed; the caller may use its source of truth.");
            return new(RedisCacheReadStatus.Unavailable, default, false);
        }
    }

    public async Task<RedisCacheWriteStatus> SetAsync<T>(
        RedisKey key,
        T? value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCacheEnabled)
        {
            return RedisCacheWriteStatus.Disabled;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var payload = _serializer.Serialize(value);
            if (payload.Length > _options.MaxPayloadBytes)
            {
                _metrics.RecordCacheWrite(
                    "cache-write",
                    "payload-too-large",
                    stopwatch.Elapsed.TotalMilliseconds);
                _logger.LogWarning(
                    "Redis cache write skipped because the payload exceeded the configured "
                    + "{MaxPayloadBytes} byte limit.",
                    _options.MaxPayloadBytes);
                return RedisCacheWriteStatus.PayloadTooLarge;
            }

            var expiration = _ttlPolicy.GetExpiration(ttl, value is null);
            var succeeded = await _database.StringSetAsync(key, payload, expiration);
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = succeeded ? "succeeded" : "rejected";
            _metrics.RecordCacheWrite("cache-write", outcome, stopwatch.Elapsed.TotalMilliseconds);
            return succeeded
                ? RedisCacheWriteStatus.Succeeded
                : RedisCacheWriteStatus.Unavailable;
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            _metrics.RecordCacheWrite(
                "cache-write",
                "unavailable",
                stopwatch.Elapsed.TotalMilliseconds);
            _metrics.RecordFailure("cache-write");
            _logger.LogWarning(
                exception,
                "Redis cache write failed; the source-of-truth result remains authoritative.");
            return RedisCacheWriteStatus.Unavailable;
        }
    }

    public async Task<RedisCacheWriteStatus> RemoveAsync(
        RedisKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCacheEnabled)
        {
            return RedisCacheWriteStatus.Disabled;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _database.KeyDeleteAsync(key);
            cancellationToken.ThrowIfCancellationRequested();
            _metrics.RecordCacheWrite(
                "cache-delete",
                "succeeded",
                stopwatch.Elapsed.TotalMilliseconds);
            return RedisCacheWriteStatus.Succeeded;
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            _metrics.RecordCacheWrite(
                "cache-delete",
                "unavailable",
                stopwatch.Elapsed.TotalMilliseconds);
            _metrics.RecordFailure("cache-delete");
            _logger.LogWarning(exception, "Redis cache deletion failed.");
            return RedisCacheWriteStatus.Unavailable;
        }
    }

    public async Task<T?> GetOrCreateAsync<T>(
        RedisKey key,
        Func<CancellationToken, Task<T?>> source,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached.Status == RedisCacheReadStatus.Hit)
        {
            return cached.Value;
        }

        var sourceValue = await source(cancellationToken);
        if (cached.Status is not (RedisCacheReadStatus.Disabled or RedisCacheReadStatus.Unavailable))
        {
            await SetAsync(key, sourceValue, ttl, cancellationToken);
        }

        return sourceValue;
    }

    private bool IsCacheEnabled => _options.Enabled && _options.Features.Cache;

    private async Task TryDeleteInvalidPayloadAsync(RedisKey key)
    {
        try
        {
            await _database.KeyDeleteAsync(key);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            _metrics.RecordFailure("invalid-payload-delete");
            _logger.LogDebug(
                exception,
                "Redis invalid cache payload could not be deleted.");
        }
    }

    private static bool IsRedisFailure(Exception exception) =>
        exception is RedisException or TimeoutException or ObjectDisposedException;
}
