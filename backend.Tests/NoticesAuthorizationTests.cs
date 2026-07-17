using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ClubHub.Api.Tests;

public sealed class NoticesAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NoticesAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:TokenSigningKey"] = "ClubHub.NoticesAuthorizationTests.SigningKey"
                }));
        });
    }

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
