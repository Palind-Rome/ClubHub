namespace ClubHub.Api.Services;

public sealed class OssStorageOptions
{
    public const string SectionName = "Oss";

    public string Region { get; init; } = string.Empty;

    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// 用于生成浏览器直连签名 URL 的公网 Endpoint；为空时由 Endpoint 自动推导。
    /// </summary>
    public string PublicEndpoint { get; init; } = string.Empty;

    public string Bucket { get; init; } = string.Empty;

    public string RoleName { get; init; } = string.Empty;
}
