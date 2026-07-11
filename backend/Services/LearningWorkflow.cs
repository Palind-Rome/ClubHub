using ClubHub.Api.Data.Entities;

namespace ClubHub.Api.Services;

/// <summary>
/// 提供培训课程、学习资源、可见范围、下载设置和学习进度的无状态业务规则。
/// </summary>
public static class LearningWorkflow
{
    public const string VisibilityClub = "club";
    public const string VisibilityPublic = "public";
    public const string VisibilityDepartment = "department";

    public const string DownloadPermissionAllow = "allow";
    public const string DownloadPermissionDeny = "deny";
    public const string DownloadPermissionApproval = "approval";

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

    private static readonly HashSet<string> SupportedResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video",
        "document",
        "material"
    };

    /// <summary>
    /// 校验课程开始时间必填，且可选结束时间晚于开始时间。
    /// </summary>
    public static bool IsCourseTimeValid(
        DateTime? startAt,
        DateTime? endAt) =>
        startAt.HasValue &&
        (!endAt.HasValue || endAt > startAt);

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
    /// 判断类型是否属于视频、文档或资料资源。
    /// </summary>
    public static bool IsSupportedResourceType(string? itemType) =>
        SupportedResourceTypes.Contains(Normalize(itemType));

    /// <summary>
    /// 判断资源类型是否属于系统支持范围。
    /// </summary>
    public static bool IsSupportedItemType(string? itemType) =>
        IsSupportedCourseType(itemType) || IsSupportedResourceType(itemType);

    /// <summary>
    /// 判断课程开放范围是否有效。
    /// </summary>
    public static bool IsVisibilityValid(string? visibility) =>
        Normalize(visibility) is VisibilityClub or VisibilityPublic or VisibilityDepartment;

    /// <summary>
    /// 将合法开放范围归一化为本社团或全校；非法值返回空。
    /// </summary>
    public static string? NormalizeVisibility(string? visibility) =>
        Normalize(visibility) switch
        {
            VisibilityClub => VisibilityClub,
            VisibilityPublic => VisibilityPublic,
            VisibilityDepartment => VisibilityDepartment,
            _ => null
        };

    /// <summary>
    /// 判断资源下载设置是否有效。
    /// </summary>
    public static bool IsDownloadPermissionValid(string? permission) =>
        NormalizeDownloadPermission(permission) is not null;

    /// <summary>
    /// 归一化资源下载设置；兼容历史 none 值为禁止下载。
    /// </summary>
    public static string? NormalizeDownloadPermission(string? permission) =>
        Normalize(permission) switch
        {
            DownloadPermissionAllow => DownloadPermissionAllow,
            DownloadPermissionDeny or "none" or "" => DownloadPermissionDeny,
            DownloadPermissionApproval => DownloadPermissionApproval,
            _ => null
        };

    /// <summary>
    /// 校验文件地址为 HTTP/HTTPS 绝对地址，或本系统受鉴权的上传文件地址和 MinIO 对象引用。
    /// </summary>
    public static bool IsFileUrlValid(string? fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl) || fileUrl.Trim().Length > 255) return false;
        var normalized = fileUrl.Trim();
        var localSegments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (localSegments is ["api", "learning", "items", var itemId, "file"] &&
            int.TryParse(itemId, out var parsedItemId) &&
            parsedItemId > 0)
        {
            return true;
        }
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) return false;
        if (string.Equals(uri.Scheme, "minio", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(uri.Host) &&
                   !string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')) &&
                   !Uri.UnescapeDataString(uri.AbsolutePath).Contains("..", StringComparison.Ordinal);
        }
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断课程是否开放加入；未设置结束时间时长期开放。
    /// </summary>
    public static bool IsEnrollmentWindowOpen(LearningItem item, DateTime now) =>
        IsPublished(item) &&
        (!item.EndAt.HasValue || now < item.EndAt.Value);

    /// <summary>
    /// 根据课程时间和保存状态计算对外展示的有效状态。
    /// </summary>
    public static string ResolveEffectiveItemStatus(LearningItem item, DateTime now)
    {
        var status = NormalizeItemStatus(item.ItemStatus);
        if (status != ItemStatusPublished) return status;
        if (item.EndAt.HasValue && now >= item.EndAt.Value) return ItemStatusFinished;
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
