using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class RecruitmentsAuthorizationTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;
    private static int _sequence;

    public RecruitmentsAuthorizationTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PendingRecruitmentIsHiddenFromOrdinaryUsersButVisibleToAuthorizedUsers()
    {
        var seeded = await SeedPendingRecruitmentAsync();

        using var ordinaryClient = CreateAuthenticatedClient(seeded.OrdinaryUserId);
        using var managerClient = CreateAuthenticatedClient(seeded.ManagerUserId);
        using var reviewerClient = CreateAuthenticatedClient(seeded.ReviewerUserId);

        Assert.DoesNotContain(seeded.RecruitmentId, await GetRecruitmentIdsAsync(ordinaryClient));
        Assert.Contains(seeded.RecruitmentId, await GetRecruitmentIdsAsync(managerClient));
        Assert.Contains(seeded.RecruitmentId, await GetRecruitmentIdsAsync(reviewerClient));
    }

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

    private async Task<SeededRecruitment> SeedPendingRecruitmentAsync()
    {
        var suffix = Interlocked.Increment(ref _sequence);
        var baseId = 7_300_000 + suffix * 20;
        var now = DateTime.UtcNow;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();

        db.AddRange(
            new User
            {
                UserId = baseId,
                Username = $"recruitment-ordinary-{suffix}",
                PasswordHash = "unused",
                AccountStatus = "normal",
                CreatedAt = now
            },
            new User
            {
                UserId = baseId + 1,
                Username = $"recruitment-manager-{suffix}",
                PasswordHash = "unused",
                AccountStatus = "normal",
                CreatedAt = now
            },
            new User
            {
                UserId = baseId + 2,
                Username = $"recruitment-reviewer-{suffix}",
                PasswordHash = "unused",
                AccountStatus = "normal",
                CreatedAt = now
            },
            new Club
            {
                ClubId = baseId + 3,
                ClubName = $"招新权限测试社团 {suffix}",
                AuditStatus = RecruitmentWorkflow.ClubApproved,
                ClubStatus = RecruitmentWorkflow.ClubActive,
                CreatedAt = now
            },
            new Role
            {
                RoleId = baseId + 4,
                RoleCode = "CLUB_OFFICER",
                RoleName = "社团干部",
                RoleScope = "club",
                CreatedAt = now
            },
            new Role
            {
                RoleId = baseId + 5,
                RoleCode = "CLUB_ADMIN",
                RoleName = "社团管理员",
                RoleScope = "platform",
                CreatedAt = now
            },
            new UserRole
            {
                UserRoleId = baseId + 6,
                UserId = baseId + 1,
                RoleId = baseId + 4,
                ClubId = baseId + 3,
                AssignedAt = now
            },
            new UserRole
            {
                UserRoleId = baseId + 7,
                UserId = baseId + 2,
                RoleId = baseId + 5,
                AssignedAt = now
            },
            new Recruitment
            {
                RecruitId = baseId + 8,
                ClubId = baseId + 3,
                Title = $"待审核招募 {suffix}",
                StartAt = now.AddDays(1),
                EndAt = now.AddDays(7),
                Quota = 10,
                Requirements = "测试要求",
                RecruitStatus = RecruitmentStatuses.PendingReview,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        return new(baseId, baseId + 1, baseId + 2, baseId + 8);
    }

    private HttpClient CreateAuthenticatedClient(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateToken(new User
        {
            UserId = userId,
            Username = $"recruitment-user-{userId}"
        });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int[]> GetRecruitmentIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/recruitments");
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.EnumerateArray()
            .Select(recruitment => recruitment.GetProperty("id").GetInt32())
            .ToArray();
    }

    private sealed record SeededRecruitment(
        int OrdinaryUserId,
        int ManagerUserId,
        int ReviewerUserId,
        int RecruitmentId);
}
