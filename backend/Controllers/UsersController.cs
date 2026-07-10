using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ClubHubDbContext _db;
    private static readonly HashSet<string> PrincipalPositionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "\u8d1f\u8d23\u4eba",
        "\u4f1a\u957f",
        "\u793e\u957f",
        "\u793e\u56e2\u8d1f\u8d23\u4eba",
        "president",
        "leader",
        "club president",
        "club leader"
    };

    private static readonly HashSet<string> OfficerPositionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "\u5e72\u90e8",
        "\u90e8\u957f",
        "\u526f\u90e8\u957f",
        "\u793e\u56e2\u5e72\u90e8",
        "officer",
        "club officer",
        "manager"
    };

    public UsersController(ClubHubDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? clubId)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });
        }

        var viewer = await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId.Value);
        if (viewer is null)
        {
            return NotFound(new { message = "当前登录用户不存在。" });
        }

        var query = _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Club)
            .Include(u => u.ClubMemberships)
                .ThenInclude(cm => cm.Club)
            .AsQueryable();

        if (clubId is not null)
        {
            var clubExists = await _db.Clubs.AnyAsync(club => club.ClubId == clubId.Value);
            if (!clubExists)
            {
                return NotFound(new { message = "社团不存在。" });
            }

            if (!IsSystemAdmin(viewer) && !IsClubPrincipal(viewer, clubId.Value) && !IsClubOfficer(viewer, clubId.Value))
            {
                return StatusCode(403, new { message = "只有系统管理员、本社团负责人或干部可以查看可分配成员。" });
            }

            query = query.Where(u =>
                (u.AccountStatus == null ||
                 u.AccountStatus == "" ||
                 u.AccountStatus.ToLower() == "active" ||
                 u.AccountStatus.ToLower() == "normal" ||
                 u.AccountStatus.ToLower() == "enabled") &&
                u.ClubMemberships.Any(cm =>
                    cm.ClubId == clubId.Value &&
                    (cm.MemberStatus == null ||
                     cm.MemberStatus == "" ||
                     cm.MemberStatus.ToLower() == "active" ||
                     cm.MemberStatus.ToLower() == "normal" ||
                     cm.MemberStatus.ToLower() == "enabled")));
        }
        else if (!IsPlatformAdmin(viewer))
        {
            query = query.Where(u => u.UserId == viewer.UserId);
        }

        var users = await query
            .OrderBy(u => u.UserId)
            .ToListAsync();

        return Ok(users.Select(ToUserSummary));
    }

    internal static bool IsPlatformAdmin(User user) =>
        user.UserRoles.Any(ur => IsPlatformAdminRole(ur.Role));

    internal static bool IsSystemAdmin(User user) =>
        user.UserRoles.Any(ur => IsSystemAdminRole(ur.Role));

    internal static bool IsClubPrincipal(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            KnownClubPrincipalRoleCodes.Contains(Normalize(ur.Role.RoleCode))) ||
        user.ClubMemberships.Any(cm =>
            cm.ClubId == clubId &&
            IsActive(cm.MemberStatus) &&
            IsStrictPrincipalPosition(cm.PositionName));

    internal static bool IsClubOfficer(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            KnownClubOfficerRoleCodes.Contains(Normalize(ur.Role.RoleCode))) ||
        user.ClubMemberships.Any(cm =>
            cm.ClubId == clubId &&
            IsActive(cm.MemberStatus) &&
            IsOfficerPosition(cm.PositionName));

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

    private static bool IsSystemAdminRole(Role? role)
    {
        if (role is null) return false;

        return Normalize(role.RoleCode) is "system_admin" or "sysadmin" ||
               (role.RoleName ?? string.Empty).Contains("系统管理员", StringComparison.Ordinal);
    }

    private static bool IsStrictPrincipalPosition(string? positionName)
    {
        if (string.IsNullOrWhiteSpace(positionName)) return false;

        var normalized = positionName.Trim();
        if (normalized.StartsWith("\u526f", StringComparison.Ordinal)) return false;

        return PrincipalPositionNames.Contains(normalized);
    }

    private static bool IsOfficerPosition(string? positionName)
    {
        if (string.IsNullOrWhiteSpace(positionName)) return false;

        var normalized = positionName.Trim();
        return OfficerPositionNames.Contains(normalized);
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
        "club_admin",
        "system_admin",
        "admin",
        "club_reviewer"
    };

    private static readonly HashSet<string> KnownClubPrincipalRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "club_president",
        "club_leader",
        "club_manager",
        "president"
    };

    private static readonly HashSet<string> KnownClubOfficerRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "club_officer",
        "officer",
        "club_manager"
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
