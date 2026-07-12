namespace ClubHub.Api.Services;

public sealed class OssStorageOptions
{
    public const string SectionName = "Oss";

    public string Region { get; init; } = string.Empty;

    public string Endpoint { get; init; } = string.Empty;

    public string Bucket { get; init; } = string.Empty;

    public string RoleName { get; init; } = string.Empty;
}
