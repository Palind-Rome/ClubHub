namespace ClubHub.Api.Services;

public sealed class LearningPreviewOptions
{
    public const string SectionName = "LearningPreview";

    public string OfficeExecutablePath { get; init; } = "soffice";

    public int ConversionTimeoutSeconds { get; init; } = 60;

    public long MaxInputBytes { get; init; } = 50L * 1024 * 1024;

    public long MaxOutputBytes { get; init; } = 100L * 1024 * 1024;

    public long MaxWorkingSetBytes { get; init; } = 512L * 1024 * 1024;

    public int SessionLifetimeMinutes { get; init; } = 30;
}
