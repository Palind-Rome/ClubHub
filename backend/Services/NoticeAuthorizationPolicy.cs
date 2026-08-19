namespace ClubHub.Api.Services;

internal static class NoticeAuthorizationPolicy
{
    internal const string ClubViewPermission = "club:notice:view";
    internal const string ClubPublishPermission = "notice:publish";
    internal const string SchoolPublishPermission = "notice:publish:school";

    internal static bool CanPublishSchool(IReadOnlyList<AuthRole> roles) =>
        AuthService.RolesAllow(roles, SchoolPublishPermission, null);

    internal static bool CanPublishForClub(IReadOnlyList<AuthRole> roles, int clubId) =>
        CanPublishSchool(roles) ||
        AuthService.RolesAllow(roles, ClubPublishPermission, clubId);

    internal static bool CanViewClub(IReadOnlyList<AuthRole> roles, int clubId) =>
        CanPublishForClub(roles, clubId) ||
        AuthService.RolesAllow(roles, ClubViewPermission, clubId);

    internal static bool CanManageNotice(IReadOnlyList<AuthRole> roles, int? clubId) =>
        clubId is null
            ? CanPublishSchool(roles)
            : CanPublishForClub(roles, clubId.Value);

    internal static bool CanViewStatistics(
        IReadOnlyList<AuthRole> roles,
        int viewerUserId,
        int publisherUserId,
        int? clubId) =>
        viewerUserId == publisherUserId ||
        CanManageNotice(roles, clubId);
}
