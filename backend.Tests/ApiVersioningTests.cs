namespace ClubHub.Api.Tests;

public sealed class ApiVersioningTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiVersioningTests(ClubHubWebApplicationFactory factory) =>
        _client = factory.CreateClient();

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
    [InlineData("/api/clubs/7/members/9/exit", "/api/v1/clubs/7/members/9")]
    [InlineData("/api/v1/learning/items/5/download", "/api/v1/learning/items/5/file?download=true")]
    public void DeprecatedRpcRoutePointsToCanonicalResource(string legacyPath, string canonicalPath)
    {
        Assert.True(ClubHub.Api.Infrastructure.Rest.ApiRouteDeprecation.TryGetSuccessor(legacyPath, out var successor));
        Assert.Equal(canonicalPath, successor);
    }
}
