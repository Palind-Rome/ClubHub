using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class ApiVersioningTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private static readonly string[] CanonicalResourceRoutes =
    [
        "/api/v1/auth/permissions",
        "/api/v1/users/me/permissions",
        "/api/v1/users/{userId}/roles",
        "/api/v1/clubs/{clubId}",
        "/api/v1/clubs/applications/{clubId}/reviews",
        "/api/v1/clubs/{clubId}/members/self",
        "/api/v1/clubs/{clubId}/members/{memberId}",
        "/api/v1/activities/{activityId}/reviews",
        "/api/v1/activities/{activityId}/budget-reviews",
        "/api/v1/activities/{activityId}/checkins",
        "/api/v1/activities/{activityId}/checkouts",
        "/api/v1/recruitments/{recruitId}/reviews",
        "/api/v1/applications/{applicationId}/reviews",
        "/api/v1/projects/{projectId}/reviews",
        "/api/v1/projects/{projectId}",
        "/api/v1/venue-reservations/{reservationId}/reviews",
        "/api/v1/learning/instructors",
        "/api/v1/learning/items/{itemId}/reviews",
        "/api/v1/learning/items/{itemId}/learning-records",
        "/api/v1/learning/items/{itemId}/file",
        "/api/v1/learning/items/{itemId}/downloads",
        "/api/v1/learning/resources"
    ];

    private readonly ClubHubWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ApiVersioningTests(ClubHubWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task VersionedControllerRouteIsAvailable()
    {
        using var response = await _client.GetAsync("/api/v1/health");

        response.EnsureSuccessStatusCode();
        Assert.False(response.Headers.Contains("Deprecation"));
    }

    [Fact]
    public async Task LegacyControllerRouteAdvertisesSuccessorAndSunset()
    {
        using var response = await _client.GetAsync("/api/health?probe=legacy");

        response.EnsureSuccessStatusCode();
        Assert.Equal("true", response.Headers.GetValues("Deprecation").Single());
        Assert.True(response.Headers.Contains("Sunset"));
        Assert.Equal(
            "</api/v1/health?probe=legacy>; rel=\"successor-version\"",
            response.Headers.GetValues("Link").Single());
    }

    [Theory]
    [InlineData("/api/v1/activities/12/review", "/api/v1/activities/12/reviews")]
    [InlineData("/api/auth/permissions/check", "/api/v1/users/me/permissions")]
    [InlineData("/api/clubs/7/members/9/exit", "/api/v1/clubs/7/members/9")]
    [InlineData("/api/v1/learning/items/5/download", "/api/v1/learning/items/5/downloads")]
    public void DeprecatedRpcRoutePointsToCanonicalResource(string legacyPath, string canonicalPath)
    {
        Assert.True(ClubHub.Api.Infrastructure.Rest.ApiRouteDeprecation.TryGetSuccessor(legacyPath, out var successor));
        Assert.Equal(canonicalPath, successor);
        Assert.DoesNotContain('{', successor);
        Assert.DoesNotContain('}', successor);
    }

    [Fact]
    public async Task LegacyRoleAssignmentDoesNotAdvertiseUnexpandedSuccessor()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/auth/roles/assign",
            new { targetUserId = 7, roleCode = "STUDENT" });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("true", response.Headers.GetValues("Deprecation").Single());
        Assert.False(response.Headers.Contains("Link"));
    }

    [Fact]
    public void CanonicalResourceRoutesAreRegisteredAtRuntime()
    {
        var routes = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => NormalizeRoute(endpoint.RoutePattern.RawText))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(CanonicalResourceRoutes, route => Assert.Contains(route, routes));
    }

    private static string NormalizeRoute(string? route)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            route ?? string.Empty,
            @":[^}]+",
            string.Empty);
        return $"/{normalized.TrimStart('/')}";
    }
}
