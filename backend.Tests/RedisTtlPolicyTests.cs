using ClubHub.Api.Infrastructure.Redis;
using Microsoft.Extensions.Options;

namespace ClubHub.Api.Tests;

public sealed class RedisTtlPolicyTests
{
    [Fact]
    public void GetExpirationUsesConfiguredDefaultAndLowerJitterBound()
    {
        var policy = CreatePolicy(sample: 0);

        var expiration = policy.GetExpiration();

        Assert.Equal(TimeSpan.FromSeconds(90), expiration);
    }

    [Fact]
    public void GetExpirationUsesNullTtlAndUpperJitterBound()
    {
        var policy = CreatePolicy(sample: 1);

        var expiration = policy.GetExpiration(isNullValue: true);

        Assert.Equal(TimeSpan.FromSeconds(11), expiration);
    }

    [Fact]
    public void GetExpirationAppliesJitterToRequestedTtl()
    {
        var policy = CreatePolicy(sample: 0.5);

        var expiration = policy.GetExpiration(TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), expiration);
    }

    [Fact]
    public void GetExpirationAppliesPerCacheJitterRatio()
    {
        var policy = CreatePolicy(sample: 0);

        var expiration = policy.GetExpiration(
            TimeSpan.FromMinutes(5),
            jitterRatio: 0.2);

        Assert.Equal(TimeSpan.FromMinutes(4), expiration);
    }

    [Fact]
    public void GetExpirationRejectsNonPositiveTtl()
    {
        var policy = CreatePolicy(sample: 0.5);

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetExpiration(TimeSpan.Zero));
    }

    private static RedisTtlPolicy CreatePolicy(double sample) =>
        new(
            Options.Create(
                new RedisOptions
                {
                    DefaultTtlSeconds = 100,
                    NullValueTtlSeconds = 10,
                    TtlJitterRatio = 0.1
                }),
            () => sample);
}
