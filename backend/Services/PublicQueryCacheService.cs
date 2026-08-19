using ClubHub.Api.Data;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace ClubHub.Api.Services;

public sealed class PublicQueryCacheService
{
    private static readonly RedisCachePolicy ActivityDetailPolicy = new(
        "activity-detail",
        TimeSpan.FromMinutes(2),
        TimeSpan.FromSeconds(30));

    private static readonly RedisCachePolicy VenueDetailPolicy = new(
        "venue-detail",
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(30));

    private readonly ClubHubDbContext _db;
    private readonly IRedisCacheService _cache;
    private readonly IRedisKeyBuilder _keyBuilder;

    public PublicQueryCacheService(
        ClubHubDbContext db,
        IRedisCacheService cache,
        IRedisKeyBuilder keyBuilder)
    {
        _db = db;
        _cache = cache;
        _keyBuilder = keyBuilder;
    }

    public Task<ActivityPublicCacheEntry?> GetActivityAsync(
        int activityId,
        CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            ActivityKey(activityId),
            token => LoadActivityAsync(activityId, token),
            ActivityDetailPolicy,
            cancellationToken);

    public Task<VenuePublicCacheEntry?> GetVenueAsync(
        int venueId,
        CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync(
            VenueKey(venueId),
            token => LoadVenueAsync(venueId, token),
            VenueDetailPolicy,
            cancellationToken);

    public Task<RedisCacheWriteStatus> InvalidateActivityAsync(
        int activityId,
        CancellationToken cancellationToken = default) =>
        _cache.RemoveAsync(ActivityKey(activityId), cancellationToken);

    public Task<RedisCacheWriteStatus> InvalidateVenueAsync(
        int venueId,
        CancellationToken cancellationToken = default) =>
        _cache.RemoveAsync(VenueKey(venueId), cancellationToken);

    private RedisKey ActivityKey(int activityId) =>
        _keyBuilder.Build("activity", "detail", activityId);

    private RedisKey VenueKey(int venueId) =>
        _keyBuilder.Build("venue", "detail", venueId);

    private Task<ActivityPublicCacheEntry?> LoadActivityAsync(
        int activityId,
        CancellationToken cancellationToken) =>
        _db.Activities
            .AsNoTracking()
            .Where(activity => activity.ActivityId == activityId)
            .Select(activity => new ActivityPublicCacheEntry(
                activity.ActivityId,
                activity.Title,
                activity.ActivityType,
                activity.Description,
                activity.Club != null ? activity.Club.ClubName : "",
                activity.ClubId,
                activity.CreatorUserId,
                activity.StartAt,
                activity.EndAt,
                activity.Location,
                activity.ActivityStatus,
                activity.Capacity,
                activity.RegistrationDeadline,
                activity.ReviewerUserId,
                activity.ReviewComment,
                activity.BudgetAmount,
                activity.BudgetPurpose,
                activity.BudgetDetail,
                activity.BudgetStatus,
                activity.BudgetReviewerId,
                activity.BudgetComment,
                activity.PublishedAt,
                activity.CheckinStartAt,
                activity.CheckinEndAt,
                activity.CheckoutStartAt,
                activity.CheckoutEndAt))
            .FirstOrDefaultAsync(cancellationToken);

    private Task<VenuePublicCacheEntry?> LoadVenueAsync(
        int venueId,
        CancellationToken cancellationToken) =>
        _db.Venues
            .AsNoTracking()
            .Where(venue => venue.VenueId == venueId)
            .Select(venue => new VenuePublicCacheEntry(
                venue.VenueId,
                venue.VenueName ?? "",
                venue.Building,
                venue.RoomNo,
                venue.Capacity ?? 0,
                venue.VenueStatus,
                venue.ManagerUserId,
                venue.CreatedAt ?? DateTime.MinValue))
            .FirstOrDefaultAsync(cancellationToken);
}

public sealed record ActivityPublicCacheEntry(
    int Id,
    string Title,
    string? ActivityType,
    string? Description,
    string ClubName,
    int ClubId,
    int? CreatorUserId,
    DateTime? StartTime,
    DateTime? EndTime,
    string? Location,
    string? Status,
    int? MaxParticipants,
    DateTime? RegistrationDeadline,
    int? ReviewerUserId,
    string? ReviewComment,
    decimal? BudgetAmount,
    string? BudgetPurpose,
    string? BudgetDetail,
    string? BudgetStatus,
    int? BudgetReviewerId,
    string? BudgetComment,
    DateTime? PublishedAt,
    DateTime? CheckinStartAt,
    DateTime? CheckinEndAt,
    DateTime? CheckoutStartAt,
    DateTime? CheckoutEndAt);

public sealed record VenuePublicCacheEntry(
    int Id,
    string Name,
    string? Building,
    string? RoomNo,
    int Capacity,
    string? Status,
    int? ManagerUserId,
    DateTime CreatedAt);
