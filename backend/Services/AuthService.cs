using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Org.OpenAPITools.Models;

namespace ClubHub.Api.Services;

public class AuthService
{
    private const string NormalStatus = "normal";
    private const string SystemScope = "system";
    private const string ClubScope = "club";
    private const string StudentRole = "STUDENT";
    private const string TeacherRole = "TEACHER";
    private const string ClubMemberRole = "CLUB_MEMBER";
    private const string ClubOfficerRole = "CLUB_OFFICER";
    private const string ClubLeaderRole = "CLUB_LEADER";
    private const string AdvisorRole = "ADVISOR";
    private const string VenueAdminRole = "VENUE_ADMIN";
    private const string SystemAdminRole = "SYSTEM_ADMIN";
    private const int StudentNoLength = 7;
    private const int StaffNoLength = 5;

    private static readonly IReadOnlyList<PermissionDefinition> PermissionCatalog =
    [
        new("profile:view", "查看个人信息", "查看自己的账号、学工号、学院、专业、年级和联系方式。"),
        new("profile:update", "维护个人信息", "修改自己的联系方式、邮箱等个人资料。"),
        new("public:view", "浏览公开信息", "浏览公开社团、招募、活动和公告。"),
        new("club:apply", "申请创建社团", "提交社团注册申请并查看审核状态。"),
        new("recruitment:apply", "报名招募", "向社团招募提交报名申请。"),
        new("activity:register", "报名活动", "报名参加公开活动。"),
        new("course:enroll", "报名课程", "加入对当前学生开放且仍有名额的培训课程。"),
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
        new("resource:review", "审核学习资源", "审核社团提交的课程和学习资源并决定发布或驳回。"),
        new("resource:delete", "删除学习资源", "删除课程和学习资源并清理相关学习记录与文件。"),
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
        new("venue:reserve", "提交场地预约", "为有运营权限的社团提交场地预约并查看本社团预约。"),
        new("venue:create", "创建场地", "新增可预约场地基础信息。"),
        new("venue:update", "维护场地", "维护场地名称、位置、容量和负责人信息。"),
        new("venue:disable", "停用或恢复场地", "将场地切换为可预约、维护中或停用状态。"),
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
            ["profile:view", "profile:update", "public:view", "club:apply", "recruitment:apply", "activity:register", "course:enroll", "own:records:view"]),
        new(
            TeacherRole,
            "教师",
            SystemScope,
            "教师基础身份，可维护个人信息并浏览公开社团、招募、活动和公告。",
            ["profile:view", "profile:update", "public:view", "own:records:view"]),
        new(
            ClubMemberRole,
            "社团成员",
            ClubScope,
            "指定社团内角色，可查看社团内部信息、资源、通知和参与讨论、签到。",
            ["club:internal:view", "club:notice:view", "club:resource:view", "forum:post", "activity:checkin", "task:own:view", "evaluation:own:view"]),
        new(
            ClubOfficerRole,
            "社团干部",
            ClubScope,
            "指定社团内角色，可管理招募、活动、通知、资源、项目任务和物资借还。",
            ["club:internal:view", "club:notice:view", "club:resource:view", "forum:post", "activity:checkin", "task:own:view", "evaluation:own:view", "recruitment:manage", "activity:create", "activity:checkin:manage", "notice:publish", "resource:upload", "project:task:manage", "material:borrow:manage", "evaluation:draft", "venue:reserve"]),
        new(
            ClubLeaderRole,
            "社团负责人",
            ClubScope,
            "指定社团内最高业务角色，可维护社团信息、成员、社团内部角色和运营统计。",
            ["club:internal:view", "club:notice:view", "club:resource:view", "forum:post", "activity:checkin", "task:own:view", "evaluation:own:view", "recruitment:manage", "activity:create", "activity:checkin:manage", "notice:publish", "resource:upload", "project:task:manage", "material:borrow:manage", "evaluation:draft", "club:info:manage", "club:member:manage", "club:role:assign", "budget:apply", "project:apply", "club:stats:view", "venue:reserve"]),
        new(
            AdvisorRole,
            "指导老师",
            ClubScope,
            "指定社团指导角色，可查看社团运营、维护并审核学习资源，以及审核活动、项目、经费和评价，可按负责人权限维护成员、考核与评奖评优。",
            ["club:internal:view", "club:operation:view", "resource:upload", "resource:review", "activity:review", "project:review", "budget:review", "evaluation:review", "evaluation:draft", "club:info:manage", "club:member:manage", "club:role:assign", "club:stats:view"]),
        new(
            "CLUB_ADMIN",
            "社团管理员",
            SystemScope,
            "校级社团管理角色，可审核社团注册申请并管理社团状态，不参与社团内部档案、成员任期和干部换届维护。",
            ["public:view", "club:review", "activity:review", "venue:review", "budget:review", "project:review", "resource:review", "resource:delete", "club:status:manage", "notice:publish:school", "forum:moderate", "stats:view"]),
        new(
            VenueAdminRole,
            "场地管理员",
            SystemScope,
            "校级场地管理角色，可维护场地基础信息、停用或恢复场地，并审核场地预约。",
            ["venue:create", "venue:update", "venue:disable", "venue:review"]),
        new(
            SystemAdminRole,
            "系统管理员",
            SystemScope,
            "系统最高角色，可管理账号状态、全局角色、系统日志和系统级数据。",
            ["*"])
    ];

    private readonly ClubHubDbContext _db;
    private readonly AuthTokenService _authTokenService;

    public AuthService(ClubHubDbContext db, AuthTokenService authTokenService)
    {
        _db = db;
        _authTokenService = authTokenService;
    }

    public async Task InitializeBaseRolesAsync()
    {
        await EnsureBaseRolesAsync();
    }

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
            return AuthServiceResult<AuthResponse>.Fail(400, "学工号不能为空。");
        }

        if (!IsValidStudentOrStaffNo(studentNo))
        {
            return AuthServiceResult<AuthResponse>.Fail(400, StudentOrStaffNoRuleMessage());
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        if (await _db.Users.AnyAsync(u => u.Username == username))
        {
            return AuthServiceResult<AuthResponse>.Fail(409, "用户名已存在，请更换后再注册。");
        }

        if (await _db.Users.AnyAsync(u => u.StudentNo == studentNo))
        {
            return AuthServiceResult<AuthResponse>.Fail(409, "学工号已被注册，请确认信息。");
        }

        var roles = await GetBaseRoleRowsAsync();
        var now = DateTime.UtcNow;

        var user = new User
        {
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

        try
        {
            await _db.SaveChangesAsync();
            await EnsureIdentityRoleAsync(user, roles, now);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync();

            var usernameExists = await _db.Users
                .AsNoTracking()
                .AnyAsync(existingUser => existingUser.Username == username);
            var studentNoExists = await _db.Users
                .AsNoTracking()
                .AnyAsync(existingUser => existingUser.StudentNo == studentNo);

            if (usernameExists && studentNoExists)
            {
                return AuthServiceResult<AuthResponse>.Fail(409, "用户名和学工号均已被注册，请更换后再注册。");
            }

            if (usernameExists)
            {
                return AuthServiceResult<AuthResponse>.Fail(409, "用户名已存在，请更换后再注册。");
            }

            if (studentNoExists)
            {
                return AuthServiceResult<AuthResponse>.Fail(409, "学工号已被注册，请确认信息。");
            }

            throw;
        }

        return AuthServiceResult<AuthResponse>.Created(await BuildAuthResponseAsync(user));
    }

    public async Task<AuthServiceResult<AuthResponse>> LoginAsync(LoginRequest request)
    {
        var loginName = NormalizeText(request.Username);
        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrEmpty(request.Password))
        {
            return AuthServiceResult<AuthResponse>.Fail(400, "请输入用户名或学工号和密码。");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == loginName || u.StudentNo == loginName);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return AuthServiceResult<AuthResponse>.Fail(401, "用户名/学工号或密码错误。");
        }

        if (!IsNormalAccount(user))
        {
            return AuthServiceResult<AuthResponse>.Fail(403, "账号已被禁用，请联系管理员。");
        }

        var roles = await GetBaseRoleRowsAsync();
        var identityRole = await EnsureIdentityRoleAsync(user, roles, DateTime.UtcNow);
        if (identityRole is not null)
        {
            await SaveIdentityRoleAsync(user, roles, identityRole);
        }

        return AuthServiceResult<AuthResponse>.Ok(await BuildAuthResponseAsync(user));
    }

    public async Task<AuthServiceResult<AuthResponse>> GetSessionAsync(int userId)
    {
        if (userId <= 0)
        {
            return AuthServiceResult<AuthResponse>.Fail(400, "请提供当前登录用户。");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user is null)
        {
            return AuthServiceResult<AuthResponse>.Fail(404, "当前登录用户不存在。");
        }

        if (!IsNormalAccount(user))
        {
            return AuthServiceResult<AuthResponse>.Fail(403, "账号已被禁用，请联系管理员。");
        }

        var roles = await GetBaseRoleRowsAsync();
        var identityRole = await EnsureIdentityRoleAsync(user, roles, DateTime.UtcNow);
        if (identityRole is not null)
        {
            await SaveIdentityRoleAsync(user, roles, identityRole);
        }

        return AuthServiceResult<AuthResponse>.Ok(await BuildAuthResponseAsync(user));
    }

    public Task<IReadOnlyList<RoleDefinition>> GetRoleDefinitionsAsync() => Task.FromResult(BaseRoles);

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

        var roles = await GetPermissionRolesAsync(userId);
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

    public async Task<AuthServiceResult<IReadOnlyList<int>>> GetPermissionClubIdsAsync(int userId, string permission)
    {
        permission = NormalizeText(permission);
        if (!PermissionCatalog.Any(p => p.Code == permission))
        {
            return AuthServiceResult<IReadOnlyList<int>>.Fail(400, "权限编码不存在。");
        }

        var user = await _db.Users.FindAsync(userId);
        if (user is null)
        {
            return AuthServiceResult<IReadOnlyList<int>>.Fail(404, "用户不存在。");
        }

        if (!IsNormalAccount(user))
        {
            return AuthServiceResult<IReadOnlyList<int>>.Ok([]);
        }

        var roles = await GetPermissionRolesAsync(userId);
        var clubIds = roles
            .SelectMany(role => role.ClubIds.Concat(role.ClubId is null ? [] : [role.ClubId.Value]))
            .Distinct()
            .Where(clubId => roles.Any(role => RoleAllows(role, permission, clubId)))
            .OrderBy(clubId => clubId)
            .ToList();

        return AuthServiceResult<IReadOnlyList<int>>.Ok(clubIds);
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

        var roleRows = await GetBaseRoleRowsAsync();
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

        var assignment = new UserRole
        {
            UserId = request.TargetUserId,
            RoleId = role.RoleId,
            ClubId = clubId,
            AssignedAt = DateTime.UtcNow
        };
        _db.UserRoles.Add(assignment);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _db.Entry(assignment).State = EntityState.Detached;
            var concurrentAssignmentExists = await _db.UserRoles
                .AsNoTracking()
                .AnyAsync(ur =>
                    ur.UserId == request.TargetUserId &&
                    ur.RoleId == role.RoleId &&
                    ur.ClubId == clubId);
            if (!concurrentAssignmentExists)
            {
                throw;
            }

            return AuthServiceResult<RoleAssignmentResult>.Ok(new RoleAssignmentResult(
                request.TargetUserId,
                authRole,
                true,
                "用户已经拥有该角色。"));
        }

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

        var roles = await GetPermissionRolesAsync(operatorUserId);
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

        var limitedClubRoles = new[] { ClubMemberRole, ClubOfficerRole };
        if (canAssignOwnClub && limitedClubRoles.Contains(targetRole.Code))
        {
            return (true, "允许分配本社团成员或干部角色。");
        }

        return (false, "当前用户没有分配该角色的权限。");
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(User user)
    {
        var rawRoles = await GetRawAuthRolesAsync(user.UserId);
        var displayRoles = await BuildDisplayRolesAsync(rawRoles);
        var permissions = BuildPermissionRoles(rawRoles)
            .SelectMany(r => r.Permissions)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        return new AuthResponse(
            _authTokenService.CreateToken(user),
            ToAuthUser(user),
            displayRoles,
            permissions);
    }

    public async Task<IReadOnlyList<AuthRole>> GetPermissionRolesAsync(int userId)
    {
        var rawRoles = await GetRawAuthRolesAsync(userId);
        return BuildPermissionRoles(rawRoles);
    }

    private async Task<IReadOnlyList<AuthRole>> GetRawAuthRolesAsync(int userId)
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

    private async Task<IReadOnlyList<AuthRole>> BuildDisplayRolesAsync(IReadOnlyList<AuthRole> rawRoles)
    {
        var clubIds = rawRoles
            .Where(role => role.ClubId is not null)
            .Select(role => role.ClubId!.Value)
            .Distinct()
            .ToList();
        var clubNames = clubIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Clubs
                .Where(club => clubIds.Contains(club.ClubId))
                .ToDictionaryAsync(club => club.ClubId, club => club.ClubName);
        var displayRoles = new List<AuthRole>();

        foreach (var role in rawRoles)
        {
            if (role.Code == StudentRole)
            {
                AddDisplayRole(role with { DisplayName = role.Name, ClubId = null, ClubIds = [] });
                continue;
            }

            if (IsStudentClubRole(role))
            {
                var clubId = role.ClubId!.Value;
                AddDisplayRole(role with
                {
                    DisplayName = BuildScopedDisplayName(GetClubName(clubId), role.Name),
                    ClubIds = [clubId]
                });
                continue;
            }

            if (role.Code == AdvisorRole)
            {
                if (role.ClubId is not null)
                {
                    var clubId = role.ClubId.Value;
                    AddDisplayRole(role with
                    {
                        DisplayName = BuildScopedDisplayName(GetClubName(clubId), role.Name),
                        ClubIds = [clubId]
                    });
                }

                continue;
            }

            AddDisplayRole(role with { DisplayName = role.Name });
        }

        return displayRoles;

        string GetClubName(int clubId) =>
            clubNames.TryGetValue(clubId, out var clubName) && !string.IsNullOrWhiteSpace(clubName)
                ? clubName
                : $"社团{clubId}";

        void AddDisplayRole(AuthRole role)
        {
            if (displayRoles.Any(existing => existing.Code == role.Code && existing.ClubId == role.ClubId))
            {
                return;
            }

            displayRoles.Add(role);
        }
    }

    private static IReadOnlyList<AuthRole> BuildPermissionRoles(IReadOnlyList<AuthRole> rawRoles)
    {
        var permissionRoles = new List<AuthRole>();
        AuthRole? permissionStudentRole = null;
        AuthRole? permissionAdvisorRole = null;

        foreach (var role in rawRoles)
        {
            if (role.Code == StudentRole)
            {
                permissionStudentRole ??= BuildStudentRole(rawRoles, role);
                if (!permissionRoles.Any(r => r.Code == StudentRole))
                {
                    permissionRoles.Add(permissionStudentRole);
                }
                continue;
            }

            if (role.Code == ClubMemberRole && rawRoles.Any(r => r.Code == StudentRole))
            {
                continue;
            }

            if (role.Code == AdvisorRole)
            {
                if (role.ClubId is null)
                {
                    continue;
                }

                permissionAdvisorRole ??= BuildAdvisorRole(rawRoles, role);
                if (!permissionRoles.Any(r => r.Code == AdvisorRole))
                {
                    permissionRoles.Add(permissionAdvisorRole);
                }
                continue;
            }

            permissionRoles.Add(role);
        }

        return permissionRoles;
    }

    private static AuthRole BuildStudentRole(IReadOnlyList<AuthRole> rawRoles, AuthRole template)
    {
        var clubIds = rawRoles
            .Where(role => role.Code == ClubMemberRole && role.ClubId is not null)
            .Select(role => role.ClubId!.Value)
            .Distinct()
            .OrderBy(clubId => clubId)
            .ToList();
        var permissions = MergePermissions(
            GetRolePermissions(StudentRole),
            GetRolePermissions(ClubMemberRole));

        return template with
        {
            ClubId = null,
            ClubIds = clubIds,
            Permissions = permissions
        };
    }

    private static AuthRole BuildAdvisorRole(IReadOnlyList<AuthRole> rawRoles, AuthRole template)
    {
        var clubIds = rawRoles
            .Where(role => role.Code == AdvisorRole && role.ClubId is not null)
            .Select(role => role.ClubId!.Value)
            .Distinct()
            .OrderBy(clubId => clubId)
            .ToList();

        return template with
        {
            ClubId = null,
            ClubIds = clubIds
        };
    }

    private static bool IsStudentClubRole(AuthRole role) =>
        role.ClubId is not null && role.Code is ClubMemberRole or ClubOfficerRole or ClubLeaderRole;

    private static string BuildScopedDisplayName(string clubName, string roleName)
    {
        const string clubPrefix = "社团";
        var roleSuffix = roleName.StartsWith(clubPrefix, StringComparison.Ordinal)
            ? roleName[clubPrefix.Length..]
            : roleName;
        return $"{clubName}{roleSuffix}";
    }

    private static IReadOnlyList<string> GetRolePermissions(string roleCode) =>
        BaseRoles.FirstOrDefault(role => role.Code == roleCode)?.Permissions ?? [];

    private static IReadOnlyList<string> MergePermissions(params IReadOnlyList<string>[] permissionGroups) =>
        permissionGroups
            .SelectMany(permission => permission)
            .Distinct()
            .OrderBy(permission => permission)
            .ToList();

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
            role.RoleName,
            role.RoleScope,
            clubId,
            clubId is null ? [] : [clubId.Value],
            roleDef?.Permissions ?? [],
            role.PermissionDesc);
    }

    private static bool RoleAllows(AuthRole role, string permission, int? clubId)
    {
        if (role.Permissions.Contains("*"))
        {
            return true;
        }

        if (!role.Permissions.Contains(permission))
        {
            return false;
        }

        if (role.Code == StudentRole && GetRolePermissions(ClubMemberRole).Contains(permission))
        {
            return clubId is not null && role.ClubIds.Contains(clubId.Value);
        }

        if (role.Code == AdvisorRole)
        {
            return clubId is not null && role.ClubIds.Contains(clubId.Value);
        }

        if (role.Scope == SystemScope)
        {
            return true;
        }

        return clubId is not null &&
            (role.ClubId == clubId || role.ClubIds.Contains(clubId.Value));
    }

    public static bool RolesAllow(
        IReadOnlyList<AuthRole> roles,
        string permission,
        int? clubId) => roles.Any(role => RoleAllows(role, permission, clubId));

    private async Task<List<Role>> GetBaseRoleRowsAsync()
    {
        var roleCodes = BaseRoles.Select(role => role.Code).ToList();
        var roles = await _db.Roles
            .Where(role => roleCodes.Contains(role.RoleCode))
            .ToListAsync();

        if (roles.Count < BaseRoles.Count)
        {
            throw new InvalidOperationException("基础角色尚未初始化，请检查应用启动种子流程。");
        }

        return roles;
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

    private async Task<UserRole?> EnsureIdentityRoleAsync(
        User user,
        IReadOnlyList<Role> roles,
        DateTime now)
    {
        var roleCode = GetDefaultIdentityRoleCode(user.StudentNo);
        if (roleCode is null)
        {
            return null;
        }

        var role = roles.Single(r => r.RoleCode == roleCode);
        var exists = await _db.UserRoles.AnyAsync(ur =>
            ur.UserId == user.UserId &&
            ur.RoleId == role.RoleId &&
            ur.ClubId == null);
        if (exists)
        {
            return null;
        }

        var assignment = new UserRole
        {
            UserId = user.UserId,
            RoleId = role.RoleId,
            ClubId = null,
            AssignedAt = now
        };
        _db.UserRoles.Add(assignment);
        return assignment;
    }

    private async Task SaveIdentityRoleAsync(
        User user,
        IReadOnlyList<Role> roles,
        UserRole assignment)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _db.Entry(assignment).State = EntityState.Detached;

            var roleCode = GetDefaultIdentityRoleCode(user.StudentNo);
            var role = roles.Single(candidate => candidate.RoleCode == roleCode);
            var concurrentAssignmentExists = await _db.UserRoles
                .AsNoTracking()
                .AnyAsync(ur =>
                    ur.UserId == user.UserId &&
                    ur.RoleId == role.RoleId &&
                    ur.ClubId == null);
            if (!concurrentAssignmentExists)
            {
                throw;
            }
        }
    }

    private static string BuildPermissionDesc(RoleDefinition roleDef)
    {
        var names = roleDef.Permissions
            .Select(code => PermissionCatalog.FirstOrDefault(p => p.Code == code)?.Name ?? code);
        return $"{roleDef.Description} 基础权限：{string.Join("、", names)}。";
    }

    private static bool IsNormalAccount(User user)
    {
        var normalized = NormalizeText(user.AccountStatus).ToLowerInvariant();
        return normalized is "" or "active" or NormalStatus or "enabled" or "在任" or "正常";
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("ORA-00001", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidStudentOrStaffNo(string value) => IsStudentNo(value) || IsStaffNo(value);

    private static bool IsStudentNo(string value) => value.Length == StudentNoLength && value.All(char.IsDigit);

    private static bool IsStaffNo(string value) => value.Length == StaffNoLength && value.All(char.IsDigit);

    private static string StudentOrStaffNoRuleMessage() =>
        $"学工号必须为学生 {StudentNoLength} 位或教师 {StaffNoLength} 位。";

    private static string? GetDefaultIdentityRoleCode(string? studentNo)
    {
        var normalized = NormalizeText(studentNo);
        if (IsStudentNo(normalized)) return StudentRole;
        if (IsStaffNo(normalized)) return TeacherRole;
        return null;
    }

    private static string NormalizeText(string? value) => (value ?? string.Empty).Trim();

    private static string? NullIfBlank(string? value)
    {
        var normalized = NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

}
