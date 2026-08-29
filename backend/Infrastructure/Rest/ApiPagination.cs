namespace ClubHub.Api.Infrastructure.Rest;

public static class ApiPagination
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;

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
