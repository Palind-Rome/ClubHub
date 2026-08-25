using System.Net;
using System.Text;

namespace ClubHub.Api.Tests;

public sealed class RecruitmentsAuthorizationTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;

    public RecruitmentsAuthorizationTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("GET", "/api/v1/recruitments", false)]
    [InlineData("POST", "/api/v1/recruitments", true)]
    [InlineData("PATCH", "/api/v1/recruitments/1", true)]
    [InlineData("DELETE", "/api/v1/recruitments/1", false)]
    [InlineData("PATCH", "/api/v1/recruitments/1/review", true)]
    [InlineData("GET", "/api/v1/recruitments/1/applications", false)]
    [InlineData("POST", "/api/v1/recruitments/1/applications", true)]
    [InlineData("PATCH", "/api/v1/recruitments/applications/1/review", true)]
    public async Task RecruitmentEndpointsWithoutBearerTokenReturnUnauthorized(
        string method,
        string path,
        bool hasJsonBody)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (hasJsonBody)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
