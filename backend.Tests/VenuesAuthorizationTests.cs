using System.Net;
using System.Text.Json;
using ClubHub.Api.Infrastructure.Rest;

namespace ClubHub.Api.Tests;

public sealed class VenuesAuthorizationTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;

    public VenuesAuthorizationTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task VersionedDeleteWithoutBodyRequiresBearerIdentity()
    {
        using var client = _factory.CreateClient();

        using var response = await client.DeleteAsync("/api/v1/venues/1");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, body.RootElement.GetProperty("code").GetString());
    }
}
