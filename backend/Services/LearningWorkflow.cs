using ClubHub.Api.Data.Entities;

namespace ClubHub.Api.Services;

/// <summary>
/// 提供培训课程状态、报名窗口和学习进度的无状态业务规则。
/// </summary>
public static class LearningWorkflow
{
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
    public const string RecordStatusNone = "none";

    private static readonly HashSet<string> SupportedCourseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "course",
        "lecture",
        "training"
    };

    /// <summary>
    /// 校验报名截止、开始和结束时间的先后关系。
    /// </summary>
    public static bool IsCourseTimeValid(
        DateTime? enrollmentDeadline,
        DateTime? startAt,
        DateTime? endAt) =>
        enrollmentDeadline.HasValue &&
        startAt.HasValue &&
        endAt.HasValue &&
        enrollmentDeadline <= startAt &&
        endAt > startAt;

    /// <summary>
    /// 判断课程容量是否为正数。
    /// </summary>
    public static bool IsCapacityValid(int? capacity) => capacity is > 0;

    /// <summary>
    /// 判断课程是否仍有可用报名名额。
    /// </summary>
    public static bool HasEnrollmentCapacity(LearningItem item, int activeRecordCount) =>
        item.Capacity is > 0 && activeRecordCount < item.Capacity.Value;

    /// <summary>
    /// 判断课程是否处于发布状态。
    /// </summary>
    public static bool IsPublished(LearningItem item) =>
        NormalizeItemStatus(item.ItemStatus) == ItemStatusPublished;

    /// <summary>
    /// 判断课程类型是否属于系统支持的类型。
    /// </summary>
    public static bool IsSupportedCourseType(string? itemType) =>
        SupportedCourseTypes.Contains(Normalize(itemType));

    /// <summary>
    /// 判断课程开放范围是否有效。
    /// </summary>
    public static bool IsVisibilityValid(string? visibility) =>
        NormalizeVisibility(visibility) is VisibilityClub or VisibilityPublic;

    /// <summary>
    /// 将开放范围归一化为本社团或全校。
    /// </summary>
    public static string NormalizeVisibility(string? visibility) =>
        Normalize(visibility) switch
        {
            VisibilityPublic => VisibilityPublic,
            _ => VisibilityClub
        };

    /// <summary>
    /// 判断当前 UTC 时间是否仍处于课程报名窗口。
    /// </summary>
    public static bool IsEnrollmentWindowOpen(LearningItem item, DateTime now) =>
        IsPublished(item) &&
        item.EnrollmentDeadline != default &&
        now < item.EnrollmentDeadline;

    /// <summary>
    /// 根据课程时间和保存状态计算对外展示的有效状态。
    /// </summary>
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

    /// <summary>
    /// 判断学习记录是否占用课程名额。
    /// </summary>
    public static bool IsActiveRecordStatus(string? status) =>
        NormalizeRecordStatus(status) is RecordStatusEnrolled or RecordStatusLearning;

    /// <summary>
    /// 判断学习进度是否处于百分比有效范围。
    /// </summary>
    public static bool IsProgressValid(decimal? progress) =>
        progress is null || progress is >= 0 and <= 100;

    /// <summary>
    /// 归一化课程状态并兼容历史 open 值。
    /// </summary>
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

    /// <summary>
    /// 归一化学习记录状态，空值按已报名处理。
    /// </summary>
    public static string NormalizeRecordStatus(string? status)
    {
        return Normalize(status) switch
        {
            "" => RecordStatusEnrolled,
            var value => value
        };
    }

    /// <summary>
    /// 根据学习进度计算报名、学习中或完成状态。
    /// </summary>
    public static string ResolveRecordStatus(decimal? progress)
    {
        if (progress >= 100) return RecordStatusCompleted;
        if (progress > 0) return RecordStatusLearning;
        return RecordStatusEnrolled;
    }

    /// <summary>
    /// 返回业务计算和持久化统一使用的 UTC 当前时间。
    /// </summary>
    public static DateTime BusinessNow() => DateTime.UtcNow;

    /// <summary>
    /// 将数据库读取的无时区时间标记为 UTC，避免序列化时被客户端按本地时间误解。
    /// </summary>
    public static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    /// <summary>
    /// 将可空数据库时间统一转换为 UTC。
    /// </summary>
    public static DateTime? AsUtc(DateTime? value) =>
        value.HasValue ? AsUtc(value.Value) : null;

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
