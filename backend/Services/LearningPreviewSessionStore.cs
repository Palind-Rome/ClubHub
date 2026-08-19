using System.Text.Json;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Services;

/// <summary>
/// 保存与预览 Cookie 绝对生命周期一致的跨实例预览会话。
/// </summary>
public sealed class LearningPreviewSessionStore : IDisposable
{
    private const int MaxSessions = 4096;
    private const string StoreScript = """
        local expired = redis.call('zrangebyscore', KEYS[2], '-inf', ARGV[7])
        for _, digest in ipairs(expired) do
          redis.call('del', ARGV[5] .. digest)
          redis.call('zrem', KEYS[2], digest)
        end
        if redis.call('exists', KEYS[1]) == 0 and redis.call('zcard', KEYS[2]) >= tonumber(ARGV[4]) then
          return 0
        end
        redis.call('set', KEYS[1], ARGV[1], 'EX', ARGV[3])
        redis.call('zadd', KEYS[2], ARGV[2], ARGV[6])
        return 1
        """;

    private readonly MemoryCache _local =
        new(new MemoryCacheOptions { SizeLimit = MaxSessions });
    private readonly IRedisDatabase? _redis;
    private readonly IRedisKeyBuilder? _keys;
    private readonly RedisOptions? _redisOptions;
    private readonly bool _development;

    public LearningPreviewSessionStore()
    {
        _development = true;
    }

    public LearningPreviewSessionStore(
        IRedisDatabase redis,
        IRedisKeyBuilder keys,
        IOptions<RedisOptions> redisOptions,
        IHostEnvironment environment)
    {
        _redis = redis;
        _keys = keys;
        _redisOptions = redisOptions.Value;
        _development = environment.IsDevelopment();
    }

    public async Task<bool> StoreAsync(
        string token,
        int userId,
        int itemId,
        PreparedLearningPreview preview,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (!RedisEnabled)
        {
            Store(token, userId, itemId, preview, lifetime);
            return true;
        }

        var digest = _keys!.HashSensitive(token);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(lifetime);
        var safePreview = !_development && preview.PhysicalPath is not null
            ? preview with { PhysicalPath = null }
            : preview;
        var payload = JsonSerializer.Serialize(
            new LearningPreviewSession(userId, itemId, safePreview));

        try
        {
            var result = await _redis!.ScriptEvaluateAsync(
                StoreScript,
                [SessionKey(digest), IndexKey()],
                [
                    payload,
                    expiresAt.ToUnixTimeMilliseconds(),
                    (long)Math.Ceiling(lifetime.TotalSeconds),
                    MaxSessions,
                    SessionPrefix(),
                    digest,
                    now.ToUnixTimeMilliseconds()
                ],
                cancellationToken);
            return (long)result == 1;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            throw new LearningPreviewSessionUnavailableException(
                "Redis preview session store is unavailable.",
                ex);
        }
    }

    public async Task<PreparedLearningPreview?> GetAsync(
        string token,
        int userId,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        if (!RedisEnabled)
        {
            return TryGet(token, userId, itemId, out var preview) ? preview : null;
        }

        try
        {
            var value = await _redis!.StringGetAsync(
                SessionKey(_keys!.HashSensitive(token)),
                cancellationToken);
            if (!value.HasValue) return null;

            var session = JsonSerializer.Deserialize<LearningPreviewSession>((string)value!);
            return session is not null &&
                   session.UserId == userId &&
                   session.ItemId == itemId
                ? session.Preview
                : null;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            throw new LearningPreviewSessionUnavailableException(
                "Redis preview session store is unavailable or damaged.",
                ex);
        }
    }

    public void Store(
        string token,
        int userId,
        int itemId,
        PreparedLearningPreview preview,
        TimeSpan lifetime)
    {
        _local.Set(
            LocalKey(token),
            new LearningPreviewSession(userId, itemId, preview),
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lifetime,
                Size = 1
            });
    }

    public bool TryGet(
        string token,
        int userId,
        int itemId,
        out PreparedLearningPreview? preview)
    {
        preview = null;
        if (!_local.TryGetValue<LearningPreviewSession>(LocalKey(token), out var session) ||
            session is null || session.UserId != userId || session.ItemId != itemId)
        {
            return false;
        }

        preview = session.Preview;
        return true;
    }

    public void Dispose() => _local.Dispose();

    private bool RedisEnabled =>
        _redisOptions?.Enabled == true && _redisOptions.Features.PreviewSessions;

    private RedisKey SessionKey(string digest) => _keys!.Build("learning", "preview", digest);

    private RedisKey IndexKey() => _keys!.Build("learning", "preview-index", "global");

    private string SessionPrefix() =>
        _keys!.BuildPrefix("learning", "preview");

    private string LocalKey(string token) => _keys?.HashSensitive(token) ??
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(token)));

    private sealed record LearningPreviewSession(
        int UserId,
        int ItemId,
        PreparedLearningPreview Preview);
}

public sealed class LearningPreviewSessionUnavailableException : Exception
{
    public LearningPreviewSessionUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
