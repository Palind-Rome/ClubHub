using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OSS = AlibabaCloud.OSS.V2;

namespace ClubHub.Api.Services;

public enum UploadFailureKind
{
    InvalidFile,
    TooLarge,
    Storage
}

public sealed record UploadResult(bool Success, string? ImageUrl, string? FileName, string? StorageKey, UploadFailureKind? FailureKind, string? ErrorMessage);

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

    private static readonly Regex MarkdownImagePattern = new(
        @"!\[[^\]]*\]\((?<url><[^>\r\n]+>|[^)\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly TimeSpan SignedUrlLifetime = TimeSpan.FromHours(2);
    private readonly OssStorageOptions _options;
    private readonly OSS.Client? _client;
    private readonly OSS.Client? _publicUrlClient;
    private readonly ILogger<ForumImageUploadService> _logger;

    public ForumImageUploadService(IOptions<OssStorageOptions> options, ILogger<ForumImageUploadService> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (!IsConfigured()) return;

        OSS.Client? uploadClient = null;
        OSS.Client? publicUrlClient = null;
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

            uploadClient = CreateClient(_options.Region, _options.Endpoint, credentialsProvider);
            publicUrlClient = CreateClient(_options.Region, GetPublicEndpoint(), credentialsProvider);
            _client = uploadClient;
            _publicUrlClient = publicUrlClient;
        }
        catch (InvalidOperationException)
        {
            uploadClient?.Dispose();
            publicUrlClient?.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            uploadClient?.Dispose();
            publicUrlClient?.Dispose();
            _logger.LogError(exception, "Forum image OSS client initialization failed");
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
    /// 上传图片到 OSS。对象保持私有，返回的图片地址为短期签名 URL。
    /// </summary>
    public async Task<UploadResult> UploadAsync(
        int clubId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var (isValid, failureKind, errorMessage) = ValidateImage(file);
        if (!isValid)
            return new(false, null, null, null, failureKind, errorMessage);

        if (_client == null || _publicUrlClient == null)
            return new(false, null, null, null, UploadFailureKind.Storage, "OSS 服务未正确配置");

        var fileName = file.FileName ?? "image.jpg";
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var objectName = $"clubs/{clubId}/forum/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}{extension}";
        var objectUploaded = false;

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
            objectUploaded = true;

            var imageUrl = BuildSignedImageUrl(objectName);
            return new(true, imageUrl, Path.GetFileName(objectName), objectName, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (objectUploaded)
            {
                try
                {
                    await _client.DeleteObjectAsync(
                        new OSS.Models.DeleteObjectRequest
                        {
                            Bucket = _options.Bucket,
                            Key = objectName
                        },
                        cancellationToken: CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(cleanupException, "Failed to clean up forum image after signing failure for key {StorageKey}", objectName);
                }
            }

            _logger.LogError(exception, "OSS upload failed for club {ClubId} file {FileName}", clubId, fileName);
            return new(false, null, null, null, UploadFailureKind.Storage, "上传失败");
        }
    }

    /// <summary>
    /// 删除已上传的图片。对象键必须属于当前社团的论坛目录。
    /// </summary>
    public async Task<bool> DeleteAsync(int clubId, string? storageKey, CancellationToken cancellationToken = default)
    {
        var normalizedStorageKey = storageKey?.Trim();
        if (!IsForumStorageKey(clubId, normalizedStorageKey) || _client == null)
            return false;

        try
        {
            await _client.DeleteObjectAsync(
                new OSS.Models.DeleteObjectRequest
                {
                    Bucket = _options.Bucket,
                    Key = normalizedStorageKey!
                },
                cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "OSS delete failed for key {StorageKey}", storageKey);
            return false;
        }
    }

    /// <summary>
    /// 将帖子正文中的论坛图片地址规范化为不含签名参数的稳定对象地址，避免把临时凭证写入数据库。
    /// </summary>
    public string CanonicalizeMarkdownImageUrls(int clubId, string content) =>
        ReplaceMarkdownImageUrls(clubId, content, BuildUnsignedImageUrl);

    /// <summary>
    /// 为帖子正文中的论坛图片重新生成短期签名地址。每次读取帖子时调用，兼容旧的未签名地址。
    /// </summary>
    public string RefreshMarkdownImageUrls(int clubId, string content) =>
        ReplaceMarkdownImageUrls(clubId, content, BuildSignedImageUrl);

    /// <summary>
    /// 判断对象键是否严格位于指定社团的论坛图片目录下。
    /// </summary>
    internal static bool IsForumStorageKey(int clubId, string? storageKey)
    {
        if (clubId <= 0 || string.IsNullOrWhiteSpace(storageKey)) return false;

        var normalized = storageKey.Trim();
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(normalized);
        }
        catch (UriFormatException)
        {
            return false;
        }
        var prefix = $"clubs/{clubId}/forum/";
        if (!decoded.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (decoded.Length == prefix.Length || decoded.Contains('\\') || decoded.Contains('?') || decoded.Contains('#')) return false;

        var segments = decoded.Split('/');
        return segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    public void Dispose()
    {
        _publicUrlClient?.Dispose();
        _client?.Dispose();
    }

    private static OSS.Client CreateClient(string region, string endpoint, OSS.Credentials.ICredentialsProvider credentialsProvider)
    {
        var configuration = OSS.Configuration.LoadDefault();
        configuration.Region = region.Trim();
        configuration.Endpoint = NormalizeEndpoint(endpoint);
        configuration.CredentialsProvider = credentialsProvider;
        configuration.ConnectTimeout = TimeSpan.FromSeconds(10);
        configuration.ReadWriteTimeout = TimeSpan.FromMinutes(5);
        return new OSS.Client(configuration);
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_options.Region) &&
        !string.IsNullOrWhiteSpace(_options.Endpoint) &&
        !string.IsNullOrWhiteSpace(_options.Bucket) &&
        !string.IsNullOrWhiteSpace(_options.RoleName);

    private string GetPublicEndpoint()
    {
        var endpoint = string.IsNullOrWhiteSpace(_options.PublicEndpoint)
            ? _options.Endpoint.Trim()
            : _options.PublicEndpoint.Trim();
        return endpoint.Replace("-internal.", ".", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildSignedImageUrl(string objectName)
    {
        var client = _publicUrlClient ?? throw new InvalidOperationException("Forum image OSS signing client is not configured.");
        var result = client.Presign(
            new OSS.Models.GetObjectRequest
            {
                Bucket = _options.Bucket,
                Key = objectName
            },
            DateTime.UtcNow.Add(SignedUrlLifetime));
        return result.Url ?? throw new InvalidOperationException("Forum image OSS signing returned an empty URL.");
    }

    private string BuildUnsignedImageUrl(string objectName)
    {
        var endpointHost = new Uri(NormalizeEndpoint(GetPublicEndpoint())).Host;
        return $"https://{_options.Bucket.Trim()}.{endpointHost}/{objectName}";
    }

    private string ReplaceMarkdownImageUrls(int clubId, string content, Func<string, string> transform)
    {
        if (string.IsNullOrEmpty(content)) return content;

        return MarkdownImagePattern.Replace(content, match =>
        {
            var urlToken = match.Groups["url"].Value;
            var imageUrl = urlToken.Trim('<', '>');
            if (!TryGetForumStorageKey(clubId, imageUrl, out var storageKey)) return match.Value;

            try
            {
                var transformedUrl = transform(storageKey);
                var replacement = urlToken.StartsWith('<') && urlToken.EndsWith('>')
                    ? $"<{transformedUrl}>"
                    : transformedUrl;
                var prefixLength = match.Groups["url"].Index - match.Index;
                return match.Value[..prefixLength] + replacement;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to refresh forum image URL for key {StorageKey}", storageKey);
                return match.Value;
            }
        });
    }

    private bool TryGetForumStorageKey(int clubId, string imageUrl, out string storageKey)
    {
        storageKey = string.Empty;
        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.Bucket))
            return false;
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443)
            return false;

        var bucket = _options.Bucket.Trim();
        if (string.IsNullOrWhiteSpace(bucket)) return false;

        var configuredHost = new Uri(NormalizeEndpoint(_options.Endpoint)).Host;
        var publicHost = new Uri(NormalizeEndpoint(GetPublicEndpoint())).Host;
        var isOssHost = string.Equals(uri.Host, $"{bucket}.{configuredHost}", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(uri.Host, $"{bucket}.{publicHost}", StringComparison.OrdinalIgnoreCase);
        if (!isOssHost) return false;

        var key = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped).TrimStart('/');
        if (!IsForumStorageKey(clubId, key)) return false;

        storageKey = key;
        return true;
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Forum image OSS endpoint must use HTTPS or omit the scheme.");

        return normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"https://{normalized}";
    }
}
