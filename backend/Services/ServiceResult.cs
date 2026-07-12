namespace ClubHub.Api.Services;

public record ServiceResult<T>(
    int StatusCode,
    T? Value,
    string? ErrorMessage,
    string? ErrorCode = null
)
{
    public bool Succeeded => ErrorMessage is null;

    public static ServiceResult<T> Ok(T value) => new(200, value, null);
    public static ServiceResult<T> Fail(int statusCode, string message, string? errorCode = null) => new(statusCode, default, message, errorCode);
}
