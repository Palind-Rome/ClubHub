using ClubHub.Api.Data.Entities;

namespace ClubHub.Api.Services;

public static class LearningWorkflow
{
    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();

    public const string VisibilityClub = "club";
    public const string VisibilityPublic = "public";

    public const string ItemStatusDraft = "draft";
    public const string ItemStatusPublished = "published";
    public const string ItemStatusClosed = "closed";
    public const string ItemStatusFinished = "finished";

    public const string RecordStatusEnrolled = "enrolled";
    public const string RecordStatusLearning = "learning";
    public const string RecordStatusCompleted = "completed";
    public const string RecordStatusCancelled = "cancelled";

    private static readonly HashSet<string> SupportedCourseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "course",
        "lecture",
        "training"
    };

    public static bool IsCourseTimeValid(
        DateTime? enrollmentDeadline,
        DateTime? startAt,
        DateTime? endAt) =>
        enrollmentDeadline.HasValue &&
        startAt.HasValue &&
        endAt.HasValue &&
        enrollmentDeadline <= startAt &&
        endAt > startAt;

    public static bool IsCapacityValid(int? capacity) => capacity is > 0;

    public static bool HasEnrollmentCapacity(LearningItem item, int activeRecordCount) =>
        item.Capacity is > 0 && activeRecordCount < item.Capacity.Value;

    public static bool IsPublished(LearningItem item) =>
        NormalizeItemStatus(item.ItemStatus) == ItemStatusPublished;

    public static bool IsSupportedCourseType(string? itemType) =>
        SupportedCourseTypes.Contains(Normalize(itemType));

    public static bool IsVisibilityValid(string? visibility) =>
        NormalizeVisibility(visibility) is VisibilityClub or VisibilityPublic;

    public static string NormalizeVisibility(string? visibility) =>
        Normalize(visibility) switch
        {
            VisibilityPublic => VisibilityPublic,
            _ => VisibilityClub
        };

    public static bool IsEnrollmentWindowOpen(LearningItem item, DateTime now) =>
        IsPublished(item) &&
        item.EnrollmentDeadline != default &&
        now < item.EnrollmentDeadline;

    public static string ResolveEffectiveItemStatus(LearningItem item, DateTime now)
    {
        var status = NormalizeItemStatus(item.ItemStatus);
        if (status != ItemStatusPublished) return status;
        if (item.EndAt.HasValue && now >= item.EndAt.Value) return ItemStatusFinished;
        if (item.EnrollmentDeadline != default && now >= item.EnrollmentDeadline)
        {
            return ItemStatusClosed;
        }
        return ItemStatusPublished;
    }

    public static bool IsActiveRecordStatus(string? status) =>
        NormalizeRecordStatus(status) is RecordStatusEnrolled or RecordStatusLearning;

    public static bool IsProgressValid(decimal? progress) =>
        progress is null || progress is >= 0 and <= 100;

    public static string NormalizeItemStatus(string? status)
    {
        return Normalize(status) switch
        {
            "" => ItemStatusDraft,
            "published" or "open" => ItemStatusPublished,
            "closed" => ItemStatusClosed,
            "finished" => ItemStatusFinished,
            var value => value
        };
    }

    public static string NormalizeRecordStatus(string? status)
    {
        return Normalize(status) switch
        {
            "" => RecordStatusEnrolled,
            var value => value
        };
    }

    public static string ResolveRecordStatus(decimal? progress)
    {
        if (progress >= 100) return RecordStatusCompleted;
        if (progress > 0) return RecordStatusLearning;
        return RecordStatusEnrolled;
    }

    public static DateTime BusinessNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone);

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }
}
