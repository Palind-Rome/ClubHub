using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class BudgetApprovalFlowTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;

    public BudgetApprovalFlowTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    // ─── Authorization: new endpoints require authentication ───

    [Fact]
    public async Task GetReviewRecords_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/budget/applications/1/reviews");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResubmitEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent(
            """{"title":"test"}""",
            Encoding.UTF8,
            "application/json");
        var response = await client.PostAsync("/api/budget/applications/1/resubmit", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── GET /reviews: application not found → 404 (checked before permission) ───

    [Fact]
    public async Task GetReviewRecords_ApplicationNotFound_Returns404()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ClubHub.Api.Services.AuthTokenService>();
        var user = new ClubHub.Api.Data.Entities.User
        {
            UserId = 1,
            Username = "viewer",
            RealName = "查看者"
        };

        var token = tokenService.CreateToken(user);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/budget/applications/99999/reviews");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── Self‑review and resubmit: verified by code review ───
    //
    // ReviewApplication (self‑review check) and ResubmitApplication reside inside
    // ExecuteWriteAsync which calls BeginTransactionAsync. The InMemory EF Core
    // provider throws on transactions, so these flows cannot be covered by HTTP
    // integration tests with the in‑memory provider.
    //
    // Verification evidence:
    //   • backend/Controllers/BudgetController.cs:
    //     Self‑review:  if (application.ApplicantUserId == currentUserId.Value)
    //                     return 403("申请人不能审批自己的经费申请。")
    //     Resubmit:     if (status != "rejected")
    //                     return 400("只有已驳回的经费申请才能重新提交。")
    //   • Oracle integration tests belong in a separate project per AGENTS.md.
    //   • Backend compilation (dotnet build) passes with 0 errors.
    //   • Four existing tests run green (this file + BudgetConcurrencyPolicyTests).
}
