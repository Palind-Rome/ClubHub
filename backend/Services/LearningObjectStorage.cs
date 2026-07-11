using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace ClubHub.Api.Services;

public sealed class LearningObjectStorage
{
    private const string ReferenceScheme = "minio";
    private readonly MinioStorageOptions _options;
    private readonly IMinioClient? _storageClient;
    private readonly IMinioClient? _downloadClient;
    private readonly Exception? _configurationError;
    private readonly SemaphoreSlim _bucketInitializationLock = new(1, 1);
    private bool _bucketInitialized;

    public LearningObjectStorage(IOptions<MinioStorageOptions> options)
    {
        _options = options.Value;
        if (!IsConfigured()) return;

        try
        {
            _storageClient = CreateClient(_options.Endpoint, _options.UseSsl);
            _downloadClient = string.IsNullOrWhiteSpace(_options.PublicEndpoint)
                ? _storageClient
                : CreateClient(
                    _options.PublicEndpoint,
                    _options.PublicUseSsl ?? _options.UseSsl);
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
        var client = GetStorageClient();
        await EnsureBucketAsync(client, cancellationToken);

        var objectName = $"clubs/{clubId}/learning/{itemId}/{Guid.NewGuid():N}{extension}";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Disposition"] =
                $"attachment; filename*=UTF-8''{Uri.EscapeDataString(originalFileName)}"
        };
        var args = new PutObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectName)
            .WithStreamData(content)
            .WithObjectSize(contentLength)
            .WithContentType(string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType)
            .WithHeaders(headers);

        await client.PutObjectAsync(args, cancellationToken);
        return BuildReference(_options.Bucket, objectName);
    }

    public Task<string> CreateDownloadUrlAsync(
        string storageReference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = GetDownloadClient();
        var (bucket, objectName) = ParseOwnedReference(storageReference);
        var expiry = Math.Clamp(_options.DownloadUrlExpirySeconds, 60, 604800);
        var args = new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithExpiry(expiry);
        return client.PresignedGetObjectAsync(args);
    }

    public async Task RemoveAsync(string storageReference, CancellationToken cancellationToken)
    {
        var client = GetStorageClient();
        var (bucket, objectName) = ParseOwnedReference(storageReference);
        var args = new RemoveObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName);
        await client.RemoveObjectAsync(args, cancellationToken);
    }

    private async Task EnsureBucketAsync(
        IMinioClient client,
        CancellationToken cancellationToken)
    {
        if (_bucketInitialized) return;
        await _bucketInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_bucketInitialized) return;
            var exists = await client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(_options.Bucket),
                cancellationToken);
            if (!exists)
            {
                if (!_options.AutoCreateBucket)
                {
                    throw new InvalidOperationException(
                        $"MinIO bucket '{_options.Bucket}' does not exist and automatic creation is disabled.");
                }

                await client.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(_options.Bucket),
                    cancellationToken);
            }

            _bucketInitialized = true;
        }
        finally
        {
            _bucketInitializationLock.Release();
        }
    }

    private IMinioClient GetStorageClient() => _storageClient ?? throw NotConfigured();

    private IMinioClient GetDownloadClient() => _downloadClient ?? throw NotConfigured();

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_options.Endpoint) &&
        !string.IsNullOrWhiteSpace(_options.AccessKey) &&
        !string.IsNullOrWhiteSpace(_options.SecretKey) &&
        !string.IsNullOrWhiteSpace(_options.Bucket);

    private IMinioClient CreateClient(string endpoint, bool useSsl)
    {
        var normalizedEndpoint = endpoint.Trim().TrimEnd('/');
        if (Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out var uri))
        {
            if (uri.AbsolutePath != "/")
            {
                throw new InvalidOperationException("MinIO endpoint cannot contain a path.");
            }

            normalizedEndpoint = uri.Authority;
            useSsl = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        return new MinioClient()
            .WithEndpoint(normalizedEndpoint)
            .WithCredentials(_options.AccessKey.Trim(), _options.SecretKey)
            .WithSSL(useSsl)
            .Build();
    }

    private (string Bucket, string ObjectName) ParseOwnedReference(string storageReference)
    {
        if (!TryParseReference(storageReference, out var bucket, out var objectName) ||
            !string.Equals(bucket, _options.Bucket, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The learning resource object reference is invalid.");
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

    private static string BuildReference(string bucket, string objectName) =>
        $"{ReferenceScheme}://{bucket}/{objectName}";

    private InvalidOperationException NotConfigured() => new(
        "MinIO is not configured correctly. Set Minio__Endpoint, Minio__AccessKey and Minio__SecretKey.",
        _configurationError);
}
