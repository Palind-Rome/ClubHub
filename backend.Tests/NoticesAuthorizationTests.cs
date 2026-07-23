using System.Net;
using System.Text;

namespace ClubHub.Api.Tests;

public sealed class NoticesAuthorizationTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;

    public NoticesAuthorizationTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("GET", "/api/notices", false)]
    [InlineData("POST", "/api/notices", true)]
    [InlineData("PATCH", "/api/notices/1", true)]
    [InlineData("DELETE", "/api/notices/1", false)]
    [InlineData("POST", "/api/notices/1/reads", false)]
    public async Task NoticeEndpoints_WithoutBearerToken_ReturnUnauthorized(
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
