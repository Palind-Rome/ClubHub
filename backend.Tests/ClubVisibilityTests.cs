using System.Net.Http.Headers;
using System.Text.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task SessionRoles_ExcludeRolesScopedToInactiveClubs()
    {
        var baseId = 9_302_000 + Interlocked.Increment(ref _sequence) * 10;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        await authService.InitializeBaseRolesAsync();

        var now = DateTime.UtcNow;
        var activeClubId = baseId + 1;
        var inactiveClubId = baseId + 2;
        var user = new User
        {
            UserId = baseId,
            Username = $"session-role-user-{baseId}",
            PasswordHash = "unused",
            RealName = "会话角色测试用户",
            AccountStatus = "normal",
            CreatedAt = now
        };
        db.Users.Add(user);
        db.Clubs.AddRange(
            new Club
            {
                ClubId = activeClubId,
                ClubName = "会话运营社团",
                AuditStatus = "approved",
                ClubStatus = "active",
                CreatedAt = now
            },
            new Club
            {
                ClubId = inactiveClubId,
                ClubName = "会话已解散社团",
                AuditStatus = "approved",
                ClubStatus = "inactive",
                CreatedAt = now
            });

        var memberRole = await db.Roles.SingleAsync(role => role.RoleCode == "CLUB_MEMBER");
        var leaderRole = await db.Roles.SingleAsync(role => role.RoleCode == "CLUB_LEADER");
        db.UserRoles.AddRange(
            new UserRole
            {
                UserRoleId = baseId + 3,
                UserId = user.UserId,
                RoleId = memberRole.RoleId,
                ClubId = activeClubId,
                AssignedAt = now
            },
            new UserRole
            {
                UserRoleId = baseId + 4,
                UserId = user.UserId,
                RoleId = leaderRole.RoleId,
                ClubId = inactiveClubId,
                AssignedAt = now
            });
        await db.SaveChangesAsync();

        var session = await authService.GetSessionAsync(user.UserId, "test-token");

        Assert.True(session.Succeeded, session.ErrorMessage);
        Assert.Contains(session.Value!.Roles, role => role.ClubId == activeClubId);
        Assert.DoesNotContain(session.Value.Roles, role => role.ClubId == inactiveClubId);

        var permissionRoles = await authService.GetPermissionRolesAsync(user.UserId);
        Assert.Contains(permissionRoles, role => role.ClubId == activeClubId);
        Assert.DoesNotContain(permissionRoles, role => role.ClubId == inactiveClubId);

        Assert.Equal(
            2,
            await db.UserRoles.CountAsync(role => role.UserId == user.UserId));

        var cachedInactiveRole = new AuthRole(
            leaderRole.RoleId,
            leaderRole.RoleCode,
            leaderRole.RoleName,
            "会话已解散社团负责人",
            leaderRole.RoleScope,
            inactiveClubId,
            [inactiveClubId],
            AuthService.GetRolePermissions(leaderRole.RoleCode),
            leaderRole.PermissionDesc);
        var cachedAuthService = new AuthService(
            db,
            scope.ServiceProvider.GetRequiredService<AuthTokenService>(),
            scope.ServiceProvider.GetRequiredService<IAuthSessionService>(),
            new FixedPermissionSnapshotCache(new PermissionSnapshot(user.UserId, [cachedInactiveRole])));

        var filteredCachedRoles = await cachedAuthService.GetPermissionRolesAsync(user.UserId);
        Assert.DoesNotContain(filteredCachedRoles, role => role.ClubId == inactiveClubId);
    }

    private sealed class FixedPermissionSnapshotCache(PermissionSnapshot snapshot)
        : IPermissionSnapshotCache
    {
        public Task<PermissionSnapshot> GetOrCreateAsync(
            int userId,
            Func<Task<PermissionSnapshot>> factory,
            CancellationToken cancellationToken = default) => Task.FromResult(snapshot);

        public Task<AccountStatusSnapshot> GetAccountStatusAsync(
            int userId,
            Func<Task<AccountStatusSnapshot>> factory,
            CancellationToken cancellationToken = default) => factory();

        public Task InvalidateAsync(
            int userId,
            bool requiredForSafety,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
