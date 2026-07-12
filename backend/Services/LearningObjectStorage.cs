using System.Net.Http;
using Microsoft.Extensions.Options;
using OSS = AlibabaCloud.OSS.V2;

namespace ClubHub.Api.Services;

public sealed class LearningObjectStorage : IDisposable
{
    private const string ReferenceScheme = "oss";
    private readonly OssStorageOptions _options;
    private readonly OSS.Client? _client;
    private readonly Exception? _configurationError;

    public LearningObjectStorage(IOptions<OssStorageOptions> options)
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
        catch (Exception exception)
        {
            _configurationError = exception;
        }
    }

    public bool IsStorageReference(string? value) => TryParseReference(value, out _, out _);

    public async Task<string> UploadAsync(
        int clubId,
        int itemId,
        string extension,
        Stream content,
        long contentLength,
        string? contentType,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        _ = contentLength;
        var client = GetClient();
        var objectName = $"clubs/{clubId}/learning/{itemId}/{Guid.NewGuid():N}{extension}";
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
            return BuildReference(_options.Bucket, objectName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LearningObjectStorageException("Failed to upload the learning resource to OSS.", exception);
        }
    }

    public async Task<StoredObjectDownload> OpenReadAsync(
        string storageReference,
        CancellationToken cancellationToken)
    {
        var client = GetClient();
        var (bucket, objectName) = ParseOwnedReference(storageReference);
        try
        {
            var result = await client.GetObjectAsync(
                new OSS.Models.GetObjectRequest
                {
                    Bucket = bucket,
                    Key = objectName
                },
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken: cancellationToken);
            if (result.Body is null)
            {
                throw new LearningObjectStorageException("OSS returned an empty response stream.");
            }

            return new StoredObjectDownload(
                result.Body,
                result.ContentType,
                result.ContentDisposition,
                result.ContentLength);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LearningObjectStorageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LearningObjectStorageException("Failed to read the learning resource from OSS.", exception);
        }
    }

    public async Task RemoveAsync(string storageReference, CancellationToken cancellationToken)
    {
        var client = GetClient();
        var (bucket, objectName) = ParseOwnedReference(storageReference);
        try
        {
            await client.DeleteObjectAsync(
                new OSS.Models.DeleteObjectRequest
                {
                    Bucket = bucket,
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
            throw new LearningObjectStorageException("Failed to delete the learning resource from OSS.", exception);
        }
    }

    public void Dispose() => _client?.Dispose();

    private OSS.Client GetClient() => _client ?? throw new LearningObjectStorageException(
        "OSS is not configured correctly. Check the Oss section and the ECS RAM role.",
        _configurationError);

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_options.Region) &&
        !string.IsNullOrWhiteSpace(_options.Endpoint) &&
        !string.IsNullOrWhiteSpace(_options.Bucket) &&
        !string.IsNullOrWhiteSpace(_options.RoleName);

    private (string Bucket, string ObjectName) ParseOwnedReference(string storageReference)
    {
        if (!TryParseReference(storageReference, out var bucket, out var objectName) ||
            !string.Equals(bucket, _options.Bucket, StringComparison.Ordinal))
        {
            throw new LearningObjectStorageException("The learning resource OSS reference is invalid.");
        }

        return (bucket, objectName);
    }

    private static bool TryParseReference(
        string? value,
        out string bucket,
        out string objectName)
    {
        bucket = string.Empty;
        objectName = string.Empty;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, ReferenceScheme, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        bucket = uri.Host;
        objectName = path;
        return true;
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        return normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"https://{normalized}";
    }

    private static string BuildReference(string bucket, string objectName) =>
        $"{ReferenceScheme}://{bucket}/{objectName}";
}

public sealed record StoredObjectDownload(
    Stream Content,
    string? ContentType,
    string? ContentDisposition,
    long? ContentLength);

public sealed class LearningObjectStorageException : Exception
{
    public LearningObjectStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
