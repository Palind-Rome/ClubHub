using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Org.OpenAPITools.Models;

namespace ClubHub.Api.Infrastructure.Rest;

public static class ApiErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string RateLimited = "RATE_LIMITED";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    public const string InternalError = "INTERNAL_ERROR";
    public const string RequestFailed = "REQUEST_FAILED";
}

public static class ApiErrorFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ApiError Create(int statusCode, object? payload = null)
    {
        var extracted = Extract(payload);
        var message = extracted.Message;
        var detail = extracted.Detail;

        if (string.IsNullOrWhiteSpace(message) || !ContainsChinese(message))
        {
            detail ??= string.IsNullOrWhiteSpace(message) ? null : message;
            message = DefaultMessage(statusCode);
        }

        return new ApiError
        {
            Code = DefaultCode(statusCode),
            Message = message,
            Detail = detail
        };
    }

    private static (string? Code, string? Message, string? Detail) Extract(object? payload)
    {
        if (payload is ApiError apiError)
        {
            return (apiError.Code, apiError.Message, apiError.Detail);
        }

        if (payload is ValidationProblemDetails validationProblem)
        {
            var detail = string.Join(
                " ",
                validationProblem.Errors
                    .OrderBy(error => error.Key, StringComparer.Ordinal)
                    .SelectMany(error => error.Value));
            return (ApiErrorCodes.ValidationError, validationProblem.Title, detail);
        }

        if (payload is ProblemDetails problem)
        {
            return (null, problem.Title, problem.Detail);
        }

        if (payload is string message)
        {
            return (null, message, null);
        }

        if (payload is null)
        {
            return (null, null, null);
        }

        try
        {
            var element = JsonSerializer.SerializeToElement(payload, JsonOptions);
            if (element.ValueKind != JsonValueKind.Object)
            {
                return (null, element.ToString(), null);
            }

            return (
                GetString(element, "code"),
                GetString(element, "message") ?? GetString(element, "title"),
                GetString(element, "detail"));
        }
        catch (NotSupportedException)
        {
            return (null, null, null);
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool ContainsChinese(string value) =>
        value.Any(character => character is >= '\u4e00' and <= '\u9fff');

    private static string DefaultCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ApiErrorCodes.ValidationError,
        StatusCodes.Status401Unauthorized => ApiErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ApiErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ApiErrorCodes.NotFound,
        StatusCodes.Status409Conflict => ApiErrorCodes.Conflict,
        StatusCodes.Status413PayloadTooLarge => ApiErrorCodes.PayloadTooLarge,
        StatusCodes.Status429TooManyRequests => ApiErrorCodes.RateLimited,
        StatusCodes.Status503ServiceUnavailable => ApiErrorCodes.ServiceUnavailable,
        >= StatusCodes.Status500InternalServerError => ApiErrorCodes.InternalError,
        _ => ApiErrorCodes.RequestFailed
    };

    private static string DefaultMessage(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "请求参数不合法。",
        StatusCodes.Status401Unauthorized => "登录状态已失效，请重新登录。",
        StatusCodes.Status403Forbidden => "当前用户没有执行此操作的权限。",
        StatusCodes.Status404NotFound => "请求的资源不存在。",
        StatusCodes.Status409Conflict => "请求与当前资源状态冲突。",
        StatusCodes.Status413PayloadTooLarge => "请求内容过大。",
        StatusCodes.Status429TooManyRequests => "请求过于频繁，请稍后重试。",
        StatusCodes.Status503ServiceUnavailable => "服务暂时不可用，请稍后重试。",
        >= StatusCodes.Status500InternalServerError => "服务器处理请求时发生错误。",
        _ => "请求处理失败。"
    };
}
