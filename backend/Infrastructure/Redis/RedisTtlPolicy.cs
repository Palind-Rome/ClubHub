using Microsoft.Extensions.Options;

namespace ClubHub.Api.Infrastructure.Redis;

public interface IRedisTtlPolicy
{
    TimeSpan GetExpiration(
        TimeSpan? requestedTtl = null,
        bool isNullValue = false,
        double? jitterRatio = null);
}

public sealed class RedisTtlPolicy : IRedisTtlPolicy
{
    private readonly RedisOptions _options;
    private readonly Func<double> _nextSample;

    public RedisTtlPolicy(IOptions<RedisOptions> options)
        : this(options, () => Random.Shared.NextDouble())
    {
    }

    internal RedisTtlPolicy(IOptions<RedisOptions> options, Func<double> nextSample)
    {
        _options = options.Value;
        _nextSample = nextSample;
    }

    public TimeSpan GetExpiration(
        TimeSpan? requestedTtl = null,
        bool isNullValue = false,
        double? jitterRatio = null)
    {
        var baseTtl = requestedTtl ??
            TimeSpan.FromSeconds(
                isNullValue
                    ? _options.NullValueTtlSeconds
                    : _options.DefaultTtlSeconds);

        if (baseTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedTtl),
                "Cache TTL must be greater than zero.");
        }

        var effectiveJitterRatio = jitterRatio ?? _options.TtlJitterRatio;
        if (effectiveJitterRatio is < 0 or > 0.5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(jitterRatio),
                "Cache TTL jitter ratio must be between zero and 0.5.");
        }

        var sample = Math.Clamp(_nextSample(), 0, 1);
        var jitterMultiplier = 1 + ((sample * 2 - 1) * effectiveJitterRatio);
        var jitteredMilliseconds = Math.Max(1_000, baseTtl.TotalMilliseconds * jitterMultiplier);
        return TimeSpan.FromMilliseconds(jitteredMilliseconds);
    }
}
