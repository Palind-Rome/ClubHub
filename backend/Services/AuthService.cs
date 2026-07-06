using System.Security.Cryptography;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Services;

public class AuthService
{
    private const string NormalStatus = "normal";
    private const string SystemScope = "system";
    private const string ClubScope = "club";
    private const string StudentRole = "STUDENT";
    private const string SystemAdminRole = "SYSTEM_ADMIN";

    private static readonly IReadOnlyList<PermissionDefinition> PermissionCatalog =
    [
        new("profile:view", "查看个人信息", "查看自己的账号、学号、学院、专业、年级和联系方式。"),
        new("profile:update", "维护个人信息", "修改自己的联系方式、邮箱等个人资料。"),
        new("public:view", "浏览公开信息", "浏览公开社团、招募、活动和公告。"),
        new("club:apply", "申请创建社团", "提交社团注册申请并查看审核状态。"),
        new("recruitment:apply", "报名招募", "向社团招募提交报名申请。"),
        new("activity:register", "报名活动", "报名参加公开活动。"),
        new("own:records:view", "查看个人记录", "查看自己的报名、签到、学习和通知记录。"),
        new("club:internal:view", "查看社团内部信息", "查看所在社团的内部成员、通知、资源和讨论区。"),
        new("club:notice:view", "查看社团通知", "查看指定社团发布的内部通知。"),
        new("club:resource:view", "查看社团资源", "查看指定社团内部学习资源。"),
        new("forum:post", "参与讨论区", "发布话题或回复社团讨论区帖子。"),
        new("activity:checkin", "活动签到签退", "参与活动时完成签到和签退。"),
        new("task:own:view", "查看本人任务", "查看分配给自己的项目任务。"),
        new("evaluation:own:view", "查看本人评价", "查看自己的贡献度和考核评价。"),
        new("recruitment:manage", "管理招募", "发布招募并审核报名申请。"),
        new("activity:create", "创建活动", "创建社团活动并提交审批。"),
        new("activity:checkin:manage", "管理活动签到", "维护活动签到码、签退码和签到时间段。"),
        new("notice:publish", "发布社团通知", "向社团、部门或成员发布通知公告。"),
        new("resource:upload", "上传学习资源", "上传社团培训视频、文档资料等学习资源。"),
        new("project:task:manage", "管理项目任务", "分配项目任务、跟踪进度并审核阶段成果。"),
        new("material:borrow:manage", "登记物资借还", "登记物资借用、归还、损坏和赔偿情况。"),
        new("evaluation:draft", "录入考核草稿", "录入成员学期考核或评优评奖草稿。"),
        new("club:info:manage", "维护社团信息", "维护社团简介、联系方式、指导老师和负责人信息。"),
        new("club:member:manage", "管理社团成员", "维护成员部门、小组、职位、任期和成员状态。"),
        new("club:role:assign", "分配社团内部角色", "为本社团成员分配成员或干部角色。"),
        new("budget:apply", "提交经费申请", "提交活动经费预算申请。"),
        new("project:apply", "提交项目申请", "提交社团项目立项申请。"),
        new("club:stats:view", "查看社团统计", "查看指定社团的成员、活动、项目、课程和考核统计。"),
        new("club:operation:view", "查看指导社团运营", "查看指导社团的活动、项目、资源和考核情况。"),
        new("activity:review", "审核活动", "审核社团活动申请。"),
        new("project:review", "审核项目", "审核项目立项和阶段成果。"),
        new("budget:review", "审核经费", "审核活动经费预算。"),
        new("evaluation:review", "审核评价", "审核成员评价或评优结果。"),
        new("club:review", "审核社团申请", "审核社团注册申请。"),
        new("venue:review", "审核场地预约", "审核活动场地预约。"),
        new("club:status:manage", "管理社团状态", "调整社团启用、停用等状态。"),
        new("notice:publish:school", "发布校级通知", "发布面向全校或跨社团的通知公告。"),
        new("forum:moderate", "管理讨论区", "置顶、隐藏或恢复讨论区内容。"),
        new("stats:view", "查看全校统计", "查看全校社团运营统计数据。"),
        new("role:assign:club", "分配社团级角色", "为任意社团分配社团成员、干部、负责人或指导老师角色。"),
        new("user:status:manage", "管理账号状态", "启用或禁用用户账号。"),
        new("role:assign:global", "分配全局角色", "分配普通学生、社团管理员、系统管理员等全局角色。"),
        new("system:log:view", "查看系统日志", "查看系统关键操作日志。"),
        new("system:data:manage", "维护系统数据", "进行系统级数据维护和兜底处理。"),
        new("*", "全部权限", "系统管理员拥有所有功能权限。")
    ];

