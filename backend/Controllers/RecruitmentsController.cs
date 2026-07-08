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
    private const string RecruitmentPendingReview = "pending_review";
    private const string RecruitmentPublished = "published";
    private const string RecruitmentClosed = "closed";
    private const string RecruitmentNotStarted = "not_started";
    private const string RecruitmentAccepting = "accepting";
    private const string RecruitmentEnded = "ended";
    private const string ApplicationPending = "pending";
    private const string ApplicationAccepted = "accepted";
    private const string ApplicationRejected = "rejected";
    private const string ReviewApproved = "approved";
    private const string ReviewRejected = "rejected";
    private const string ClubApproved = "approved";
    private const string ClubActive = "active";
    private const string MemberActive = "active";
    private const string ClubMemberRoleCode = "CLUB_MEMBER";
    private const int MaxStudentClubMemberships = 3;

    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();
    private static readonly HashSet<string> RecruitmentManagerRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "club_officer",
        "club_leader"
    };
    private static readonly HashSet<string> ClubMembershipRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ClubMemberRoleCode,
        "CLUB_OFFICER",
        "CLUB_LEADER",
        "club_president"
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

        var normalizedStatus = NormalizeRecruitmentStatusFilter(status);
        if (!string.IsNullOrWhiteSpace(status) && normalizedStatus is null)
        {
            return BadRequest(new { message = "招募状态只能是 draft、pending_review、not_started、accepting 或 ended。" });
        }

        var query = RecruitmentQuery();
        if (clubId is not null)
        {
            query = query.Where(r => r.ClubId == clubId.Value);
        }

        var now = BusinessNow();
        var recruitments = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.RecruitId)
            .ToListAsync();

        return Ok(recruitments
            .Where(r => CanViewRecruitment(viewer, r))
            .Where(r => normalizedStatus is null || EffectiveRecruitmentStatus(r, now) == normalizedStatus)
            .Select(r => ToRecruitmentDto(r, viewer, now)));
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

        var requestedStatus = NormalizeRecruitmentWorkflowStatus(req.RecruitStatus);
        if (!string.IsNullOrWhiteSpace(req.RecruitStatus) && requestedStatus is null)
        {
            return BadRequest(new { message = "纳新只能保存草稿或提交审核。" });
        }
        var recruitStatus = requestedStatus ?? RecruitmentDraft;

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
            RecruitStatus = recruitStatus,
            CreatedAt = now,
            Club = club
        };

        _db.Recruitments.Add(recruitment);
        await _db.SaveChangesAsync();

        var created = await RecruitmentQuery().FirstAsync(r => r.RecruitId == recruitment.RecruitId);
        return CreatedAtAction(nameof(GetAll), new { viewerUserId = req.CurrentUserId }, ToRecruitmentDto(created, operatorUser, BusinessNow()));
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

        if (!CanEditRecruitment(operatorUser, recruitment))
        {
            return StatusCode(403, new { message = "只有本社团干部或负责人可以维护草稿纳新。" });
        }

        var status = NormalizeRecruitmentWorkflowStatus(req.RecruitStatus);
        if (!string.IsNullOrWhiteSpace(req.RecruitStatus) && status is null)
        {
            return BadRequest(new { message = "纳新状态只能保存为草稿或提交审核。" });
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
        return Ok(ToRecruitmentDto(updated, operatorUser, BusinessNow()));
    }

    [HttpDelete("{recruitId:int}")]
    public async Task<IActionResult> Delete(int recruitId, [FromQuery] int currentUserId)
    {
        if (currentUserId <= 0) return BadRequest(new { message = "请选择当前操作用户。" });

        var operatorUser = await LoadUserAsync(currentUserId);
        if (operatorUser is null) return NotFound(new { message = "当前用户不存在。" });

        var recruitment = await RecruitmentQuery().FirstOrDefaultAsync(r => r.RecruitId == recruitId);
        if (recruitment is null) return NotFound(new { message = "招募不存在。" });

        if (NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) != RecruitmentDraft)
        {
            return Conflict(new { message = "只有草稿纳新可以删除。" });
        }

        if (!CanDeleteDraftRecruitment(operatorUser, recruitment))
        {
            return StatusCode(403, new { message = "只有本社团干部或负责人可以删除草稿纳新。" });
        }

        if (recruitment.Applications.Count > 0)
        {
            return Conflict(new { message = "已有报名记录的纳新不能删除。" });
        }

        _db.Recruitments.Remove(recruitment);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{recruitId:int}/review")]
    public async Task<IActionResult> ReviewRecruitment(int recruitId, [FromBody] ReviewRecruitmentRequest req)
    {
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请选择当前审核用户。" });

        var decision = NormalizeRecruitmentReviewDecision(req.Decision);
        if (decision is not ReviewApproved and not ReviewRejected)
        {
            return BadRequest(new { message = "审核结果只能是 approved 或 rejected。" });
        }

        var reviewer = await LoadUserAsync(req.CurrentUserId);
        if (reviewer is null) return NotFound(new { message = "当前用户不存在。" });

        var recruitment = await RecruitmentQuery().FirstOrDefaultAsync(r => r.RecruitId == recruitId);
        if (recruitment is null) return NotFound(new { message = "招募不存在。" });

        if (!CanReviewRecruitment(reviewer, recruitment))
        {
            return StatusCode(403, new { message = "只有非本社团提出人的社团管理员可以审核纳新。" });
        }

        if (recruitment.Club is null || !IsMaintainableClub(recruitment.Club))
        {
            return Conflict(new { message = "社团状态不允许审核纳新。" });
        }

        if (NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) != RecruitmentPendingReview)
        {
            return Conflict(new { message = "只有审核中的纳新可以处理审核结果。" });
        }

        var validationError = ValidateRecruitmentState(
            recruitment.Title,
            recruitment.StartAt,
            recruitment.EndAt,
            recruitment.Quota,
            recruitment.Requirements);
        if (validationError is not null) return BadRequest(new { message = validationError });

        if (decision == ReviewApproved &&
            await HasOverlappingPublishedRecruitmentAsync(
                recruitment.ClubId,
                recruitment.StartAt!.Value,
                recruitment.EndAt!.Value,
                recruitment.RecruitId))
        {
            return Conflict(new { message = "同一社团同一时间最多只能发布一个已通过招募，请先结束或调整已有招募时间。" });
        }

        recruitment.RecruitStatus = decision == ReviewApproved ? RecruitmentPublished : RecruitmentDraft;
        await _db.SaveChangesAsync();

        var reviewed = await RecruitmentQuery().FirstAsync(r => r.RecruitId == recruitId);
        return Ok(ToRecruitmentDto(reviewed, reviewer, BusinessNow()));
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
        var now = BusinessNow();
        if (EffectiveRecruitmentStatus(recruitment, now) != RecruitmentAccepting)
        {
            return Conflict(new { message = "只有申请中的纳新可以提交报名。" });
        }

        var hasSubmitted = await _db.RecruitmentApplications.AnyAsync(a =>
            a.RecruitId == recruitId &&
            a.UserId == applicant.UserId);
        if (hasSubmitted) return Conflict(new { message = "你已经提交过该招募报名，请勿重复提交。" });

        if (await IsCurrentClubMemberAsync(recruitment.ClubId, applicant.UserId))
        {
            return Conflict(new { message = "你已经是该社团成员，无需再次报名招募。" });
        }

        var currentClubCount = await CountCurrentMembershipClubsAsync(applicant.UserId);
        if (currentClubCount >= MaxStudentClubMemberships)
        {
            return Conflict(new { message = "一个学生最多只能同时加入 3 个社团，当前已达到上限。" });
        }

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
            if (await IsCurrentClubMemberAsync(application.Recruitment.ClubId, application.UserId))
            {
                return Conflict(new { message = "该学生已经是社团成员，不能重复录取。" });
            }

            var currentClubCount = await CountCurrentMembershipClubsAsync(application.UserId);
            if (currentClubCount >= MaxStudentClubMemberships)
            {
                return Conflict(new { message = "一个学生最多只能同时加入 3 个社团，该学生已达到上限。" });
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

    private async Task<bool> HasOverlappingPublishedRecruitmentAsync(
        int clubId,
        DateTime startAt,
        DateTime endAt,
        int? ignoredRecruitId = null) =>
        await _db.Recruitments.AnyAsync(r =>
            r.ClubId == clubId &&
            (ignoredRecruitId == null || r.RecruitId != ignoredRecruitId.Value) &&
            r.RecruitStatus == RecruitmentPublished &&
            r.StartAt.HasValue &&
            r.EndAt.HasValue &&
            r.StartAt.Value < endAt &&
            r.EndAt.Value > startAt);

    private async Task<bool> IsCurrentClubMemberAsync(int clubId, int userId)
    {
        var today = BusinessToday();
        var currentMemberRecordExists = await _db.ClubMembers.AnyAsync(m =>
            m.ClubId == clubId &&
            m.UserId == userId &&
            (m.MemberStatus == null || m.MemberStatus == MemberActive) &&
            (m.TermStart == null || m.TermStart <= today) &&
            (m.TermEnd == null || m.TermEnd >= today));
        if (currentMemberRecordExists) return true;

        var roleAssignments = await _db.UserRoles
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId && ur.ClubId == clubId)
            .ToListAsync();
        return roleAssignments.Any(ur => IsClubMembershipRole(ur.Role));
    }

    private async Task<int> CountCurrentMembershipClubsAsync(int userId, int? excludingClubId = null)
    {
        var today = BusinessToday();
        var memberClubIds = await _db.ClubMembers
            .Where(m =>
                m.UserId == userId &&
                (excludingClubId == null || m.ClubId != excludingClubId.Value) &&
                (m.MemberStatus == null || m.MemberStatus == MemberActive) &&
                (m.TermStart == null || m.TermStart <= today) &&
                (m.TermEnd == null || m.TermEnd >= today))
            .Select(m => m.ClubId)
            .ToListAsync();

        var roleAssignments = await _db.UserRoles
            .Include(ur => ur.Role)
            .Where(ur =>
                ur.UserId == userId &&
                ur.ClubId != null &&
                (excludingClubId == null || ur.ClubId != excludingClubId.Value))
            .ToListAsync();

        return memberClubIds
            .Concat(roleAssignments
                .Where(ur => IsClubMembershipRole(ur.Role))
                .Select(ur => ur.ClubId!.Value))
            .Distinct()
            .Count();
    }

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

    private static RecruitmentDto ToRecruitmentDto(Recruitment recruitment, User viewer, DateTime now)
    {
        var currentApplication = recruitment.Applications
            .Where(a => a.UserId == viewer.UserId)
            .OrderByDescending(a => a.SubmittedAt)
            .ThenByDescending(a => a.ApplicationId)
            .FirstOrDefault();

        var status = EffectiveRecruitmentStatus(recruitment, now);
        var applicationStatus = NormalizeApplicationStatus(currentApplication?.ApplicationStatus);
        var currentUserIsMember = CurrentUserIsMemberOfClub(viewer, recruitment.ClubId);
        var isOwnProposal = IsOwnRecruitmentProposal(viewer, recruitment);

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
            currentUserIsMember,
            isOwnProposal,
            CanManageRecruitment(viewer, recruitment.ClubId),
            CanEditRecruitment(viewer, recruitment),
            CanDeleteDraftRecruitment(viewer, recruitment),
            CanReviewRecruitment(viewer, recruitment));
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

    private static bool CanViewRecruitment(User viewer, Recruitment recruitment)
    {
        var status = NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) ?? RecruitmentDraft;
        if (status == RecruitmentDraft) return CanManageRecruitment(viewer, recruitment.ClubId);
        if (status == RecruitmentPendingReview)
        {
            return CanManageRecruitment(viewer, recruitment.ClubId) || CanReviewRecruitment(viewer, recruitment);
        }

        return true;
    }

    private static bool CanManageRecruitment(User user, int clubId) =>
        UsersController.IsSystemAdmin(user) || HasClubRecruitmentManagerRole(user, clubId);

    private static bool HasClubRecruitmentManagerRole(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            IsRecruitmentManagerRole(ur.Role));

    private static bool IsOwnRecruitmentProposal(User user, Recruitment recruitment) =>
        HasClubRecruitmentManagerRole(user, recruitment.ClubId);

    private static bool CanEditRecruitment(User user, Recruitment recruitment) =>
        NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) == RecruitmentDraft &&
        CanManageRecruitment(user, recruitment.ClubId);

    private static bool CanDeleteDraftRecruitment(User user, Recruitment recruitment) =>
        CanEditRecruitment(user, recruitment);

    private static bool CanReviewRecruitment(User user, Recruitment recruitment) =>
        (UsersController.IsPlatformAdmin(user) || UsersController.IsSystemAdmin(user)) &&
        !HasClubRecruitmentManagerRole(user, recruitment.ClubId);

    private static bool IsRecruitmentManagerRole(Role? role) =>
        role is not null && RecruitmentManagerRoleCodes.Contains(Normalize(role.RoleCode));

    private static bool IsClubMembershipRole(Role? role) =>
        role is not null && ClubMembershipRoleCodes.Contains(Normalize(role.RoleCode));

    private static bool CurrentUserIsMemberOfClub(User viewer, int clubId) =>
        viewer.ClubMemberships.Any(m => m.ClubId == clubId && IsCurrentMemberRecord(m)) ||
        viewer.UserRoles.Any(ur => ur.ClubId == clubId && IsClubMembershipRole(ur.Role));

    private static bool IsCurrentMemberRecord(ClubMember member)
    {
        var today = BusinessToday();
        return (member.MemberStatus == null || member.MemberStatus == MemberActive) &&
               (member.TermStart == null || member.TermStart.Value.Date <= today) &&
               (member.TermEnd == null || member.TermEnd.Value.Date >= today);
    }

    private static bool IsMaintainableClub(Club club) =>
        club.AuditStatus == ClubApproved && club.ClubStatus == ClubActive;

    private static string EffectiveRecruitmentStatus(Recruitment recruitment, DateTime now)
    {
        var storageStatus = NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) ?? RecruitmentDraft;
        if (storageStatus == RecruitmentDraft) return RecruitmentDraft;
        if (storageStatus == RecruitmentPendingReview) return RecruitmentPendingReview;
        if (storageStatus == RecruitmentClosed) return RecruitmentEnded;

        if (recruitment.EndAt is not null && recruitment.EndAt.Value < now) return RecruitmentEnded;
        if (recruitment.StartAt is not null && recruitment.StartAt.Value > now) return RecruitmentNotStarted;
        return RecruitmentAccepting;
    }

    private static string? NormalizeRecruitmentStorageStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        return Normalize(status) switch
        {
            RecruitmentDraft or "草稿" => RecruitmentDraft,
            RecruitmentPendingReview or "pending" or "reviewing" or "审核中" or "待审核" => RecruitmentPendingReview,
            RecruitmentPublished or "open" or "approved" or "报名中" or "申请中" or "发布" or "已通过" => RecruitmentPublished,
            RecruitmentClosed or RecruitmentEnded or "finished" or "结束" or "已结束" => RecruitmentClosed,
            _ => null
        };
    }

    private static string? NormalizeRecruitmentWorkflowStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        return Normalize(status) switch
        {
            RecruitmentDraft or "草稿" => RecruitmentDraft,
            RecruitmentPendingReview or "pending" or "reviewing" or "审核中" or "待审核" => RecruitmentPendingReview,
            _ => null
        };
    }

    private static string? NormalizeRecruitmentStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        return Normalize(status) switch
        {
            RecruitmentDraft or "草稿" => RecruitmentDraft,
            RecruitmentPendingReview or "pending" or "reviewing" or "审核中" or "待审核" => RecruitmentPendingReview,
            RecruitmentNotStarted or "notstarted" or "未开始" => RecruitmentNotStarted,
            RecruitmentAccepting or RecruitmentPublished or "open" or "申请中" or "报名中" => RecruitmentAccepting,
            RecruitmentEnded or RecruitmentClosed or "finished" or "结束" or "已结束" => RecruitmentEnded,
            _ => null
        };
    }

    private static string? NormalizeRecruitmentReviewDecision(string? decision)
    {
        if (string.IsNullOrWhiteSpace(decision)) return null;

        return Normalize(decision) switch
        {
            ReviewApproved or "accepted" or "approve" or "通过" or "审核通过" => ReviewApproved,
            ReviewRejected or "reject" or "驳回" or "审核驳回" => ReviewRejected,
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
        RecruitmentPendingReview => "审核中",
        RecruitmentNotStarted => "未开始",
        RecruitmentAccepting => "申请中",
        RecruitmentEnded => "已结束",
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

    private static DateTime BusinessToday() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone).Date;

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
    bool CurrentUserIsMember,
    bool IsOwnProposal,
    bool CanManage,
    bool CanEdit,
    bool CanDelete,
    bool CanReview);

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
    public string? RecruitStatus { get; set; } = "draft";
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

public class ReviewRecruitmentRequest
{
    public int CurrentUserId { get; set; }
    public string? Decision { get; set; }
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
