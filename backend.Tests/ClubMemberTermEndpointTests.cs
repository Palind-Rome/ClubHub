using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class ClubMemberTermEndpointTests
{
    [Fact]
    public async Task GetMembers_DefaultQueryIncludesFutureActiveTerm()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        var dates = FutureTermDates(2);
        using var client = await SeedClubAsync(factory, dates.Start, dates.End, "未来任期");

        using var response = await client.GetAsync("/api/v1/clubs/830002/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var member = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("active", member.GetProperty("memberStatus").GetString());
        Assert.Equal("未来任期", member.GetProperty("termName").GetString());
        Assert.False(member.GetProperty("isCurrent").GetBoolean());
    }

    [Fact]
    public async Task CreateMemberTerm_DuplicateTermReturnsConflictWithoutEndingExistingTerm()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        var dates = FutureTermDates(2);
        using var client = await SeedClubAsync(factory, dates.Start, dates.End, "2030-2031学年");

        using var response = await CreateTermAsync(
            client,
            "2030-2031学年",
            dates.Start,
            dates.End);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("CONFLICT", body.RootElement.GetProperty("code").GetString());
        Assert.Contains("同名任期", body.RootElement.GetProperty("message").GetString());
        await AssertExistingTermUnchangedAsync(factory, dates.Start, dates.End);
    }

    [Fact]
    public async Task CreateMemberTerm_EarlierThanExistingActiveTermReturnsConflict()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        var existingDates = FutureTermDates(3);
        var requestedDates = FutureTermDates(2);
        using var client = await SeedClubAsync(
            factory,
            existingDates.Start,
            existingDates.End,
            "较晚任期");

        using var response = await CreateTermAsync(
            client,
            "较早任期",
            requestedDates.Start,
            requestedDates.End);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertExistingTermUnchangedAsync(factory, existingDates.Start, existingDates.End);
    }

    [Fact]
    public async Task CreateMemberTerm_SameStartWithDifferentNameReturnsConflict()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        var dates = FutureTermDates(2);
        using var client = await SeedClubAsync(factory, dates.Start, dates.End, "原任期");

        using var response = await CreateTermAsync(
            client,
            "同日起始的新任期",
            dates.Start,
            dates.End);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertExistingTermUnchangedAsync(factory, dates.Start, dates.End);
    }

    [Fact]
    public async Task CreateMemberTerm_LaterTermClosesEarlierOverlappingTerm()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        var existingStart = new DateTime(DateTime.UtcNow.Year, 7, 1);
        var requestedDates = FutureTermDates(2);
        var existingEnd = requestedDates.End;
        using var client = await SeedClubAsync(
            factory,
            existingStart,
            existingEnd,
            "原任期");

        using var response = await CreateTermAsync(
            client,
            "新任期",
            requestedDates.Start,
            requestedDates.End);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var terms = await db.ClubMembers
            .Where(member => member.ClubId == 830002 && member.UserId == 830003)
            .OrderBy(member => member.TermStart)
            .ToListAsync();
        Assert.Equal(2, terms.Count);
        Assert.Equal("ended", terms[0].MemberStatus);
        Assert.Equal(requestedDates.Start.AddDays(-1), terms[0].TermEnd);
        Assert.Equal("active", terms[1].MemberStatus);
        Assert.Equal(requestedDates.Start, terms[1].TermStart);
        Assert.Equal(requestedDates.End, terms[1].TermEnd);
    }

    private static async Task<HttpClient> SeedClubAsync(
        ClubHubWebApplicationFactory factory,
        DateTime existingStart,
        DateTime existingEnd,
        string existingTermName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var now = DateTime.UtcNow;
        var principal = new User
        {
            UserId = 830001,
            Username = "member-term-principal",
            PasswordHash = "unused",
            RealName = "任期测试负责人",
            AccountStatus = "normal",
            CreatedAt = now
        };
        db.Users.AddRange(
            principal,
            new User
            {
                UserId = 830003,
                Username = "member-term-target",
                PasswordHash = "unused",
                RealName = "任期测试成员",
                AccountStatus = "normal",
                CreatedAt = now
            });
        db.Clubs.Add(new Club
        {
            ClubId = 830002,
            ClubName = "任期回归测试社团",
            PresidentUserId = principal.UserId,
            AuditStatus = "approved",
            ClubStatus = "active",
            CreatedAt = now
        });
        db.ClubMembers.Add(new ClubMember
        {
            MemberId = 830004,
            ClubId = 830002,
            UserId = 830003,
            PositionName = "成员",
            TermName = existingTermName,
            TermStart = existingStart,
            TermEnd = existingEnd,
            MemberStatus = "active",
            JoinAt = now,
            ContributionScore = 0
        });
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider
            .GetRequiredService<AuthTokenService>()
            .CreateToken(principal);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> CreateTermAsync(
        HttpClient client,
        string termName,
        DateTime termStart,
        DateTime termEnd)
    {
        return await client.PostAsJsonAsync(
            "/api/v1/clubs/830002/members/terms",
            new
            {
                userId = 830003,
                positionName = "成员",
                termName,
                termStart,
                termEnd,
                memberStatus = "active",
                contributionScore = 0,
                closeCurrentTerm = true
            });
    }

    private static async Task AssertExistingTermUnchangedAsync(
        ClubHubWebApplicationFactory factory,
        DateTime expectedStart,
        DateTime expectedEnd)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var term = Assert.Single(await db.ClubMembers
            .Where(member => member.ClubId == 830002 && member.UserId == 830003)
            .ToListAsync());
        Assert.Equal("active", term.MemberStatus);
        Assert.Equal(expectedStart, term.TermStart);
        Assert.Equal(expectedEnd, term.TermEnd);
    }

    private static (DateTime Start, DateTime End) FutureTermDates(int yearsAhead)
    {
        var startYear = DateTime.UtcNow.Year + yearsAhead;
        return (new DateTime(startYear, 7, 1), new DateTime(startYear + 1, 6, 30));
    }
}
