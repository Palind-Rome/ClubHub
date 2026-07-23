using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public class GeneratedJsonRequiredMembersTests
{
    [Fact]
    public async Task ReviewDeliverableWithoutApprovedReturnsBadRequest()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);

        using var content = new StringContent(
            """
            {
              "reviewComment": "missing approval result"
            }
            """,
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/projects/1/tasks/1/deliverable/review", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReviewDeliverableRejectedWithoutCommentReturnsBadRequest()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        using var client = CreateAuthenticatedClient(factory);

        using var content = new StringContent(
            """
            {
              "approved": false,
              "reviewComment": "   "
            }
            """,
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/projects/1/tasks/1/deliverable/review", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient CreateAuthenticatedClient(ClubHubWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateToken(new User
        {
            UserId = 1,
            Username = "reviewer"
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
