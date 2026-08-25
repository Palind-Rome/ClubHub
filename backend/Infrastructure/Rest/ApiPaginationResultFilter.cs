using System.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClubHub.Api.Infrastructure.Rest;

public sealed class ApiPaginationResultFilter : IAsyncResultFilter
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) ||
            context.Result is not ObjectResult { StatusCode: null or >= 200 and < 300 } objectResult ||
            objectResult.Value is string or IDictionary ||
            objectResult.Value is not IEnumerable values)
        {
            await next();
            return;
        }

        if (ActionHandlesPagination(context))
        {
            await next();
            return;
        }

        if (!context.HttpContext.Request.Query.ContainsKey("page") &&
            !context.HttpContext.Request.Query.ContainsKey("pageSize"))
        {
            await next();
            return;
        }

        if (!TryReadPositiveInt(context, "page", DefaultPage, int.MaxValue, out var page) ||
            !TryReadPositiveInt(context, "pageSize", DefaultPageSize, MaximumPageSize, out var pageSize))
        {
            context.Result = new ObjectResult(ApiErrorFactory.Create(
                StatusCodes.Status400BadRequest,
                $"page 必须大于 0，pageSize 必须在 1 到 {MaximumPageSize} 之间。"))
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
            await next();
            return;
        }

        var allItems = values.Cast<object?>().ToList();
        var skip = ((long)page - 1) * pageSize;
        objectResult.Value = skip > int.MaxValue
            ? Array.Empty<object?>()
            : allItems.Skip((int)skip).Take(pageSize).ToArray();

        ApplyResponseHeaders(
            context.HttpContext.Request,
            context.HttpContext.Response,
            page,
            pageSize,
            allItems.Count);

        await next();
    }

    public static void ApplyResponseHeaders(
        HttpRequest request,
        HttpResponse response,
        int page,
        int pageSize,
        int totalCount)
    {
        response.Headers["X-Page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
        response.Headers["X-Page-Size"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        response.Headers["X-Total-Count"] = totalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AddNavigationLinks(request, response, page, pageSize, totalCount);
    }

    private static bool ActionHandlesPagination(ResultExecutingContext context)
    {
        var parameterNames = context.ActionDescriptor.Parameters
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return parameterNames.Contains("page") || parameterNames.Contains("pageSize");
    }

    private static bool TryReadPositiveInt(
        ResultExecutingContext context,
        string name,
        int defaultValue,
        int maximum,
        out int value)
    {
        var rawValue = context.HttpContext.Request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            value = defaultValue;
            return true;
        }

        return int.TryParse(rawValue, out value) && value > 0 && value <= maximum;
    }

    private static void AddNavigationLinks(
        HttpRequest request,
        HttpResponse response,
        int page,
        int pageSize,
        int totalCount)
    {
        var links = new List<string>();
        if (page > 1)
        {
            links.Add($"<{BuildPageUrl(request, page - 1, pageSize)}>; rel=\"prev\"");
        }

        if ((long)page * pageSize < totalCount)
        {
            links.Add($"<{BuildPageUrl(request, page + 1, pageSize)}>; rel=\"next\"");
        }

        if (links.Count > 0)
        {
            response.Headers.Append("Link", string.Join(", ", links));
        }
    }

    private static string BuildPageUrl(HttpRequest request, int page, int pageSize)
    {
        var query = QueryString.Empty;
        foreach (var pair in request.Query.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (pair.Key.Equals("page", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Equals("pageSize", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in pair.Value)
            {
                query = query.Add(pair.Key, value ?? string.Empty);
            }
        }

        query = query
            .Add("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Add("pageSize", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return $"{request.PathBase}{request.Path}{query}";
    }
}
