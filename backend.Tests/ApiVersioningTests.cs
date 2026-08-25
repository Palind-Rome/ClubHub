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
}
