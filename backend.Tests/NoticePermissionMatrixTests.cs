using ClubHub.Api.Services;

namespace ClubHub.Api.Tests;

public sealed class NoticePermissionMatrixTests
{
    private const int ManagedClubId = 10;
    private const int OtherClubId = 20;

    [Theory]
    [InlineData("STUDENT", false, false, false)]
    [InlineData("TEACHER", false, false, false)]
    [InlineData("CLUB_MEMBER", true, false, false)]
    [InlineData("CLUB_OFFICER", true, true, false)]
    [InlineData("CLUB_LEADER", true, true, false)]
    [InlineData("ADVISOR", true, true, false)]
    [InlineData("VENUE_ADMIN", false, false, false)]
    [InlineData("CLUB_ADMIN", true, true, true)]
    [InlineData("SYSTEM_ADMIN", true, true, true)]
    public void RolePermissions_EnforceNoticeScope(
        string roleCode,
        bool canViewManagedClub,
        bool canPublishManagedClub,
        bool canPublishSchool)
    {
        var roles = new[] { CreateRole(roleCode, ManagedClubId) };

        Assert.Equal(
            canViewManagedClub,
            NoticeAuthorizationPolicy.CanViewClub(roles, ManagedClubId));
        Assert.Equal(
            canPublishManagedClub,
            NoticeAuthorizationPolicy.CanPublishForClub(roles, ManagedClubId));
        Assert.Equal(
            canPublishSchool,
            NoticeAuthorizationPolicy.CanPublishSchool(roles));

        var hasGlobalScope = roleCode is "CLUB_ADMIN" or "SYSTEM_ADMIN";
        Assert.Equal(
            hasGlobalScope && canViewManagedClub,
            NoticeAuthorizationPolicy.CanViewClub(roles, OtherClubId));
        Assert.Equal(
            hasGlobalScope && canPublishManagedClub,
            NoticeAuthorizationPolicy.CanPublishForClub(roles, OtherClubId));
    }

    [Fact]
    public void AdvisorNoticePermissions_MatchClubLeader()
    {
        var advisorPermissions = NoticePermissionsFor("ADVISOR");
        var leaderPermissions = NoticePermissionsFor("CLUB_LEADER");

        Assert.Equal(leaderPermissions, advisorPermissions);
    }

    [Fact]
    public void StudentMemberPermissions_AreLimitedToJoinedClubs()
    {
        var permissions = AuthService.GetRolePermissions("STUDENT")
            .Concat(AuthService.GetRolePermissions("CLUB_MEMBER"))
            .Distinct()
            .ToArray();
        var roles = new[]
        {
            new AuthRole(
                1,
                "STUDENT",
                "普通学生",
                "普通学生",
                "system",
                null,
                [ManagedClubId],
                permissions,
                null)
        };

        Assert.True(NoticeAuthorizationPolicy.CanViewClub(roles, ManagedClubId));
        Assert.False(NoticeAuthorizationPolicy.CanViewClub(roles, OtherClubId));
        Assert.False(NoticeAuthorizationPolicy.CanPublishForClub(roles, ManagedClubId));
    }

    [Fact]
    public void ReadStatistics_AreVisibleOnlyToPublisherOrScopedManager()
    {
        var memberRoles = new[] { CreateRole("CLUB_MEMBER", ManagedClubId) };
        var officerRoles = new[] { CreateRole("CLUB_OFFICER", ManagedClubId) };

        Assert.True(NoticeAuthorizationPolicy.CanViewStatistics(
            memberRoles,
            viewerUserId: 7,
            publisherUserId: 7,
            clubId: ManagedClubId));
        Assert.False(NoticeAuthorizationPolicy.CanViewStatistics(
            memberRoles,
            viewerUserId: 7,
            publisherUserId: 8,
            clubId: ManagedClubId));
        Assert.True(NoticeAuthorizationPolicy.CanViewStatistics(
            officerRoles,
            viewerUserId: 7,
            publisherUserId: 8,
            clubId: ManagedClubId));
        Assert.False(NoticeAuthorizationPolicy.CanViewStatistics(
            officerRoles,
            viewerUserId: 7,
            publisherUserId: 8,
            clubId: OtherClubId));
    }

    private static AuthRole CreateRole(string roleCode, int clubId)
    {
        var isSystemRole = roleCode is "STUDENT" or "TEACHER" or "VENUE_ADMIN" or
            "CLUB_ADMIN" or "SYSTEM_ADMIN";
        IReadOnlyList<int> scopedClubIds = isSystemRole ? [] : [clubId];

        return new AuthRole(
            1,
            roleCode,
            roleCode,
            roleCode,
            isSystemRole ? "system" : "club",
            roleCode == "ADVISOR" || isSystemRole ? null : clubId,
            scopedClubIds,
            AuthService.GetRolePermissions(roleCode),
            null);
    }

    private static string[] NoticePermissionsFor(string roleCode) =>
        AuthService.GetRolePermissions(roleCode)
            .Where(permission => permission is
                NoticeAuthorizationPolicy.ClubViewPermission or
                NoticeAuthorizationPolicy.ClubPublishPermission)
            .OrderBy(permission => permission)
            .ToArray();
}
