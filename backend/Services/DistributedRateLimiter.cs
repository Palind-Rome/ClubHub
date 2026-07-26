using ClubHub.Api.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Services;

public interface IDistributedRateLimiter
{
    bool Enabled { get; }

    Task<RateLimitDecision> AcquireAsync(
        string policy,
        string subject,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default);

    Task ResetAsync(
        string policy,
        string subject,
        CancellationToken cancellationToken = default);
}

public readonly record struct RateLimitDecision(bool Allowed, int Remaining, int RetryAfterSeconds);

public sealed class DistributedRateLimiter : IDistributedRateLimiter
{
    private const string AcquireScript = """
        local current = redis.call('incr', KEYS[1])
        if current == 1 then redis.call('expire', KEYS[1], ARGV[1]) end
        local ttl = redis.call('ttl', KEYS[1])
        local remaining = tonumber(ARGV[2]) - current
        if remaining < 0 then remaining = 0 end
        return {current <= tonumber(ARGV[2]) and 1 or 0, remaining, ttl}
        """;

    private readonly IRedisDatabase _redis;
    private readonly IRedisKeyBuilder _keys;
    private readonly RedisOptions _options;

    public DistributedRateLimiter(
        IRedisDatabase redis,
        IRedisKeyBuilder keys,
        IOptions<RedisOptions> options)
    {
        _redis = redis;
        _keys = keys;
        _options = options.Value;
    }

    public bool Enabled => _options.Enabled && _options.Features.RateLimiting;

    public async Task<RateLimitDecision> AcquireAsync(
        string policy,
        string subject,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) return new RateLimitDecision(true, limit, 0);

        try
        {
            var result = (RedisResult[]?)await _redis.ScriptEvaluateAsync(
                AcquireScript,
                [_keys.Build("rate-limit", policy, _keys.HashSensitive(subject))],
                [(long)Math.Ceiling(window.TotalSeconds), limit],
                cancellationToken);
            if (result is null || result.Length != 3)
            {
                throw new RedisException("Unexpected fixed-window rate-limit response.");
            }

            return new RateLimitDecision(
                (long)result[0] == 1,
                checked((int)(long)result[1]),
                Math.Max(1, checked((int)(long)result[2])));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            throw new RateLimitUnavailableException("Distributed rate limiter is unavailable.", ex);
        }
    }

    public async Task ResetAsync(
        string policy,
        string subject,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled) return;
        try
        {
            await _redis.KeyDeleteAsync(
                _keys.Build("rate-limit", policy, _keys.HashSensitive(subject)),
                cancellationToken);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            throw new RateLimitUnavailableException("Distributed rate limiter is unavailable.", ex);
        }
    }
}

public sealed class RateLimitUnavailableException : Exception
{
    public RateLimitUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
