using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecruitmentsController : ControllerBase
{
    private const string RecruitmentDraft = "draft";
    private const string RecruitmentPublished = "published";
    private const string RecruitmentClosed = "closed";
    private const string ApplicationPending = "pending";
    private const string ApplicationAccepted = "accepted";
    private const string ApplicationRejected = "rejected";
    private const string ClubApproved = "approved";
    private const string ClubActive = "active";
    private const string MemberActive = "active";
    private const string ClubMemberRoleCode = "CLUB_MEMBER";

    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();
    private static readonly HashSet<string> RecruitmentManagerRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "club_officer",
        "club_leader"
    };

    private readonly ClubHubDbContext _db;

    public RecruitmentsController(ClubHubDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int viewerUserId,
        [FromQuery] int? clubId,
        [FromQuery] string? status)
    {
        if (viewerUserId <= 0) return BadRequest(new { message = "请选择当前用户。" });

        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null) return NotFound(new { message = "当前用户不存在。" });

        var normalizedStatus = NormalizeRecruitmentStatus(status);
        if (!string.IsNullOrWhiteSpace(status) && normalizedStatus is null)
        {
            return BadRequest(new { message = "招募状态只能是 draft、published 或 closed。" });
        }

        var query = RecruitmentQuery();
        if (clubId is not null)
        {
            query = query.Where(r => r.ClubId == clubId.Value);
        }

        if (normalizedStatus is not null)
        {
            query = query.Where(r => r.RecruitStatus == normalizedStatus);
        }

        if (!UsersController.IsSystemAdmin(viewer))
        {
            var manageableClubIds = ManagedClubIds(viewer);
            query = query.Where(r =>
                r.RecruitStatus == RecruitmentPublished ||
                r.Applications.Any(a => a.UserId == viewer.UserId) ||
                manageableClubIds.Contains(r.ClubId));
        }

        var recruitments = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.RecruitId)
            .ToListAsync();

        return Ok(recruitments.Select(r => ToRecruitmentDto(r, viewer)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecruitmentRequest req)
    {
        var validationError = ValidateCreateRecruitmentRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var operatorUser = await LoadUserAsync(req.CurrentUserId);
        if (operatorUser is null) return NotFound(new { message = "当前用户不存在。" });

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == req.ClubId);
        if (club is null) return NotFound(new { message = "社团不存在。" });

        if (!IsMaintainableClub(club))
        {
            return Conflict(new { message = "只有运营中的已通过社团可以发布招募。" });
        }

        if (!CanManageRecruitment(operatorUser, club.ClubId))
        {
            return StatusCode(403, new { message = "只有系统管理员或本社团干部可以发布招募。" });
        }

        var now = DateTime.UtcNow;
        var nextId = (await _db.Recruitments.MaxAsync(r => (int?)r.RecruitId) ?? 0) + 1;
        var recruitment = new Recruitment
        {
            RecruitId = nextId,
            ClubId = club.ClubId,
            Title = req.Title.Trim(),
            Description = EmptyToNull(req.Description),
            StartAt = req.StartAt,
            EndAt = req.EndAt,
            Quota = req.Quota,
            Requirements = req.Requirements.Trim(),
            RecruitStatus = NormalizeRecruitmentStatus(req.RecruitStatus) ?? RecruitmentPublished,
            CreatedAt = now,
            Club = club
        };

        _db.Recruitments.Add(recruitment);
        await _db.SaveChangesAsync();

        var created = await RecruitmentQuery().FirstAsync(r => r.RecruitId == recruitment.RecruitId);
        return CreatedAtAction(nameof(GetAll), new { viewerUserId = req.CurrentUserId }, ToRecruitmentDto(created, operatorUser));
    }

    [HttpPatch("{recruitId:int}")]
    public async Task<IActionResult> Update(int recruitId, [FromBody] UpdateRecruitmentRequest req)
    {
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请选择当前操作用户。" });

        var operatorUser = await LoadUserAsync(req.CurrentUserId);
        if (operatorUser is null) return NotFound(new { message = "当前用户不存在。" });

        var recruitment = await RecruitmentQuery().FirstOrDefaultAsync(r => r.RecruitId == recruitId);
        if (recruitment is null) return NotFound(new { message = "招募不存在。" });

        if (recruitment.Club is null || !IsMaintainableClub(recruitment.Club))
        {
            return Conflict(new { message = "社团状态不允许维护招募。" });
        }

        if (!CanManageRecruitment(operatorUser, recruitment.ClubId))
        {
            return StatusCode(403, new { message = "只有系统管理员或本社团干部可以维护招募。" });
        }

        var status = NormalizeRecruitmentStatus(req.RecruitStatus);
        if (!string.IsNullOrWhiteSpace(req.RecruitStatus) && status is null)
        {
            return BadRequest(new { message = "招募状态只能是 draft、published 或 closed。" });
        }

        if (req.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { message = "招募标题不能为空。" });
            recruitment.Title = req.Title.Trim();
        }

        if (req.Description is not null) recruitment.Description = EmptyToNull(req.Description);
        if (req.StartAt is not null) recruitment.StartAt = req.StartAt.Value;
        if (req.EndAt is not null) recruitment.EndAt = req.EndAt.Value;
        if (req.Quota is not null) recruitment.Quota = req.Quota.Value;
        if (req.Requirements is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Requirements)) return BadRequest(new { message = "招募要求不能为空。" });
            recruitment.Requirements = req.Requirements.Trim();
        }
        if (status is not null) recruitment.RecruitStatus = status;

        var validationError = ValidateRecruitmentState(recruitment.Title, recruitment.StartAt, recruitment.EndAt, recruitment.Quota, recruitment.Requirements);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var acceptedCount = recruitment.Applications.Count(a => a.ApplicationStatus == ApplicationAccepted);
        if (recruitment.Quota is not null && recruitment.Quota.Value < acceptedCount)
        {
            return Conflict(new { message = "招募名额不能小于已录取人数。" });
        }

        await _db.SaveChangesAsync();

        var updated = await RecruitmentQuery().FirstAsync(r => r.RecruitId == recruitId);
        return Ok(ToRecruitmentDto(updated, operatorUser));
    }

    [HttpGet("{recruitId:int}/applications")]
    public async Task<IActionResult> GetApplications(int recruitId, [FromQuery] int viewerUserId)
    {
        if (viewerUserId <= 0) return BadRequest(new { message = "请选择当前用户。" });

        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null) return NotFound(new { message = "当前用户不存在。" });

        var recruitment = await RecruitmentQuery().FirstOrDefaultAsync(r => r.RecruitId == recruitId);
        if (recruitment is null) return NotFound(new { message = "招募不存在。" });

        var canManage = CanManageRecruitment(viewer, recruitment.ClubId);
        var query = ApplicationQuery().Where(a => a.RecruitId == recruitId);
        if (!canManage)
        {
            query = query.Where(a => a.UserId == viewer.UserId);
        }

        var applications = await query
            .OrderByDescending(a => a.SubmittedAt)
            .ThenByDescending(a => a.ApplicationId)
            .ToListAsync();

        if (!canManage && applications.Count == 0)
        {
            return StatusCode(403, new { message = "当前用户没有查看该招募报名的权限。" });
        }

        return Ok(applications.Select(ToApplicationDto));
    }

    [HttpPost("{recruitId:int}/applications")]
    public async Task<IActionResult> CreateApplication(int recruitId, [FromBody] CreateRecruitmentApplicationRequest req)
    {
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请选择当前报名用户。" });
        if (string.IsNullOrWhiteSpace(req.ApplicationReason)) return BadRequest(new { message = "报名理由不能为空。" });

        var applicant = await LoadUserAsync(req.CurrentUserId);
        if (applicant is null) return NotFound(new { message = "当前用户不存在。" });
        if (!UsersController.IsActive(applicant.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能提交招募报名。" });
        }
        if (!UsersController.IsStudent(applicant) || UsersController.IsPlatformAdmin(applicant))
        {
            return StatusCode(403, new { message = "只有普通学生可以提交招募报名。" });
        }

        var recruitment = await RecruitmentQuery().FirstOrDefaultAsync(r => r.RecruitId == recruitId);
        if (recruitment is null) return NotFound(new { message = "招募不存在。" });
        if (recruitment.Club is null || !IsMaintainableClub(recruitment.Club))
        {
            return Conflict(new { message = "社团状态不允许接收报名。" });
        }
        if (recruitment.RecruitStatus != RecruitmentPublished)
        {
            return Conflict(new { message = "只有报名中的招募可以提交报名。" });
        }

        var now = BusinessNow();
        if (recruitment.StartAt is not null && recruitment.StartAt.Value > now)
        {
            return Conflict(new { message = "招募尚未开始，暂不能报名。" });
        }
        if (recruitment.EndAt is not null && recruitment.EndAt.Value < now)
        {
            return Conflict(new { message = "招募已结束，不能继续报名。" });
        }

        var hasSubmitted = await _db.RecruitmentApplications.AnyAsync(a =>
            a.RecruitId == recruitId &&
            a.UserId == applicant.UserId);
        if (hasSubmitted) return Conflict(new { message = "你已经提交过该招募报名，请勿重复提交。" });

        var isActiveMember = await _db.ClubMembers.AnyAsync(m =>
            m.ClubId == recruitment.ClubId &&
            m.UserId == applicant.UserId &&
            (m.MemberStatus == null || m.MemberStatus == MemberActive));
        if (isActiveMember) return Conflict(new { message = "你已经是该社团成员，无需再次报名招募。" });

        if (recruitment.Quota is not null && recruitment.Applications.Count(a => a.ApplicationStatus == ApplicationAccepted) >= recruitment.Quota.Value)
        {
            return Conflict(new { message = "招募名额已满，暂时不能继续报名。" });
        }

        var nextId = (await _db.RecruitmentApplications.MaxAsync(a => (int?)a.ApplicationId) ?? 0) + 1;
        var application = new RecruitmentApplication
        {
            ApplicationId = nextId,
            RecruitId = recruitment.RecruitId,
            UserId = applicant.UserId,
            ApplicationReason = req.ApplicationReason.Trim(),
            ApplicationStatus = ApplicationPending,
            SubmittedAt = DateTime.UtcNow,
            Recruitment = recruitment,
            User = applicant
        };

        _db.RecruitmentApplications.Add(application);
        await _db.SaveChangesAsync();

        var created = await ApplicationQuery().FirstAsync(a => a.ApplicationId == application.ApplicationId);
        return CreatedAtAction(nameof(GetApplications), new { recruitId, viewerUserId = req.CurrentUserId }, ToApplicationDto(created));
    }

    [HttpPatch("applications/{applicationId:int}/review")]
    public async Task<IActionResult> ReviewApplication(int applicationId, [FromBody] ReviewRecruitmentApplicationRequest req)
    {
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请选择当前筛选用户。" });

        var decision = NormalizeApplicationStatus(req.Decision);
        if (decision is not ApplicationAccepted and not ApplicationRejected)
        {
            return BadRequest(new { message = "筛选结果只能是 accepted 或 rejected。" });
        }

        if (req.InterviewScore is not null && (req.InterviewScore < 0 || req.InterviewScore > 100))
        {
            return BadRequest(new { message = "面试分数必须在 0 到 100 之间。" });
        }

        var reviewer = await LoadUserAsync(req.CurrentUserId);
        if (reviewer is null) return NotFound(new { message = "当前用户不存在。" });

        var application = await ApplicationQuery().FirstOrDefaultAsync(a => a.ApplicationId == applicationId);
        if (application is null) return NotFound(new { message = "招募报名不存在。" });
        if (application.Recruitment is null) return NotFound(new { message = "招募不存在。" });

        if (!CanManageRecruitment(reviewer, application.Recruitment.ClubId))
        {
            return StatusCode(403, new { message = "只有系统管理员或本社团干部可以筛选招募报名。" });
        }

        if (application.ApplicationStatus != ApplicationPending)
        {
            return Conflict(new { message = "只有待筛选的报名可以录入筛选结果。" });
        }

        if (decision == ApplicationAccepted)
        {
            var activeMemberExists = await _db.ClubMembers.AnyAsync(m =>
                m.ClubId == application.Recruitment.ClubId &&
                m.UserId == application.UserId &&
                (m.MemberStatus == null || m.MemberStatus == MemberActive));
            if (activeMemberExists)
            {
                return Conflict(new { message = "该学生已经是社团成员，不能重复录取。" });
            }

            var acceptedCount = await _db.RecruitmentApplications.CountAsync(a =>
                a.RecruitId == application.RecruitId &&
                a.ApplicationStatus == ApplicationAccepted);
            if (application.Recruitment.Quota is not null && acceptedCount >= application.Recruitment.Quota.Value)
            {
                return Conflict(new { message = "招募名额已满，不能继续录取。" });
            }

            await AddAcceptedMemberAsync(application, DateTime.UtcNow);
        }

        application.ApplicationStatus = decision;
        application.InterviewScore = req.InterviewScore;
        application.ReviewerUserId = reviewer.UserId;
        application.Reviewer = reviewer;
        application.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var reviewed = await ApplicationQuery().FirstAsync(a => a.ApplicationId == application.ApplicationId);
        return Ok(ToApplicationDto(reviewed));
    }

    private IQueryable<Recruitment> RecruitmentQuery() =>
        _db.Recruitments
            .Include(r => r.Club)
            .Include(r => r.Applications);

    private IQueryable<RecruitmentApplication> ApplicationQuery() =>
        _db.RecruitmentApplications
            .Include(a => a.Recruitment)
                .ThenInclude(r => r!.Club)
            .Include(a => a.User)
            .Include(a => a.Reviewer);

    private async Task<User?> LoadUserAsync(int userId) =>
        await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId);

    private async Task AddAcceptedMemberAsync(RecruitmentApplication application, DateTime now)
    {
        if (application.Recruitment is null) return;

        var nextMemberId = (await _db.ClubMembers.MaxAsync(m => (int?)m.MemberId) ?? 0) + 1;
        _db.ClubMembers.Add(new ClubMember
        {
            MemberId = nextMemberId,
            ClubId = application.Recruitment.ClubId,
            UserId = application.UserId,
            PositionName = "社员",
            TermName = $"{BusinessDate(now).Year} 招新录取",
            TermStart = BusinessDate(now),
            MemberStatus = MemberActive,
            JoinAt = now,
            ContributionScore = 0
        });

        await EnsureClubMemberRoleAsync(application.Recruitment.ClubId, application.UserId, now);
    }

    private async Task EnsureClubMemberRoleAsync(int clubId, int userId, DateTime now)
    {
        var role = await EnsureClubRoleAsync(
            ClubMemberRoleCode,
            "社团成员",
            "指定社团内角色，可查看社团内部信息、资源、通知和参与讨论、签到。",
            now);

        var hasRole = await _db.UserRoles.AnyAsync(ur =>
            ur.UserId == userId &&
            ur.ClubId == clubId &&
            ur.Role != null &&
            ur.Role.RoleCode.ToUpper() == ClubMemberRoleCode);
        if (hasRole) return;

        _db.UserRoles.Add(new UserRole
        {
            UserRoleId = await NextUserRoleIdAsync(),
            UserId = userId,
            RoleId = role.RoleId,
            ClubId = clubId,
            AssignedAt = now
        });
    }

    private async Task<Role> EnsureClubRoleAsync(string roleCode, string roleName, string permissionDesc, DateTime now)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleCode.ToUpper() == roleCode);
        if (role is not null) return role;

        role = new Role
        {
            RoleId = await NextRoleIdAsync(),
            RoleCode = roleCode,
            RoleName = roleName,
            RoleScope = "club",
            PermissionDesc = permissionDesc,
            CreatedAt = now
        };
        _db.Roles.Add(role);
        return role;
    }

    private async Task<int> NextRoleIdAsync()
    {
        var maxSaved = await _db.Roles.MaxAsync(r => (int?)r.RoleId) ?? 0;
        var maxAdded = _db.ChangeTracker.Entries<Role>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.RoleId)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(maxSaved, maxAdded) + 1;
    }

    private async Task<int> NextUserRoleIdAsync()
    {
        var maxSaved = await _db.UserRoles.MaxAsync(ur => (int?)ur.UserRoleId) ?? 0;
        var maxAdded = _db.ChangeTracker.Entries<UserRole>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.UserRoleId)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(maxSaved, maxAdded) + 1;
    }

    private static RecruitmentDto ToRecruitmentDto(Recruitment recruitment, User viewer)
    {
        var currentApplication = recruitment.Applications
            .Where(a => a.UserId == viewer.UserId)
            .OrderByDescending(a => a.SubmittedAt)
            .ThenByDescending(a => a.ApplicationId)
            .FirstOrDefault();

        var status = NormalizeRecruitmentStatus(recruitment.RecruitStatus) ?? RecruitmentDraft;
        var applicationStatus = NormalizeApplicationStatus(currentApplication?.ApplicationStatus);

        return new RecruitmentDto(
            recruitment.RecruitId,
            recruitment.ClubId,
            recruitment.Club?.ClubName ?? $"社团 {recruitment.ClubId}",
            recruitment.Title,
            recruitment.Description,
            recruitment.StartAt,
            recruitment.EndAt,
            recruitment.Quota,
            recruitment.Requirements,
            status,
            RecruitmentStatusText(status),
            recruitment.CreatedAt,
            recruitment.Applications.Count,
            recruitment.Applications.Count(a => a.ApplicationStatus == ApplicationAccepted),
            currentApplication?.ApplicationId,
            applicationStatus,
            applicationStatus is null ? null : ApplicationStatusText(applicationStatus),
            CanManageRecruitment(viewer, recruitment.ClubId));
    }

    private static RecruitmentApplicationDto ToApplicationDto(RecruitmentApplication application)
    {
        var status = NormalizeApplicationStatus(application.ApplicationStatus) ?? ApplicationPending;

        return new RecruitmentApplicationDto(
            application.ApplicationId,
            application.RecruitId,
            application.Recruitment?.Title ?? $"招募 {application.RecruitId}",
            application.Recruitment?.ClubId ?? 0,
            application.Recruitment?.Club?.ClubName ?? "未知社团",
            application.UserId,
            DisplayUser(application.User) ?? $"用户 {application.UserId}",
            application.User?.StudentNo,
            application.ApplicationReason ?? string.Empty,
            application.InterviewScore,
            status,
            ApplicationStatusText(status),
            application.ReviewerUserId,
            DisplayUser(application.Reviewer),
            application.SubmittedAt,
            application.ReviewedAt);
    }

    private static string? ValidateCreateRecruitmentRequest(CreateRecruitmentRequest req)
    {
        if (req.CurrentUserId <= 0) return "请选择当前操作用户。";
        if (req.ClubId <= 0) return "请选择发布招募的社团。";
        return ValidateRecruitmentState(req.Title, req.StartAt, req.EndAt, req.Quota, req.Requirements);
    }

    private static string? ValidateRecruitmentState(
        string? title,
        DateTime? startAt,
        DateTime? endAt,
        int? quota,
        string? requirements)
    {
        if (string.IsNullOrWhiteSpace(title)) return "招募标题不能为空。";
        if (startAt is null || startAt.Value == default) return "招募开始时间不能为空。";
        if (endAt is null || endAt.Value == default) return "招募结束时间不能为空。";
        if (endAt.Value <= startAt.Value) return "招募结束时间必须晚于开始时间。";
        if (quota is null or <= 0) return "招募人数必须大于 0。";
        if (string.IsNullOrWhiteSpace(requirements)) return "招募要求不能为空。";
        return null;
    }

    private static bool CanManageRecruitment(User user, int clubId) =>
        UsersController.IsSystemAdmin(user) ||
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            IsRecruitmentManagerRole(ur.Role));

    private static List<int> ManagedClubIds(User user)
    {
        if (UsersController.IsSystemAdmin(user)) return [];

        return user.UserRoles
            .Where(ur => ur.ClubId is not null && IsRecruitmentManagerRole(ur.Role))
            .Select(ur => ur.ClubId!.Value)
            .Distinct()
            .ToList();
    }

    private static bool IsRecruitmentManagerRole(Role? role) =>
        role is not null && RecruitmentManagerRoleCodes.Contains(Normalize(role.RoleCode));

    private static bool IsMaintainableClub(Club club) =>
        club.AuditStatus == ClubApproved && club.ClubStatus == ClubActive;

    private static string? NormalizeRecruitmentStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        return Normalize(status) switch
        {
            RecruitmentDraft or "草稿" => RecruitmentDraft,
            RecruitmentPublished or "open" or "报名中" or "发布" => RecruitmentPublished,
            RecruitmentClosed or "finished" or "结束" or "已结束" => RecruitmentClosed,
            _ => null
        };
    }

    private static string? NormalizeApplicationStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        return Normalize(status) switch
        {
            ApplicationPending or "待筛选" or "待审核" => ApplicationPending,
            ApplicationAccepted or "approved" or "录取" or "已录取" => ApplicationAccepted,
            ApplicationRejected or "拒绝" or "未录取" => ApplicationRejected,
            _ => null
        };
    }

    private static string RecruitmentStatusText(string status) => status switch
    {
        RecruitmentDraft => "草稿",
        RecruitmentPublished => "报名中",
        RecruitmentClosed => "已结束",
        _ => "未知"
    };

    private static string ApplicationStatusText(string status) => status switch
    {
        ApplicationPending => "待筛选",
        ApplicationAccepted => "已录取",
        ApplicationRejected => "未录取",
        _ => "未知"
    };

    private static DateTime BusinessNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone);

    private static DateTime BusinessDate(DateTime utcDateTime)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, BusinessTimeZone).Date;
    }

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }

    private static string? DisplayUser(User? user)
    {
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(user.RealName)) return user.RealName;
        if (!string.IsNullOrWhiteSpace(user.Username)) return user.Username;
        return $"用户 {user.UserId}";
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record RecruitmentDto(
    int Id,
    int ClubId,
    string ClubName,
    string Title,
    string? Description,
    DateTime? StartAt,
    DateTime? EndAt,
    int? Quota,
    string? Requirements,
    string RecruitStatus,
    string RecruitStatusText,
    DateTime CreatedAt,
    int ApplicationCount,
    int AcceptedCount,
    int? CurrentUserApplicationId,
    string? CurrentUserApplicationStatus,
    string? CurrentUserApplicationStatusText,
    bool CanManage);

public record RecruitmentApplicationDto(
    int Id,
    int RecruitId,
    string RecruitTitle,
    int ClubId,
    string ClubName,
    int UserId,
    string ApplicantName,
    string? StudentNo,
    string ApplicationReason,
    decimal? InterviewScore,
    string ApplicationStatus,
    string ApplicationStatusText,
    int? ReviewerUserId,
    string? ReviewerName,
    DateTime? SubmittedAt,
    DateTime? ReviewedAt);

public class CreateRecruitmentRequest
{
    public int CurrentUserId { get; set; }
    public int ClubId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public int? Quota { get; set; }
    public string Requirements { get; set; } = string.Empty;
    public string? RecruitStatus { get; set; } = "published";
}

public class UpdateRecruitmentRequest
{
    public int CurrentUserId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public int? Quota { get; set; }
    public string? Requirements { get; set; }
    public string? RecruitStatus { get; set; }
}

public class CreateRecruitmentApplicationRequest
{
    public int CurrentUserId { get; set; }
    public string ApplicationReason { get; set; } = string.Empty;
}

public class ReviewRecruitmentApplicationRequest
{
    public int CurrentUserId { get; set; }
    public string? Decision { get; set; }
    public decimal? InterviewScore { get; set; }
}