    private static readonly IReadOnlyList<RoleDefinition> BaseRoles =
    [
        new(
            StudentRole,
            "普通学生",
            SystemScope,
            "注册后默认角色，可维护个人信息、浏览公开内容、申请社团和参与报名。",
            ["profile:view", "profile:update", "public:view", "club:apply", "recruitment:apply", "activity:register", "own:records:view"]),
        new(
            "CLUB_MEMBER",
            "社团成员",
            ClubScope,
            "指定社团内角色，可查看社团内部信息、资源、通知和参与讨论、签到。",
            ["club:internal:view", "club:notice:view", "club:resource:view", "forum:post", "activity:checkin", "task:own:view", "evaluation:own:view"]),
        new(
            "CLUB_OFFICER",
            "社团干部",
            ClubScope,
            "指定社团内角色，可管理招募、活动、通知、资源、项目任务和物资借还。",
            ["club:internal:view", "club:notice:view", "club:resource:view", "forum:post", "activity:checkin", "task:own:view", "evaluation:own:view", "recruitment:manage", "activity:create", "activity:checkin:manage", "notice:publish", "resource:upload", "project:task:manage", "material:borrow:manage", "evaluation:draft"]),
        new(
            "CLUB_LEADER",
            "社团负责人",
            ClubScope,
            "指定社团内最高业务角色，可维护社团信息、成员、社团内部角色和运营统计。",
            ["club:internal:view", "club:notice:view", "club:resource:view", "forum:post", "activity:checkin", "task:own:view", "evaluation:own:view", "recruitment:manage", "activity:create", "activity:checkin:manage", "notice:publish", "resource:upload", "project:task:manage", "material:borrow:manage", "evaluation:draft", "club:info:manage", "club:member:manage", "club:role:assign", "budget:apply", "project:apply", "club:stats:view"]),
        new(
            "ADVISOR",
            "指导老师",
            ClubScope,
            "指定社团指导角色，可查看社团运营并审核活动、项目、经费和评价。",
            ["club:operation:view", "activity:review", "project:review", "budget:review", "evaluation:review", "club:stats:view"]),
        new(
            "CLUB_ADMIN",
            "社团管理员",
            SystemScope,
            "校级社团管理角色，可审核社团、活动、场地、经费、项目并管理社团状态。",
            ["public:view", "club:review", "activity:review", "venue:review", "budget:review", "project:review", "club:status:manage", "notice:publish:school", "forum:moderate", "stats:view", "role:assign:club"]),
        new(
            SystemAdminRole,
            "系统管理员",
            SystemScope,
            "系统最高角色，可管理账号状态、全局角色、系统日志和系统级数据。",
            ["*"])
    ];

    private readonly ClubHubDbContext _db;

    public AuthService(ClubHubDbContext db) => _db = db;

    public async Task<AuthServiceResult<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        var username = NormalizeText(request.Username);
        var password = request.Password;
        var realName = NormalizeText(request.RealName);
        var studentNo = NormalizeText(request.StudentNo);

        if (username.Length < 3)
        {
            return AuthServiceResult<AuthResponse>.Fail(400, "用户名至少需要 3 个字符。");
        }

        if (password.Length < 6)
        {
            return AuthServiceResult<AuthResponse>.Fail(400, "密码至少需要 6 个字符。");
        }

        if (string.IsNullOrWhiteSpace(realName))
        {
            return AuthServiceResult<AuthResponse>.Fail(400, "真实姓名不能为空。");
        }

        if (string.IsNullOrWhiteSpace(studentNo))
        {
            return AuthServiceResult<AuthResponse>.Fail(400, "学号不能为空。");
        }

        if (await _db.Users.AnyAsync(u => u.Username == username))
        {
            return AuthServiceResult<AuthResponse>.Fail(409, "用户名已存在，请更换后再注册。");
        }

        if (await _db.Users.AnyAsync(u => u.StudentNo == studentNo))
        {
            return AuthServiceResult<AuthResponse>.Fail(409, "学号已被注册，请确认信息。");
        }

        var roles = await EnsureBaseRolesAsync();
        var studentRole = roles.Single(r => r.RoleCode == StudentRole);
        var now = DateTime.UtcNow;
        var userId = (await _db.Users.MaxAsync(u => (int?)u.UserId) ?? 0) + 1;
        var userRoleId = (await _db.UserRoles.MaxAsync(ur => (int?)ur.UserRoleId) ?? 0) + 1;

        var user = new User
        {
            UserId = userId,
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            RealName = realName,
            StudentNo = studentNo,
            Gender = NullIfBlank(request.Gender),
            Phone = NullIfBlank(request.Phone),
            Email = NullIfBlank(request.Email),
            College = NullIfBlank(request.College),
            Major = NullIfBlank(request.Major),
            Grade = NullIfBlank(request.Grade),
            AccountStatus = NormalStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Users.Add(user);
        _db.UserRoles.Add(new UserRole
        {
            UserRoleId = userRoleId,
            UserId = user.UserId,
            RoleId = studentRole.RoleId,
            ClubId = null,
            AssignedAt = now
        });
        await _db.SaveChangesAsync();

        return AuthServiceResult<AuthResponse>.Created(await BuildAuthResponseAsync(user));
    }

