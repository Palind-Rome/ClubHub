using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly ClubHubDbContext _db;

    public UsersController(ClubHubDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Club)
            .Include(u => u.ClubMemberships)
                .ThenInclude(cm => cm.Club)
            .OrderBy(u => u.UserId)
            .ToListAsync();

        return Ok(users.Select(ToUserSummary));
    }

    internal static bool IsPlatformAdmin(User user) =>
        user.UserRoles.Any(ur => IsPlatformAdminRole(ur.Role));

    internal static bool IsClubPrincipal(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            KnownClubPrincipalRoleCodes.Contains(Normalize(ur.Role.RoleCode))) ||
        user.ClubMemberships.Any(cm =>
            cm.ClubId == clubId &&
            IsActive(cm.MemberStatus) &&
            IsPrincipalPosition(cm.PositionName));

    internal static bool IsActive(string? accountOrMemberStatus) =>
        string.IsNullOrWhiteSpace(accountOrMemberStatus) ||
        Normalize(accountOrMemberStatus) is "active" or "normal" or "enabled" or "在任" or "正常";

    private static UserSummaryDto ToUserSummary(User user)
    {
        var roles = user.UserRoles
            .OrderBy(ur => ur.ClubId ?? 0)
            .ThenBy(ur => ur.Role?.RoleName)
            .Select(ur => new UserRoleSummaryDto(
                ur.Role?.RoleCode ?? string.Empty,
                ur.Role?.RoleName ?? "未命名角色",
                ur.Role?.RoleScope,
                ur.ClubId,
                ur.Club?.ClubName))
            .ToList();

        var memberships = user.ClubMemberships
            .OrderBy(cm => cm.Club?.ClubName)
            .ThenBy(cm => cm.PositionName)
            .Select(cm => new UserMembershipSummaryDto(
                cm.ClubId,
                cm.Club?.ClubName ?? $"社团 {cm.ClubId}",
                cm.DepartmentName,
                cm.GroupName,
                cm.PositionName,
                cm.TermName,
                cm.MemberStatus))
            .ToList();

        return new UserSummaryDto(
            user.UserId,
            user.Username,
            user.RealName,
            user.StudentNo,
            DisplayUser(user),
            user.AccountStatus,
            roles,
            memberships,
            IsActive(user.AccountStatus) && IsStudent(user) && !IsPlatformAdmin(user),
            IsActive(user.AccountStatus) && IsPlatformAdmin(user));
    }

    internal static bool IsStudent(User user) =>
        user.UserRoles.Any(ur =>
            ur.Role is not null &&
            (Normalize(ur.Role.RoleCode) is "student" or "stu" ||
             (ur.Role.RoleName ?? string.Empty).Contains("学生", StringComparison.Ordinal)));

    private static bool IsPlatformAdminRole(Role? role)
    {
        if (role is null) return false;

        var code = Normalize(role.RoleCode);
        if (KnownPlatformAdminRoleCodes.Contains(code)) return true;

        return (role.RoleName ?? string.Empty).Contains("管理员", StringComparison.Ordinal) &&
               ((role.RoleScope ?? string.Empty).Contains("平台", StringComparison.Ordinal) ||
                (role.PermissionDesc ?? string.Empty).Contains("审核", StringComparison.Ordinal));
    }

    private static bool IsPrincipalPosition(string? positionName)
    {
        if (string.IsNullOrWhiteSpace(positionName)) return false;

        return positionName.Contains("负责人", StringComparison.Ordinal) ||
               positionName.Contains("会长", StringComparison.Ordinal) ||
               positionName.Contains("社长", StringComparison.Ordinal) ||
               positionName.Contains("leader", StringComparison.OrdinalIgnoreCase) ||
               positionName.Contains("president", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayUser(User user)
    {
        var name = !string.IsNullOrWhiteSpace(user.RealName) ? user.RealName : user.Username;
        var studentNo = string.IsNullOrWhiteSpace(user.StudentNo) ? string.Empty : $"（{user.StudentNo}）";
        return string.IsNullOrWhiteSpace(name) ? $"用户 {user.UserId}" : $"{name}{studentNo}";
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static readonly HashSet<string> KnownPlatformAdminRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "platform_admin",
        "system_admin",
        "admin",
        "club_reviewer"
    };

    private static readonly HashSet<string> KnownClubPrincipalRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "club_president",
        "club_leader",
        "club_admin",
        "club_manager",
        "president"
    };
}

public record UserSummaryDto(
    int Id,
    string? Username,
    string? RealName,
    string? StudentNo,
    string DisplayName,
    string? AccountStatus,
    IReadOnlyList<UserRoleSummaryDto> Roles,
    IReadOnlyList<UserMembershipSummaryDto> Memberships,
    bool CanSubmitClubApplication,
    bool CanReviewClubApplication);

public record UserRoleSummaryDto(
    string RoleCode,
    string RoleName,
    string? RoleScope,
    int? ClubId,
    string? ClubName);

public record UserMembershipSummaryDto(
    int ClubId,
    string ClubName,
    string? DepartmentName,
    string? GroupName,
    string? PositionName,
    string? TermName,
    string? MemberStatus);
