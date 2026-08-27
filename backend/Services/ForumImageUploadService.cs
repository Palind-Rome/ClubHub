using System.Collections.Frozen;
using Microsoft.Extensions.Options;
using OSS = AlibabaCloud.OSS.V2;

namespace ClubHub.Api.Services;

public enum UploadFailureKind
{
    InvalidFile,
    TooLarge,
    Storage
}

public sealed record UploadResult(bool Success, string? ImageUrl, string? FileName, UploadFailureKind? FailureKind, string? ErrorMessage);

public sealed class ForumImageUploadService : IDisposable
{
    private static readonly FrozenSet<string> AllowedMimeTypes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp"
    );

    private static readonly FrozenSet<string> AllowedExtensions = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".webp"
    );

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const string LegacyReferenceScheme = "oss";
    private readonly OssStorageOptions _options;
    private readonly OSS.Client? _client;
    private readonly Exception? _configurationError;
    private readonly ILogger<ForumImageUploadService> _logger;

    public ForumImageUploadService(IOptions<OssStorageOptions> options, ILogger<ForumImageUploadService> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (!IsConfigured()) return;

        try
        {
            var credentialClient = new Aliyun.Credentials.Client(
                new Aliyun.Credentials.Models.Config
                {
                    Type = "ecs_ram_role",
                    RoleName = _options.RoleName.Trim()
                });
            var credentialsProvider = new OSS.Credentials.CredentialsProviderFunc(() =>
            {
                var credential = credentialClient.GetCredential();
                return new OSS.Credentials.Credentials(
                    credential.AccessKeyId,
                    credential.AccessKeySecret,
                    credential.SecurityToken);
            });

            var configuration = OSS.Configuration.LoadDefault();
            configuration.Region = _options.Region.Trim();
            configuration.Endpoint = NormalizeEndpoint(_options.Endpoint);
            configuration.CredentialsProvider = credentialsProvider;
            configuration.ConnectTimeout = TimeSpan.FromSeconds(10);
            configuration.ReadWriteTimeout = TimeSpan.FromMinutes(5);
            _client = new OSS.Client(configuration);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _configurationError = exception;
        }
    }

    /// <summary>
    /// 验证图片文件的合法性（MIME 类型、大小等）
    /// </summary>
    public (bool IsValid, UploadFailureKind? FailureKind, string? ErrorMessage) ValidateImage(IFormFile file)
    {
        if (file == null)
            return (false, UploadFailureKind.InvalidFile, "文件不存在");

        if (file.Length == 0)
            return (false, UploadFailureKind.InvalidFile, "文件大小为 0");

        if (file.Length > MaxFileSizeBytes)
            return (false, UploadFailureKind.TooLarge, $"文件过大，最大允许 {MaxFileSizeBytes / (1024 * 1024)} MB");

        var mimeType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedMimeTypes.Contains(mimeType))
            return (false, UploadFailureKind.InvalidFile, "不支持的文件类型，仅支持 jpg、png、gif、webp");

        var fileName = file.FileName?.ToLowerInvariant() ?? string.Empty;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return (false, UploadFailureKind.InvalidFile, "文件扩展名不合法");

        return (true, null, null);
    }

    /// <summary>
    /// 上传图片到 OSS
    /// </summary>
    public async Task<UploadResult> UploadAsync(
        int clubId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var (isValid, failureKind, errorMessage) = ValidateImage(file);
        if (!isValid)
            return new(false, null, null, failureKind, errorMessage);

        if (_client == null)
            return new(false, null, null, UploadFailureKind.Storage, "OSS 服务未正确配置");

        var fileName = file.FileName ?? "image.jpg";
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var objectName = $"clubs/{clubId}/forum/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}{extension}";

        try
        {
            using var stream = file.OpenReadStream();
            await _client.PutObjectAsync(
                new OSS.Models.PutObjectRequest
                {
                    Bucket = _options.Bucket,
                    Key = objectName,
                    Body = stream,
                    ContentType = file.ContentType ?? "image/jpeg",
                    ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(fileName)}"
                },
                cancellationToken: cancellationToken);

            var imageUrl = BuildImageUrl(objectName);
            return new(true, imageUrl, Path.GetFileName(objectName), null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "OSS upload failed for club {ClubId} file {FileName}", clubId, fileName);
            return new(false, null, null, UploadFailureKind.Storage, "上传失败");
        }
    }

    public void Dispose() => _client?.Dispose();

    private OSS.Client? GetClient() => _client;

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_options.Region) &&
        !string.IsNullOrWhiteSpace(_options.Endpoint) &&
        !string.IsNullOrWhiteSpace(_options.Bucket) &&
        !string.IsNullOrWhiteSpace(_options.RoleName);

    private string BuildImageUrl(string objectName)
    {
        var endpoint = NormalizeEndpoint(_options.Endpoint).TrimStart("https://".AsSpan()).TrimStart("http://".AsSpan());
        var bucket = _options.Bucket.Trim();
        return $"https://{bucket}.{endpoint}/{objectName}";
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Forum image OSS endpoint must use HTTPS or omit the scheme.");
        }

        return normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"https://{normalized}";
    }
}
