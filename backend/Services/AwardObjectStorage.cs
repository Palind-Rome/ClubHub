using System.Net.Http;
using Microsoft.Extensions.Options;
using OSS = AlibabaCloud.OSS.V2;

namespace ClubHub.Api.Services;

public sealed class OssAwardObjectStorage : IAwardObjectStorage, IDisposable
{
    private const string LegacyReferenceScheme = "oss";
    private readonly OssStorageOptions _options;
    private readonly OSS.Client? _client;
    private readonly Exception? _configurationError;

    public OssAwardObjectStorage(IOptions<OssStorageOptions> options)
    {
        _options = options.Value;
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

    public bool IsStorageReference(string? value) => TryParseReference(value, out _, out _);

    public async Task<string> UploadAsync(
        int clubId,
        int awardFileOwnerId,
        string extension,
        Stream content,
        long contentLength,
        string? contentType,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        _ = contentLength;
        var client = GetClient();
        var objectName = $"clubs/{clubId}/awards/{awardFileOwnerId}/{Guid.NewGuid():N}{extension}";
        try
        {
            await client.PutObjectAsync(
                new OSS.Models.PutObjectRequest
                {
                    Bucket = _options.Bucket,
                    Key = objectName,
                    Body = content,
                    ContentType = string.IsNullOrWhiteSpace(contentType)
                        ? "application/octet-stream"
                        : contentType,
                    ContentDisposition =
                        $"attachment; filename*=UTF-8''{Uri.EscapeDataString(originalFileName)}"
                },
                cancellationToken: cancellationToken);
            return objectName;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AwardObjectStorageException("Failed to upload the award file to OSS.", exception);
        }
    }

    public async Task<StoredObjectMetadata> GetMetadataAsync(
        string storageReference,
        CancellationToken cancellationToken)
    {
        var client = GetClient();
        var objectName = ParseOwnedReference(storageReference);
        try
        {
            var result = await client.HeadObjectAsync(
                new OSS.Models.HeadObjectRequest
                {
                    Bucket = _options.Bucket,
                    Key = objectName
                },
                cancellationToken: cancellationToken);
            return new StoredObjectMetadata(
                result.ContentLength,
                result.ContentType,
                result.ContentDisposition,
                result.ETag,
                DateTimeOffset.TryParse(result.LastModified, out var lastModified)
                    ? lastModified
                    : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AwardObjectStorageException(
                "Failed to read award file metadata from object storage.",
                exception);
        }
    }

    public async Task<StoredObjectDownload> OpenReadAsync(
        string storageReference,
        CancellationToken cancellationToken)
    {
        var client = GetClient();
        var objectName = ParseOwnedReference(storageReference);
        try
        {
            var result = await client.GetObjectAsync(
                new OSS.Models.GetObjectRequest
                {
                    Bucket = _options.Bucket,
                    Key = objectName
                },
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken: cancellationToken);
            if (result.Body is null)
            {
                throw new AwardObjectStorageException("OSS returned an empty response stream.");
            }

            return new StoredObjectDownload(result.Body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AwardObjectStorageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AwardObjectStorageException("Failed to read the award file from OSS.", exception);
        }
    }

    public async Task RemoveAsync(string storageReference, CancellationToken cancellationToken)
    {
        var client = GetClient();
        var objectName = ParseOwnedReference(storageReference);
        try
        {
            await client.DeleteObjectAsync(
                new OSS.Models.DeleteObjectRequest
                {
                    Bucket = _options.Bucket,
                    Key = objectName
                },
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AwardObjectStorageException("Failed to delete the award file from OSS.", exception);
        }
    }

    public void Dispose() => _client?.Dispose();

    private OSS.Client GetClient() => _client ?? throw new AwardObjectStorageException(
        "Award file OSS is not configured correctly. Check the Oss section and the ECS RAM role.",
        _configurationError);

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_options.Region) &&
        !string.IsNullOrWhiteSpace(_options.Endpoint) &&
        !string.IsNullOrWhiteSpace(_options.Bucket) &&
        !string.IsNullOrWhiteSpace(_options.RoleName);

    private string ParseOwnedReference(string storageReference)
    {
        if (!TryParseReference(storageReference, out var bucket, out var objectName) ||
            (bucket is not null && !string.Equals(bucket, _options.Bucket, StringComparison.Ordinal)))
        {
            throw new AwardObjectStorageException("The award file OSS reference is invalid.");
        }

        return objectName;
    }

    private static bool TryParseReference(
        string? value,
        out string? bucket,
        out string objectName)
    {
        bucket = null;
        objectName = string.Empty;
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Scheme, LegacyReferenceScheme, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                return false;
            }

            bucket = uri.Host;
            normalized = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        }

        if (!IsValidObjectKey(normalized)) return false;
        objectName = normalized;
        return true;
    }

    private static bool IsValidObjectKey(string value) =>
        value.Length <= 1024 &&
        value.StartsWith("clubs/", StringComparison.Ordinal) &&
        value.Contains("/awards/", StringComparison.Ordinal) &&
        !value.StartsWith("/", StringComparison.Ordinal) &&
        !value.Contains('\\') &&
        !value.Split('/').Any(segment => segment is "" or "." or "..");

    private static string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Award file OSS endpoint must use HTTPS or omit the scheme.");
        }

        return normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"https://{normalized}";
    }
}
