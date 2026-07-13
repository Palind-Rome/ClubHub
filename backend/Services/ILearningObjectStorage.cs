namespace ClubHub.Api.Services;

/// <summary>
/// 学习资源使用的厂商无关对象存储契约。
/// </summary>
public interface ILearningObjectStorage
{
    bool IsStorageReference(string? value);

    Task<string> UploadAsync(
        int clubId,
        int itemId,
        string extension,
        Stream content,
        long contentLength,
        string? contentType,
        string originalFileName,
        CancellationToken cancellationToken);

    Task<StoredObjectMetadata> GetMetadataAsync(
        string storageReference,
        CancellationToken cancellationToken);

    Task<StoredObjectDownload> OpenReadAsync(
        string storageReference,
        CancellationToken cancellationToken);

    Task RemoveAsync(string storageReference, CancellationToken cancellationToken);
}

public sealed record StoredObjectMetadata(
    long? ContentLength,
    string? ContentType,
    string? ContentDisposition,
    string? ETag,
    DateTimeOffset? LastModified);

public sealed record StoredObjectDownload(Stream Content);

public sealed class LearningObjectStorageException : Exception
{
    public LearningObjectStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
