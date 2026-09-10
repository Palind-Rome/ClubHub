using System.Net.Http.Headers;
using System.Text.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class ClubVisibilityTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;
    private static int _sequence;

    public ClubVisibilityTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PublicClubList_ExcludesUnapprovedOrInactiveClubs()
    {
        var baseId = 9_300_000 + Interlocked.Increment(ref _sequence) * 10;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        db.Clubs.AddRange(
            new Club
            {
                ClubId = baseId,
                ClubName = "可见运营社团",
                AuditStatus = "approved",
                ClubStatus = "active",
                CreatedAt = DateTime.UtcNow
            },
            new Club
            {
                ClubId = baseId + 1,
                ClubName = "已解散社团",
                AuditStatus = "approved",
                ClubStatus = "inactive",
                CreatedAt = DateTime.UtcNow
            },
            new Club
            {
                ClubId = baseId + 2,
                ClubName = "待审核社团",
                AuditStatus = "pending",
                ClubStatus = "pending",
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/clubs");

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray()
            .Select(club => club.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("可见运营社团", names);
        Assert.DoesNotContain("已解散社团", names);
        Assert.DoesNotContain("待审核社团", names);
    }

    [Fact]
    public async Task MemberClubList_ExcludesDissolvedClubFromOperationalWorkspaces()
    {
        var baseId = 9_301_000 + Interlocked.Increment(ref _sequence) * 10;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var now = DateTime.UtcNow;
        db.Users.Add(new User
        {
            UserId = baseId,
            Username = $"club-visibility-user-{baseId}",
            PasswordHash = "unused",
            RealName = "社团可见性测试用户",
            AccountStatus = "normal",
            CreatedAt = now
        });
        db.Clubs.AddRange(
            new Club
            {
                ClubId = baseId + 1,
                ClubName = "成员可见运营社团",
                AuditStatus = "approved",
                ClubStatus = "active",
                CreatedAt = now
            },
            new Club
            {
                ClubId = baseId + 2,
                ClubName = "成员不可见解散社团",
                AuditStatus = "approved",
                ClubStatus = "inactive",
                CreatedAt = now
            });
        db.ClubMembers.AddRange(
            new ClubMember
            {
                MemberId = baseId + 3,
                ClubId = baseId + 1,
                UserId = baseId,
                MemberStatus = "active",
                TermStart = now.AddDays(-1),
                TermEnd = now.AddDays(30)
            },
            new ClubMember
            {
                MemberId = baseId + 4,
                ClubId = baseId + 2,
                UserId = baseId,
                MemberStatus = "active",
                TermStart = now.AddDays(-1),
                TermEnd = now.AddDays(30)
            });
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateToken(
            new User { UserId = baseId, Username = $"club-visibility-user-{baseId}" });
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/v1/clubs");

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray()
            .Select(club => club.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("成员可见运营社团", names);
        Assert.DoesNotContain("成员不可见解散社团", names);
    }
}
