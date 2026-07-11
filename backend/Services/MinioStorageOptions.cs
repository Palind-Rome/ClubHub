namespace ClubHub.Api.Services;

public sealed class MinioStorageOptions
{
    public const string SectionName = "Minio";

    public string Endpoint { get; init; } = string.Empty;

    public string PublicEndpoint { get; init; } = string.Empty;

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public string Bucket { get; init; } = "clubhub-learning";

    public bool UseSsl { get; init; } = true;

    public bool? PublicUseSsl { get; init; }

    public bool AutoCreateBucket { get; init; }

    public int DownloadUrlExpirySeconds { get; init; } = 300;
}
