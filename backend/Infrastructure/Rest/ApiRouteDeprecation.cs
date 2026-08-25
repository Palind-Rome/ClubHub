using System.Text.RegularExpressions;

namespace ClubHub.Api.Infrastructure.Rest;

public static partial class ApiRouteDeprecation
{
    private static readonly (Regex Pattern, string Replacement)[] RpcRouteReplacements =
    [
        (Route(@"/auth/permissions/check$"), "/api/v1/auth/permissions"),
        (Route(@"/auth/roles/assign$"), "/api/v1/users/{userId}/roles"),
        (Route(@"/clubs/applications/(?<id>\d+)/review$"), "/api/v1/clubs/applications/${id}/reviews"),
        (Route(@"/clubs/(?<id>\d+)/dissolve$"), "/api/v1/clubs/${id}"),
        (Route(@"/clubs/(?<clubId>\d+)/members/self/exit$"), "/api/v1/clubs/${clubId}/members/self"),
        (Route(@"/clubs/(?<clubId>\d+)/members/(?<memberId>\d+)/exit$"), "/api/v1/clubs/${clubId}/members/${memberId}"),
        (Route(@"/activities/(?<id>\d+)/review$"), "/api/v1/activities/${id}/reviews"),
        (Route(@"/activities/(?<id>\d+)/budget/review$"), "/api/v1/activities/${id}/budget-reviews"),
        (Route(@"/activities/(?<id>\d+)/checkin$"), "/api/v1/activities/${id}/checkins"),
        (Route(@"/activities/(?<id>\d+)/checkout$"), "/api/v1/activities/${id}/checkouts"),
        (Route(@"/recruitments/(?<id>\d+)/review$"), "/api/v1/recruitments/${id}/reviews"),
        (Route(@"/recruitments/applications/(?<id>\d+)/review$"), "/api/v1/applications/${id}/reviews"),
        (Route(@"/projects/(?<id>\d+)/review$"), "/api/v1/projects/${id}/reviews"),
        (Route(@"/projects/(?<id>\d+)/cancel$"), "/api/v1/projects/${id}"),
        (Route(@"/venue-reservations/(?<id>\d+)/review$"), "/api/v1/venue-reservations/${id}/reviews"),
        (Route(@"/learning/instructor-lookup$"), "/api/v1/learning/instructors"),
        (Route(@"/learning/resources/upload$"), "/api/v1/learning/resources"),
        (Route(@"/learning/items/(?<id>\d+)/review$"), "/api/v1/learning/items/${id}/reviews"),
        (Route(@"/learning/items/(?<id>\d+)/learning$"), "/api/v1/learning/items/${id}/learning-records"),
        (Route(@"/learning/items/(?<id>\d+)/download$"), "/api/v1/learning/items/${id}/file?download=true")
    ];

    public static bool TryGetSuccessor(PathString requestPath, out string successorPath)
    {
        var path = requestPath.Value ?? string.Empty;
        foreach (var (pattern, replacement) in RpcRouteReplacements)
        {
            if (!pattern.IsMatch(path))
            {
                continue;
            }

            successorPath = pattern.Replace(path, replacement);
            return true;
        }

        if (requestPath.StartsWithSegments("/api", out var remainingPath) &&
            !remainingPath.StartsWithSegments("/v1"))
        {
            successorPath = $"/api/v1{remainingPath}";
            return true;
        }

        successorPath = string.Empty;
        return false;
    }

    private static Regex Route(string suffixPattern) =>
        new($@"^/api(?:/v1)?{suffixPattern}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
