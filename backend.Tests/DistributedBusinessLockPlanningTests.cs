using ClubHub.Api.Controllers;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.Extensions.Options;

namespace ClubHub.Api.Tests;

public sealed class DistributedBusinessLockPlanningTests
{
    private static readonly IRedisKeyBuilder Keys = new RedisKeyBuilder(Options.Create(
        new RedisOptions { EnvironmentPrefix = "test" }));

    [Fact]
    public void VenueDatesUseBeijingCalendarAcrossUtcDayBoundary()
    {
        var keys = VenueReservationsController.BuildVenueDateLockKeys(
            Keys,
            42,
            new DateTime(2026, 7, 28, 15, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 28, 17, 30, 0, DateTimeKind.Utc));

        Assert.Equal(
            [
                "clubhub:test:venue:lock:v1:42:2026-07-28",
                "clubhub:test:venue:lock:v1:42:2026-07-29"
            ],
            keys.Select(key => key.ToString()));
    }

    [Fact]
    public void VenueDatesDoNotLockNextDayWhenEndIsBeijingMidnight()
    {
        var keys = VenueReservationsController.BuildVenueDateLockKeys(
            Keys,
            42,
            new DateTime(2026, 7, 28, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 28, 16, 0, 0, DateTimeKind.Utc));

        Assert.Equal(
            ["clubhub:test:venue:lock:v1:42:2026-07-28"],
            keys.Select(key => key.ToString()));
    }

    [Fact]
    public void VenueDatesAllowAtMostThirtyOneBeijingCalendarDays()
    {
        var keys = VenueReservationsController.BuildVenueDateLockKeys(
            Keys,
            42,
            new DateTime(2026, 6, 30, 16, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 31, 16, 0, 0, DateTimeKind.Utc));

        Assert.Equal(VenueReservationsController.MaxReservationLockedDays, keys.Count);
    }

    [Fact]
    public void VenueDatesRejectMoreThanThirtyOneBeijingCalendarDays()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            VenueReservationsController.BuildVenueDateLockKeys(
                Keys,
                42,
                new DateTime(2026, 6, 30, 16, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 1, 16, 0, 0, DateTimeKind.Utc)));

        Assert.Contains("31", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2025-2026学年春季", "2026春季")]
    [InlineData("2026-2027学年秋季", "2026秋季")]
    public void EvaluationTermAliasesShareOneWindowIdentity(string first, string second)
    {
        Assert.Equal(
            ClubsController.EvaluationTermWindowIdentity(first),
            ClubsController.EvaluationTermWindowIdentity(second));
        Assert.Equal(
            ClubsController.EvaluationTermPrefilterToken(first),
            ClubsController.EvaluationTermPrefilterToken(second));
        Assert.Equal("2026", ClubsController.EvaluationTermPrefilterToken(first));
    }
}