    public async Task<AuthServiceResult<AuthResponse>> LoginAsync(LoginRequest request)
    {
        var username = NormalizeText(request.Username);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(request.Password))
        {
            return AuthServiceResult<AuthResponse>.Fail(400, "请输入用户名和密码。");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return AuthServiceResult<AuthResponse>.Fail(401, "用户名或密码错误。");
        }

        if (!IsNormalAccount(user))
        {
            return AuthServiceResult<AuthResponse>.Fail(403, "账号已被禁用，请联系管理员。");
        }

        await EnsureBaseRolesAsync();
        return AuthServiceResult<AuthResponse>.Ok(await BuildAuthResponseAsync(user));
    }

    public async Task<IReadOnlyList<RoleDefinition>> GetRoleDefinitionsAsync()
    {
        await EnsureBaseRolesAsync();
        return BaseRoles;
    }

    public IReadOnlyList<PermissionDefinition> GetPermissionCatalog() => PermissionCatalog;

    public async Task<AuthServiceResult<PermissionCheckResult>> CheckPermissionAsync(int userId, string permission, int? clubId)
    {
        permission = NormalizeText(permission);
        if (!PermissionCatalog.Any(p => p.Code == permission))
        {
            return AuthServiceResult<PermissionCheckResult>.Fail(400, "权限编码不存在。");
        }

        var user = await _db.Users.FindAsync(userId);
        if (user is null)
        {
            return AuthServiceResult<PermissionCheckResult>.Fail(404, "用户不存在。");
        }

        var roles = await GetAuthRolesAsync(userId);
        var matchedRoles = roles
            .Where(role => RoleAllows(role, permission, clubId))
            .ToList();
        var allowed = IsNormalAccount(user) && matchedRoles.Count > 0;
        var message = allowed ? "允许访问。" : "当前用户没有该权限。";

        return AuthServiceResult<PermissionCheckResult>.Ok(new PermissionCheckResult(
            userId,
            permission,
            clubId,
            allowed,
            matchedRoles,
            message));
    }

    public async Task<AuthServiceResult<RoleAssignmentResult>> AssignRoleAsync(AssignRoleRequest request)
    {
        var roleCode = NormalizeText(request.RoleCode).ToUpperInvariant();
        var roleDef = BaseRoles.FirstOrDefault(r => r.Code == roleCode);
        if (roleDef is null)
        {
            return AuthServiceResult<RoleAssignmentResult>.Fail(400, "角色编码不存在。");
        }

        if (roleDef.Scope == ClubScope && request.ClubId is null)
        {
            return AuthServiceResult<RoleAssignmentResult>.Fail(400, "社团范围角色必须指定社团。");
        }

        var targetUser = await _db.Users.FindAsync(request.TargetUserId);
        if (targetUser is null)
        {
            return AuthServiceResult<RoleAssignmentResult>.Fail(404, "被分配角色的用户不存在。");
        }

        var roleRows = await EnsureBaseRolesAsync();
        var role = roleRows.Single(r => r.RoleCode == roleCode);
        var clubId = roleDef.Scope == ClubScope ? request.ClubId : null;
        var permissionResult = await CanAssignRoleAsync(request.OperatorUserId, roleDef, clubId);
        if (!permissionResult.Allowed)
        {
            return AuthServiceResult<RoleAssignmentResult>.Fail(403, permissionResult.Message);
        }

        var existing = await _db.UserRoles
            .FirstOrDefaultAsync(ur =>
                ur.UserId == request.TargetUserId &&
                ur.RoleId == role.RoleId &&
                ur.ClubId == clubId);

        var authRole = ToAuthRole(role, clubId);
        if (existing is not null)
        {
            return AuthServiceResult<RoleAssignmentResult>.Ok(new RoleAssignmentResult(
                request.TargetUserId,
                authRole,
                true,
                "用户已经拥有该角色。"));
        }

        var userRoleId = (await _db.UserRoles.MaxAsync(ur => (int?)ur.UserRoleId) ?? 0) + 1;
        _db.UserRoles.Add(new UserRole
        {
            UserRoleId = userRoleId,
            UserId = request.TargetUserId,
            RoleId = role.RoleId,
            ClubId = clubId,
            AssignedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return AuthServiceResult<RoleAssignmentResult>.Ok(new RoleAssignmentResult(
            request.TargetUserId,
            authRole,
            false,
            "角色分配成功。"));
    }

    private async Task<(bool Allowed, string Message)> CanAssignRoleAsync(int operatorUserId, RoleDefinition targetRole, int? clubId)
    {
        var operatorUser = await _db.Users.FindAsync(operatorUserId);
        if (operatorUser is null)
        {
            return (false, "操作人不存在。");
        }

        if (!IsNormalAccount(operatorUser))
        {
            return (false, "操作人账号已被禁用。");
        }

        var roles = await GetAuthRolesAsync(operatorUserId);
        var canAssignGlobal = roles.Any(role => RoleAllows(role, "role:assign:global", clubId));
        if (targetRole.Scope == SystemScope)
        {
            return canAssignGlobal
                ? (true, "允许分配全局角色。")
                : (false, "只有系统管理员可以分配全局角色。");
        }

        var canAssignClub = roles.Any(role => RoleAllows(role, "role:assign:club", clubId));
        var canAssignOwnClub = roles.Any(role => RoleAllows(role, "club:role:assign", clubId));
        if (canAssignGlobal || canAssignClub)
        {
            return (true, "允许分配社团角色。");
        }

        var limitedClubRoles = new[] { "CLUB_MEMBER", "CLUB_OFFICER" };
        if (canAssignOwnClub && limitedClubRoles.Contains(targetRole.Code))
        {
            return (true, "允许分配本社团成员或干部角色。");
        }

        return (false, "当前用户没有分配该角色的权限。");
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(User user)
    {
        var roles = await GetAuthRolesAsync(user.UserId);
        var permissions = roles
            .SelectMany(r => r.Permissions)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        return new AuthResponse(
            GenerateDemoToken(),
            ToAuthUser(user),
            roles,
            permissions);
    }

    private async Task<IReadOnlyList<AuthRole>> GetAuthRolesAsync(int userId)
    {
        var userRoles = await _db.UserRoles
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId)
            .OrderBy(ur => ur.RoleId)
            .ThenBy(ur => ur.ClubId)
            .ToListAsync();

        return userRoles
            .Where(ur => ur.Role is not null)
            .Select(ur => ToAuthRole(ur.Role!, ur.ClubId))
            .ToList();
    }

    private static AuthUser ToAuthUser(User user) => new(
        user.UserId,
        user.Username,
        user.RealName,
        user.StudentNo,
        user.Gender,
        user.Phone,
        user.Email,
        user.College,
        user.Major,
        user.Grade,
        user.AccountStatus);

    private static AuthRole ToAuthRole(Role role, int? clubId)
    {
        var roleDef = BaseRoles.FirstOrDefault(r => r.Code == role.RoleCode);
        return new AuthRole(
            role.RoleId,
            role.RoleCode,
            role.RoleName,
            role.RoleScope,
            clubId,
            roleDef?.Permissions ?? [],
            role.PermissionDesc);
    }

    private static bool RoleAllows(AuthRole role, string permission, int? clubId)
    {
        if (!role.Permissions.Contains("*") && !role.Permissions.Contains(permission))
        {
            return false;
        }

        return role.Scope == SystemScope || (clubId is not null && role.ClubId == clubId);
    }

    private async Task<List<Role>> EnsureBaseRolesAsync()
    {
        var roles = await _db.Roles.ToListAsync();
        var nextId = (roles.Count == 0 ? 0 : roles.Max(r => r.RoleId)) + 1;
        var changed = false;

        foreach (var roleDef in BaseRoles)
        {
            var permissionDesc = BuildPermissionDesc(roleDef);
            var existing = roles.FirstOrDefault(r => r.RoleCode == roleDef.Code);
            if (existing is null)
            {
                existing = new Role
                {
                    RoleId = nextId++,
                    RoleCode = roleDef.Code,
                    RoleName = roleDef.Name,
                    RoleScope = roleDef.Scope,
                    PermissionDesc = permissionDesc,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Roles.Add(existing);
                roles.Add(existing);
                changed = true;
                continue;
            }

            if (existing.RoleName != roleDef.Name ||
                existing.RoleScope != roleDef.Scope ||
                existing.PermissionDesc != permissionDesc)
            {
                existing.RoleName = roleDef.Name;
                existing.RoleScope = roleDef.Scope;
                existing.PermissionDesc = permissionDesc;
                changed = true;
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync();
        }

        return roles;
    }

    private static string BuildPermissionDesc(RoleDefinition roleDef)
    {
        var names = roleDef.Permissions
            .Select(code => PermissionCatalog.FirstOrDefault(p => p.Code == code)?.Name ?? code);
        return $"{roleDef.Description} 基础权限：{string.Join("、", names)}。";
    }

    private static bool IsNormalAccount(User user) =>
        string.Equals(user.AccountStatus, NormalStatus, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeText(string? value) => (value ?? string.Empty).Trim();

    private static string? NullIfBlank(string? value)
    {
        var normalized = NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string GenerateDemoToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }
}
