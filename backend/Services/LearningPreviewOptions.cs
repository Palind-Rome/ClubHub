namespace ClubHub.Api.Services;

public sealed class LearningPreviewOptions
{
    public const string SectionName = "LearningPreview";

    public string OfficeExecutablePath { get; init; } = "soffice";

    /// <summary>
    /// 是否允许在本地开发环境中运行 Office 转换；生产环境始终禁用。
    /// </summary>
    public bool EnableOfficeConversion { get; init; }

    public int ConversionTimeoutSeconds { get; init; } = 60;

    public long MaxInputBytes { get; init; } = 50L * 1024 * 1024;

    public long MaxOutputBytes { get; init; } = 100L * 1024 * 1024;

    public long MaxWorkingSetBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>
    /// 单个 API 进程允许同时运行的 Office 转换数量。
    /// </summary>
    public int MaxConcurrentConversions { get; init; } = 2;

    public int SessionLifetimeMinutes { get; init; } = 30;
}
