namespace ClubHub.Api.Services;

/// <summary>
/// Storage abstraction for award workflow files such as rules and application materials.
/// </summary>
public interface IAwardObjectStorage
{
    bool IsStorageReference(string? value);

    Task<string> UploadAsync(
        int clubId,
        int awardFileOwnerId,
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

public sealed class AwardObjectStorageException : Exception
{
    public AwardObjectStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
