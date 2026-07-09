using ClubHub.Api.Data.Entities;

namespace ClubHub.Api.Services;

public static class LearningWorkflow
{
    public const string ItemStatusDraft = "draft";
    public const string ItemStatusPublished = "published";
    public const string ItemStatusClosed = "closed";
    public const string ItemStatusFinished = "finished";

    public const string RecordStatusEnrolled = "enrolled";
    public const string RecordStatusLearning = "learning";
    public const string RecordStatusCompleted = "completed";
    public const string RecordStatusCancelled = "cancelled";

    public static bool IsCourseTimeValid(DateTime? startAt, DateTime? endAt) =>
        startAt is null || endAt is null || endAt > startAt;

    public static bool IsCapacityValid(int? capacity) => capacity is null or > 0;

    public static bool HasEnrollmentCapacity(LearningItem item, int activeRecordCount) =>
        item.Capacity is null || activeRecordCount < item.Capacity.Value;

    public static bool IsPublished(LearningItem item) =>
        NormalizeItemStatus(item.ItemStatus) == ItemStatusPublished;

    public static bool IsActiveRecordStatus(string? status)
    {
        return NormalizeRecordStatus(status) is RecordStatusEnrolled or RecordStatusLearning;
    }

    public static bool IsProgressValid(decimal? progress) =>
        progress is null || progress is >= 0 and <= 100;

    public static string NormalizeItemStatus(string? status)
    {
        return (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => ItemStatusDraft,
            "草稿" => ItemStatusDraft,
            "published" or "open" or "已发布" or "报名中" => ItemStatusPublished,
            "closed" or "已关闭" or "停止报名" => ItemStatusClosed,
            "finished" or "已结束" or "完成" => ItemStatusFinished,
            var value => value
        };
    }

    public static string NormalizeRecordStatus(string? status)
    {
        return (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => RecordStatusEnrolled,
            "已报名" => RecordStatusEnrolled,
            "学习中" => RecordStatusLearning,
            "已完成" => RecordStatusCompleted,
            "已取消" => RecordStatusCancelled,
            var value => value
        };
    }

    public static string ResolveRecordStatus(decimal? progress)
    {
        if (progress >= 100) return RecordStatusCompleted;
        if (progress > 0) return RecordStatusLearning;
        return RecordStatusEnrolled;
    }
}
