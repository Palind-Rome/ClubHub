namespace ClubHub.Api.Services;

public sealed class OssStorageOptions
{
    public const string SectionName = "Oss";

    public string Region { get; init; } = "cn-shanghai";

    public string Endpoint { get; init; } = "oss-cn-shanghai-internal.aliyuncs.com";

    public string Bucket { get; init; } = "clubhub-learning-prod-2026-c7h2";

    public string RoleName { get; init; } = "ClubHubOssProdRole";
}
