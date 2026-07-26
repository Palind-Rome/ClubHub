using System.Text.Json;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Services;

public interface IAuthSessionService
{
    bool Enabled { get; }

    Task CreateAsync(string token, AuthTokenPrincipal principal, CancellationToken cancellationToken = default);

    Task<AuthSessionValidation> ValidateAndRefreshAsync(
        string token,
        AuthTokenPrincipal principal,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(string token, AuthTokenPrincipal principal, CancellationToken cancellationToken = default);

    Task RevokeAllAsync(int userId, CancellationToken cancellationToken = default);
}

public enum AuthSessionValidation
{
    Valid,
    Missing,
    Unavailable
}

public sealed class AuthSessionService : IAuthSessionService
{
    private const string CreateScript = """
        redis.call('set', KEYS[1], ARGV[1], 'EX', ARGV[2])
        redis.call('zadd', KEYS[2], ARGV[3], ARGV[4])
        local count = redis.call('zcard', KEYS[2])
        if count > tonumber(ARGV[5]) then
          local evicted = redis.call('zrange', KEYS[2], 0, count - tonumber(ARGV[5]) - 1)
          for _, digest in ipairs(evicted) do
            redis.call('del', ARGV[6] .. digest)
            redis.call('zrem', KEYS[2], digest)
          end
        end
        redis.call('expire', KEYS[2], ARGV[7])
        return 1
        """;

    private const string RefreshScript = """
        local payload = redis.call('get', KEYS[1])
        if not payload then return nil end
        redis.call('expire', KEYS[1], ARGV[1])
        return payload
        """;

    private const string RevokeAllScript = """
        local digests = redis.call('zrange', KEYS[1], 0, -1)
        for _, digest in ipairs(digests) do
          redis.call('del', ARGV[1] .. digest)
        end
        redis.call('del', KEYS[1])
        return #digests
        """;

    private readonly IRedisDatabase _redis;
    private readonly IRedisKeyBuilder _keys;
    private readonly RedisOptions _redisOptions;
    private readonly AuthSessionOptions _options;
    private readonly ILogger<AuthSessionService> _logger;

    public AuthSessionService(
        IRedisDatabase redis,
        IRedisKeyBuilder keys,
        IOptions<RedisOptions> redisOptions,
        IOptions<AuthSessionOptions> options,
        ILogger<AuthSessionService> logger)
    {
        _redis = redis;
        _keys = keys;
        _redisOptions = redisOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _redisOptions.Enabled && _redisOptions.Features.AuthSessions;

    public async Task CreateAsync(
        string token,
        AuthTokenPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) return;

        var now = DateTimeOffset.UtcNow;
        var ttl = GetRemainingTtl(principal, now);
        if (ttl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Cannot create an already expired authentication session.");
        }

        var digest = _keys.HashSensitive(token);
        var sessionKey = SessionKey(digest);
        var userIndexKey = UserIndexKey(principal.UserId);
        var payload = JsonSerializer.Serialize(new AuthSessionPayload(
            principal.UserId,
            principal.SessionId,
            principal.IssuedAt,
            principal.ExpiresAt));
        var indexTtl = TimeSpan.FromHours(_options.AbsoluteLifetimeHours + 1);

        await _redis.ScriptEvaluateAsync(
            CreateScript,
            [sessionKey, userIndexKey],
            [
                payload,
                (long)Math.Ceiling(ttl.TotalSeconds),
                principal.IssuedAt.ToUnixTimeMilliseconds(),
                digest,
                _options.MaxSessionsPerUser,
                SessionPrefix(),
                (long)Math.Ceiling(indexTtl.TotalSeconds)
            ],
            cancellationToken);
    }

    public async Task<AuthSessionValidation> ValidateAndRefreshAsync(
        string token,
        AuthTokenPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) return AuthSessionValidation.Valid;

        try
        {
            var ttl = GetRemainingTtl(principal, DateTimeOffset.UtcNow);
            if (ttl <= TimeSpan.Zero) return AuthSessionValidation.Missing;

            var digest = _keys.HashSensitive(token);
            var result = await _redis.ScriptEvaluateAsync(
                RefreshScript,
                [SessionKey(digest)],
                [(long)Math.Ceiling(ttl.TotalSeconds)],
                cancellationToken);
            if (result.IsNull) return AuthSessionValidation.Missing;

            var payload = JsonSerializer.Deserialize<AuthSessionPayload>((string)result!);
            return payload is not null &&
                   payload.UserId == principal.UserId &&
                   payload.SessionId == principal.SessionId &&
                   payload.ExpiresAt == principal.ExpiresAt
                ? AuthSessionValidation.Valid
                : AuthSessionValidation.Missing;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            _logger.LogWarning(ex, "Redis could not validate authentication session.");
            return AuthSessionValidation.Unavailable;
        }
    }

    public async Task RevokeAsync(
        string token,
        AuthTokenPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) return;

        var digest = _keys.HashSensitive(token);
        const string script = """
            redis.call('del', KEYS[1])
            redis.call('zrem', KEYS[2], ARGV[1])
            return 1
            """;
        await _redis.ScriptEvaluateAsync(
            script,
            [SessionKey(digest), UserIndexKey(principal.UserId)],
            [digest],
            cancellationToken);
    }

    public async Task RevokeAllAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return;

        await _redis.ScriptEvaluateAsync(
            RevokeAllScript,
            [UserIndexKey(userId)],
            [SessionPrefix()],
            cancellationToken);
    }

    private TimeSpan GetRemainingTtl(AuthTokenPrincipal principal, DateTimeOffset now)
    {
        var sliding = TimeSpan.FromMinutes(_options.SlidingLifetimeMinutes);
        var absoluteRemaining = principal.ExpiresAt - now;
        return absoluteRemaining < sliding ? absoluteRemaining : sliding;
    }

    private RedisKey SessionKey(string digest) => _keys.Build("auth", "session", digest);

    private RedisKey UserIndexKey(int userId) => _keys.Build("auth", "user-sessions", userId.ToString());

    private string SessionPrefix() => _keys.BuildPrefix("auth", "session");

    private sealed record AuthSessionPayload(
        int UserId,
        string SessionId,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt);
}
