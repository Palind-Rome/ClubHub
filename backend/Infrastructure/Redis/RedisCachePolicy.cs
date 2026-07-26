namespace ClubHub.Api.Infrastructure.Redis;

public sealed record RedisCachePolicy(
    string Name,
    TimeSpan Ttl,
    TimeSpan NullTtl,
    double TtlJitterRatio = 0.2,
    double NullTtlJitterRatio = 0.1,
    TimeSpan? RebuildLeaseTtl = null,
    TimeSpan? RebuildWaitTimeout = null,
    TimeSpan? RebuildPollInterval = null)
{
    public TimeSpan EffectiveRebuildLeaseTtl =>
        RebuildLeaseTtl ?? TimeSpan.FromSeconds(5);

    public TimeSpan EffectiveRebuildWaitTimeout =>
        RebuildWaitTimeout ?? TimeSpan.FromSeconds(1);

    public TimeSpan EffectiveRebuildPollInterval =>
        RebuildPollInterval ?? TimeSpan.FromMilliseconds(50);

    public void Validate()
    {
        if (!RedisKeyBuilder.IsValidNamespaceSegment(Name))
        {
            throw new ArgumentException(
                "Cache policy names must be lowercase namespace segments.",
                nameof(Name));
        }

        if (Ttl <= TimeSpan.Zero || NullTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Ttl),
                "Cache and null TTL values must be greater than zero.");
        }

        if (TtlJitterRatio is < 0 or > 0.5 ||
            NullTtlJitterRatio is < 0 or > 0.5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TtlJitterRatio),
                "Cache TTL jitter ratios must be between zero and 0.5.");
        }

        if (EffectiveRebuildLeaseTtl <= TimeSpan.Zero ||
            EffectiveRebuildWaitTimeout < TimeSpan.Zero ||
            EffectiveRebuildPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RebuildLeaseTtl),
                "Cache rebuild timing values must be valid positive durations.");
        }
    }
}
