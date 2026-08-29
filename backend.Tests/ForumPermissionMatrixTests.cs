using ClubHub.Api.Services;

namespace ClubHub.Api.Tests;

public sealed class ForumPermissionMatrixTests
{
    [Fact]
    public void AdvisorCanParticipateInAndModerateAssignedClubForum()
    {
        var permissions = AuthService.GetRolePermissions("ADVISOR");

        Assert.Contains("forum:post", permissions);
        Assert.Contains("forum:moderate", permissions);
    }

    [Theory]
    [InlineData("CLUB_OFFICER")]
    [InlineData("CLUB_LEADER")]
    public void ClubManagersCanBothPostAndModerate(string roleCode)
    {
        var permissions = AuthService.GetRolePermissions(roleCode);

        Assert.Contains("forum:post", permissions);
        Assert.Contains("forum:moderate", permissions);
    }
}
