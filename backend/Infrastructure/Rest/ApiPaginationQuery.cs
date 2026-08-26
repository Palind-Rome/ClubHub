using Microsoft.EntityFrameworkCore;
using Org.OpenAPITools.Models;

namespace ClubHub.Api.Infrastructure.Rest;

public static class ApiPaginationQuery
{
    public static async Task<ApiPaginationQueryResult<T>> MaterializeAsync<T>(
        IQueryable<T> query,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var request = httpContext.Request;
        if (!request.Query.ContainsKey("page") && !request.Query.ContainsKey("pageSize"))
        {
            return new(await query.ToListAsync(cancellationToken), null);
        }

        if (!TryReadPositiveInt(
                request,
                "page",
                ApiPagination.DefaultPage,
                int.MaxValue,
                out var page) ||
            !TryReadPositiveInt(
                request,
                "pageSize",
                ApiPagination.DefaultPageSize,
                ApiPagination.MaximumPageSize,
                out var pageSize))
        {
            return new([], ApiErrorFactory.Create(
                StatusCodes.Status400BadRequest,
                $"page 必须大于 0，pageSize 必须在 1 到 {ApiPagination.MaximumPageSize} 之间。"));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var skip = ((long)page - 1) * pageSize;
        var items = skip > int.MaxValue
            ? []
            : await query.Skip((int)skip).Take(pageSize).ToListAsync(cancellationToken);

        ApiPagination.ApplyResponseHeaders(
            request,
            httpContext.Response,
            page,
            pageSize,
            totalCount);
        return new(items, null);
    }

    private static bool TryReadPositiveInt(
        HttpRequest request,
        string name,
        int defaultValue,
        int maximum,
        out int value)
    {
        var rawValue = request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            value = defaultValue;
            return true;
        }

        return int.TryParse(rawValue, out value) && value > 0 && value <= maximum;
    }
}

public sealed record ApiPaginationQueryResult<T>(IReadOnlyList<T> Items, ApiError? Error);
