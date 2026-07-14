using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExitClubMemberRequest = Org.OpenAPITools.Models.ExitClubMemberRequest;
using ApiLearningTeacherCandidate = Org.OpenAPITools.Models.LearningTeacherCandidate;
using UpdateClubMemberGroupingRequest = Org.OpenAPITools.Models.UpdateClubMemberGroupingRequest;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClubsController : ControllerBase
{
    private const string AuditPending = "pending";
    private const string AuditApproved = "approved";
    private const string AuditRejected = "rejected";
    private const string ClubPending = "pending";
    private const string ClubActive = "active";
    private const string ClubRejected = "rejected";
    private const string ClubInactive = "inactive";
    private const string MemberActive = "active";
    private const string MemberEnded = "ended";
    private const string MemberSuspended = "suspended";
    private const string ApplicationAccepted = "accepted";
    private const string ApplicationRejected = "rejected";
    private const string ClubMemberRoleCode = "CLUB_MEMBER";
    private const string ClubOfficerRoleCode = "CLUB_OFFICER";
    private const string ClubLeaderRoleCode = "CLUB_LEADER";
    private const string ClubAdvisorRoleCode = "ADVISOR";
    private const int ClubMemberTextMaxLength = 255;
    private const int MaxStudentClubMemberships = RecruitmentWorkflow.MaxStudentClubMemberships;
    private const string EvaluationSemester = "semester";
    private const string EvaluationAward = "award";
    private const string EvaluationDraft = "draft";
    private const string EvaluationPublished = "published";
    private const decimal MaxEvaluationScore = 100m;
    private const decimal ActivityCheckedOutScore = 10m;
    private const decimal ActivityCheckedInScore = 8m;
    private const decimal ActivityAcceptedScore = 5m;

    private readonly ClubHubDbContext _db;
    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();
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
    private static readonly HashSet<string> CadrePositionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "\u5e72\u90e8",
        "\u90e8\u957f",
        "\u526f\u90e8\u957f",
        "\u7ec4\u957f",
        "\u526f\u7ec4\u957f",
        "\u5e72\u4e8b",
        "\u793e\u56e2\u5e72\u90e8",
        "\u90e8\u95e8\u8d1f\u8d23\u4eba",
        "\u5c0f\u7ec4\u8d1f\u8d23\u4eba",
        "officer",
        "cadre",
        "minister",
        "group leader"
    };
    private static readonly HashSet<string> DepartmentManagerPositionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "\u90e8\u957f",
        "\u526f\u90e8\u957f",
        "\u90e8\u95e8\u8d1f\u8d23\u4eba",
        "minister"
    };

    public ClubsController(ClubHubDbContext db) => _db = db;

    [HttpGet("advisor-candidates")]
    public async Task<IActionResult> GetAdvisorCandidates()
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var viewer = await LoadUserAsync(currentUserId.Value);
        if (viewer is null)
        {
            return NotFound(new { message = "当前用户不存在。" });
        }

        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能选择指导老师。" });
        }

        var candidates = await _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u =>
                u.AccountStatus == null ||
                u.AccountStatus == string.Empty ||
                u.AccountStatus.ToLower() == "active" ||
                u.AccountStatus.ToLower() == "normal" ||
                u.AccountStatus.ToLower() == "enabled")
            .Where(u =>
                (u.StudentNo != null && u.StudentNo.Length == 5) ||
                u.UserRoles.Any(ur =>
                    ur.Role != null &&
                    ((ur.Role.RoleCode != null &&
                      (ur.Role.RoleCode.ToUpper() == "TEACHER" ||
                       ur.Role.RoleCode.ToUpper() == ClubAdvisorRoleCode)) ||
                     (ur.Role.RoleName != null &&
                      (ur.Role.RoleName.Contains("教师") ||
                       ur.Role.RoleName.Contains("老师"))))))
            .OrderBy(u => u.RealName)
            .ThenBy(u => u.StudentNo)
            .ThenBy(u => u.UserId)
            .ToListAsync();

        return Ok(candidates
            .Where(IsTeacherCandidate)
            .Select(user => new ApiLearningTeacherCandidate
            {
                Id = user.UserId,
                RealName = user.RealName,
                StudentNo = user.StudentNo,
                DisplayName = AdvisorCandidateDisplayName(user)
            }));
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var query = ClubQuery();
        var userId = User.GetUserId();
        if (userId is not null)
        {
            var viewer = await LoadUserAsync(userId.Value);
            if (viewer is not null && !UsersController.IsPlatformAdmin(viewer))
            {
                query = query.Where(c =>
                    c.ApplicantUserId == viewer.UserId ||
                    c.PresidentUserId == viewer.UserId ||
                    c.Members.Any(m => m.UserId == viewer.UserId) ||
                    c.UserRoles.Any(ur => ur.UserId == viewer.UserId));
            }
        }

        var clubs = await query
            .OrderBy(c => c.ClubId)
            .ToListAsync();

        return Ok(clubs.Select(ToClubDto));
    }

    [HttpGet("applications")]
    public async Task<IActionResult> GetApplications(
        [FromQuery] string? auditStatus,
        [FromQuery] string? keyword,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var viewer = await LoadUserAsync(currentUserId.Value);
        if (viewer is null) return NotFound(new { message = "当前用户不存在。" });

        var query = ClubQuery()
            .Where(c => c.ApplicantUserId != null && c.AuditStatus != null);

        if (!UsersController.IsPlatformAdmin(viewer))
        {
            query = query.Where(c => c.ApplicantUserId == viewer.UserId);
        }

        if (!string.IsNullOrWhiteSpace(auditStatus))
        {
            var normalizedStatus = auditStatus.Trim().ToLowerInvariant();
            if (!IsKnownAuditStatus(normalizedStatus))
            {
                return BadRequest(new { message = "审核状态只能是 pending、approved 或 rejected。" });
            }

            query = query.Where(c => c.AuditStatus == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = keyword.Trim().ToUpperInvariant();
            query = query.Where(c =>
                c.ClubName.ToUpper().Contains(normalizedKeyword) ||
                (c.Applicant != null &&
                 ((c.Applicant.RealName ?? string.Empty).ToUpper().Contains(normalizedKeyword) ||
                  (c.Applicant.Username ?? string.Empty).ToUpper().Contains(normalizedKeyword) ||
                  (c.Applicant.StudentNo ?? string.Empty).ToUpper().Contains(normalizedKeyword))));
        }

        if (startDate is not null && endDate is not null && startDate.Value.Date > endDate.Value.Date)
        {
            return BadRequest(new { message = "提交开始日期不能晚于结束日期。" });
        }

        if (startDate is not null)
        {
            var start = BusinessDateBoundaryUtc(startDate.Value.Date);
            query = query.Where(c => c.CreatedAt >= start);
        }

        if (endDate is not null)
        {
            var endExclusive = BusinessDateBoundaryUtc(endDate.Value.Date.AddDays(1));
            query = query.Where(c => c.CreatedAt < endExclusive);
        }

        var applications = await query
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ThenByDescending(c => c.ClubId)
            .ToListAsync();

        return Ok(applications.Select(ToApplicationDto));
    }

    [HttpPost("applications")]
    public async Task<IActionResult> CreateApplication([FromBody] CreateClubApplicationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var validationError = ValidateApplicationRequest(req);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var applicant = await LoadUserAsync(currentUserId.Value);
        if (applicant is null)
        {
            return NotFound(new { message = "当前用户不存在，请先选择有效的学生用户。" });
        }

        if (!UsersController.IsActive(applicant.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能提交社团注册申请。" });
        }

        if (UsersController.IsPlatformAdmin(applicant))
        {
            return StatusCode(403, new { message = "平台管理员负责审核申请，不能以审核身份提交社团注册申请。" });
        }

        if (!UsersController.IsStudent(applicant))
        {
            return StatusCode(403, new { message = "只有学生用户可以提交社团注册申请。" });
        }

        var name = req.Name.Trim();
        var category = req.Category.Trim();
        var normalizedName = name.ToUpperInvariant();
        var hasConflict = await _db.Clubs.AnyAsync(c =>
            c.ClubName.ToUpper() == normalizedName &&
            (c.AuditStatus == null || c.AuditStatus != AuditRejected) &&
            (c.ClubStatus == null || c.ClubStatus != ClubRejected));
        if (hasConflict)
        {
            return Conflict(new { message = "该社团名称已存在，或已有待审核/已通过的注册申请。" });
        }

        var now = DateTime.UtcNow;
        var club = new Club
        {
            ClubName = name,
            Category = category,
            Description = EmptyToNull(req.Description),
            ApplicantUserId = applicant.UserId,
            AdvisorName = null,
            ContactPhone = EmptyToNull(req.ContactPhone),
            ApplyReason = req.ApplyReason.Trim(),
            MaterialUrl = req.MaterialUrl.Trim(),
            AuditStatus = AuditPending,
            ClubStatus = ClubPending,
            CreatedAt = now,
            UpdatedAt = now,
            Applicant = applicant
        };

        var advisor = await ValidateAdvisorAsync(req.AdvisorUserId);
        if (advisor.Result is not null) return advisor.Result;
        if (advisor.User is not null)
        {
            club.AdvisorName = DisplayUser(advisor.User);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            _db.Clubs.Add(club);
            await _db.SaveChangesAsync();
            if (advisor.User is not null)
            {
                await EnsureSingleClubAdvisorRoleAsync(club.ClubId, advisor.User.UserId, now);
                await _db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return CreatedAtAction(nameof(GetById), new { clubId = club.ClubId }, ToApplicationDto(club));
    }

    [HttpPatch("applications/{clubId:int}")]
    public async Task<IActionResult> ResubmitApplication(int clubId, [FromBody] CreateClubApplicationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var validationError = ValidateApplicationRequest(req);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var applicant = await LoadUserAsync(currentUserId.Value);
        if (applicant is null)
        {
            return NotFound(new { message = "当前用户不存在，请先选择有效的学生用户。" });
        }

        if (!UsersController.IsActive(applicant.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能重新提交社团注册申请。" });
        }

        if (UsersController.IsPlatformAdmin(applicant) || !UsersController.IsStudent(applicant))
        {
            return StatusCode(403, new { message = "只有原学生申请人可以修改后重新提交社团注册申请。" });
        }

        var club = await _db.Clubs
            .Include(c => c.Applicant)
            .Include(c => c.Reviewer)
            .FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null || club.ApplicantUserId is null || club.AuditStatus is null)
        {
            return NotFound(new { message = "社团注册申请不存在。" });
        }

        if (club.ApplicantUserId != applicant.UserId)
        {
            return StatusCode(403, new { message = "只能修改并重新提交自己的社团注册申请。" });
        }

        if (club.AuditStatus != AuditRejected)
        {
            return Conflict(new { message = "只有已退回的社团注册申请可以修改后重新提交。" });
        }

        var name = req.Name.Trim();
        var category = req.Category.Trim();
        var normalizedName = name.ToUpperInvariant();
        var hasConflict = await _db.Clubs.AnyAsync(c =>
            c.ClubId != clubId &&
            c.ClubName.ToUpper() == normalizedName &&
            (c.AuditStatus == null || c.AuditStatus != AuditRejected) &&
            (c.ClubStatus == null || c.ClubStatus != ClubRejected));
        if (hasConflict)
        {
            return Conflict(new { message = "该社团名称已存在，或已有待审核/已通过的注册申请。" });
        }

        var advisor = await ValidateAdvisorAsync(req.AdvisorUserId);
        if (advisor.Result is not null) return advisor.Result;

        var now = DateTime.UtcNow;
        club.ClubName = name;
        club.Category = category;
        club.Description = EmptyToNull(req.Description);
        club.AdvisorName = advisor.User is null ? null : DisplayUser(advisor.User);
        club.ContactPhone = EmptyToNull(req.ContactPhone);
        club.ApplyReason = req.ApplyReason.Trim();
        club.MaterialUrl = req.MaterialUrl.Trim();
        club.AuditStatus = AuditPending;
        club.ClubStatus = ClubPending;
        club.PresidentUserId = null;
        club.FoundedAt = null;
        club.UpdatedAt = now;

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            if (advisor.User is not null)
            {
                await EnsureSingleClubAdvisorRoleAsync(club.ClubId, advisor.User.UserId, now);
            }
            else
            {
                await RemoveClubAdvisorRolesExceptAsync(club.ClubId, null);
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        var updated = await ClubQuery().FirstAsync(c => c.ClubId == club.ClubId);
        return Ok(ToApplicationDto(updated));
    }

    [HttpPatch("applications/{clubId:int}/review")]
    public async Task<IActionResult> ReviewApplication(int clubId, [FromBody] ReviewClubApplicationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var decision = req.Decision?.Trim().ToLowerInvariant();

        if (decision is not AuditApproved and not AuditRejected)
        {
            return BadRequest(new { message = "审核结果只能是通过或退回。" });
        }

        if (decision == AuditRejected && string.IsNullOrWhiteSpace(req.ReviewComment))
        {
            return BadRequest(new { message = "退回申请时必须填写审核意见。" });
        }

        var reviewer = await LoadUserAsync(currentUserId.Value);
        if (reviewer is null)
        {
            return NotFound(new { message = "当前用户不存在，请确认审核用户是否正确。" });
        }

        if (!UsersController.IsPlatformAdmin(reviewer))
        {
            return StatusCode(403, new { message = "只有平台管理员可以审核社团注册申请。" });
        }

        var club = await _db.Clubs
            .Include(c => c.Applicant)
            .Include(c => c.Reviewer)
            .FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return NotFound(new { message = "社团注册申请不存在。" });
        }

        if (club.AuditStatus != AuditPending)
        {
            return Conflict(new { message = "只有待审核的社团注册申请可以审核。" });
        }

        var now = DateTime.UtcNow;
        club.AuditStatus = decision;
        club.ReviewerUserId = reviewer.UserId;
        club.Reviewer = reviewer;
        club.ReviewComment = EmptyToNull(req.ReviewComment);
        club.UpdatedAt = now;

        if (decision == AuditApproved)
        {
            club.ClubStatus = ClubActive;
            club.PresidentUserId = club.ApplicantUserId;
            club.FoundedAt = now;
            var membershipError = await EnsurePresidentMembershipAsync(club, now);
            if (membershipError is not null) return membershipError;
        }
        else
        {
            club.ClubStatus = ClubRejected;
            club.PresidentUserId = null;
            club.FoundedAt = null;
            await RemoveClubAdvisorRolesExceptAsync(club.ClubId, null);
        }

        await _db.SaveChangesAsync();

        var reviewed = await ClubQuery().FirstAsync(c => c.ClubId == club.ClubId);
        return Ok(ToApplicationDto(reviewed));
    }

    [HttpGet("{clubId:int}")]
    public async Task<IActionResult> GetById(int clubId)
    {
        var club = await ClubQuery().FirstOrDefaultAsync(c => c.ClubId == clubId);
        return club is null ? NotFound() : Ok(ToClubDto(club));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClubRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return BadRequest(new { message = "社团名称不能为空。" });
        }

        if (string.IsNullOrWhiteSpace(req.Category))
        {
            return BadRequest(new { message = "社团类别不能为空。" });
        }

        var creator = await LoadUserAsync(currentUserId.Value);
        if (creator is null)
        {
            return NotFound(new { message = "当前操作用户不存在。" });
        }

        if (!UsersController.IsActive(creator.AccountStatus))
        {
            return BadRequest(new { message = "当前操作用户账号不可用，不能直接创建社团。" });
        }

        if (!UsersController.IsPlatformAdmin(creator))
        {
            return StatusCode(403, new { message = "只有平台管理员可以直接创建社团；学生请提交社团注册申请。" });
        }

        var normalizedName = req.Name.Trim().ToUpperInvariant();
        var hasConflict = await _db.Clubs.AnyAsync(c =>
            c.ClubName.ToUpper() == normalizedName &&
            (c.AuditStatus == null || c.AuditStatus != AuditRejected) &&
            (c.ClubStatus == null || c.ClubStatus != ClubRejected));
        if (hasConflict)
        {
            return Conflict(new { message = "社团名称已存在，或已有待审核/已通过的注册申请。" });
        }

        var now = DateTime.UtcNow;
        var club = new Club
        {
            ClubName = req.Name.Trim(),
            Category = req.Category.Trim(),
            Description = EmptyToNull(req.Description),
            AuditStatus = AuditApproved,
            ClubStatus = ClubActive,
            FoundedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Clubs.Add(club);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { clubId = club.ClubId }, ToClubDto(club));
    }

    [HttpPut("{clubId:int}")]
    public async Task<IActionResult> Update(int clubId, [FromBody] UpdateClubRequest req)
    {
        var club = await _db.Clubs.FindAsync(clubId);
        if (club is null) return NotFound();

        if (req.Name is not null) club.ClubName = req.Name.Trim();
        if (req.Category is not null) club.Category = req.Category.Trim();
        if (req.Description is not null) club.Description = EmptyToNull(req.Description);
        club.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(ToClubDto(club));
    }

    [HttpPatch("{clubId:int}/profile")]
    public async Task<IActionResult> UpdateProfile(int clubId, [FromBody] UpdateClubProfileRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanMaintainClubAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var club = access.Club!;
        if (!IsMaintainableClub(club))
        {
            return Conflict(new { message = "只有已通过审核且正在运营的社团可以维护基础信息。" });
        }

        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return BadRequest(new { message = "社团名称不能为空。" });
        }

        if (string.IsNullOrWhiteSpace(req.Category))
        {
            return BadRequest(new { message = "社团类别不能为空。" });
        }

        var name = req.Name.Trim();
        var category = req.Category.Trim();
        var normalizedName = name.ToUpperInvariant();
        var hasConflict = await _db.Clubs.AnyAsync(c =>
            c.ClubId != clubId &&
            c.ClubName.ToUpper() == normalizedName &&
            (c.ClubStatus == null || c.ClubStatus != ClubRejected));
        if (hasConflict)
        {
            return Conflict(new { message = "社团名称已被其他社团或有效申请占用。" });
        }

        if (req.PresidentUserId is not null)
        {
            var president = await _db.Users.FindAsync(req.PresidentUserId.Value);
            if (president is null)
            {
                return NotFound(new { message = "指定的社团负责人不存在。" });
            }

            var today = BusinessToday();
            var isActiveMember = await _db.ClubMembers.AnyAsync(cm =>
                cm.ClubId == clubId &&
                cm.UserId == req.PresidentUserId.Value &&
                (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
                (cm.TermStart == null || cm.TermStart <= today) &&
                (cm.TermEnd == null || cm.TermEnd >= today));
            if (!isActiveMember)
            {
                return BadRequest(new { message = "社团负责人必须是本社团当前有效成员，请先为该用户新增任期记录。" });
            }
        }

        var advisor = await ValidateAdvisorAsync(req.AdvisorUserId);
        if (advisor.Result is not null) return advisor.Result;

        var now = DateTime.UtcNow;
        club.ClubName = name;
        club.Category = category;
        club.Description = EmptyToNull(req.Description);
        club.LogoUrl = EmptyToNull(req.LogoUrl);
        club.PresidentUserId = req.PresidentUserId;
        club.AdvisorName = advisor.User is null ? null : DisplayUser(advisor.User);
        club.ContactPhone = EmptyToNull(req.ContactPhone);
        club.UpdatedAt = now;

        if (club.PresidentUserId is not null)
        {
            await EnsureSingleClubPresidentRoleAsync(club.ClubId, club.PresidentUserId.Value, now);
        }
        else
        {
            await RemoveClubPresidentRolesExceptAsync(club.ClubId, null);
        }

        if (advisor.User is not null)
        {
            await EnsureSingleClubAdvisorRoleAsync(club.ClubId, advisor.User.UserId, now);
        }
        else
        {
            await RemoveClubAdvisorRolesExceptAsync(club.ClubId, null);
        }

        await _db.SaveChangesAsync();

        var updated = await ClubQuery().FirstAsync(c => c.ClubId == clubId);
        return Ok(ToClubDto(updated));
    }

    [HttpGet("{clubId:int}/members")]
    public async Task<IActionResult> GetMembers(
        int clubId,
        [FromQuery] bool includeHistory = false,
        [FromQuery] string? termName = null,
        [FromQuery] string? departmentName = null,
        [FromQuery] string? groupName = null)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewMembersAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var today = BusinessToday();
        var query = _db.ClubMembers
            .AsNoTracking()
            .Include(cm => cm.Club)
            .Include(cm => cm.User)
            .Where(cm => cm.ClubId == clubId);

        if (!includeHistory)
        {
            query = query.Where(cm =>
                (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
                (cm.TermStart == null || cm.TermStart <= today) &&
                (cm.TermEnd == null || cm.TermEnd >= today));
        }

        var departmentFilter = EmptyToNull(departmentName);
        if (departmentFilter is not null)
        {
            query = query.Where(cm => cm.DepartmentName == departmentFilter);
        }

        var groupFilter = EmptyToNull(groupName);
        if (groupFilter is not null)
        {
            query = query.Where(cm => cm.GroupName == groupFilter);
        }

        var termFilter = EmptyToNull(termName);
        if (termFilter is not null)
        {
            query = query.Where(cm => cm.TermName == termFilter);
        }

        var members = await query
            .OrderBy(cm => cm.DepartmentName)
            .ThenBy(cm => cm.GroupName)
            .ThenBy(cm => cm.PositionName)
            .ThenByDescending(cm => cm.TermStart)
            .ThenBy(cm => cm.UserId)
            .ToListAsync();

        return Ok(members.Select(ToMemberRecordDto));
    }

    [HttpPatch("{clubId:int}/members/{memberId:int}/grouping")]
    public async Task<IActionResult> UpdateMemberGrouping(
        int clubId,
        int memberId,
        [FromBody] UpdateClubMemberGroupingRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanUpdateMemberGroupingAsync(clubId, memberId, currentUserId.Value, req);
        if (access.Result is not null) return access.Result;

        var member = access.Member!;
        member.DepartmentName = EmptyToNull(req.DepartmentName);
        member.GroupName = EmptyToNull(req.GroupName);

        await _db.SaveChangesAsync();

        var updated = await MemberQuery().FirstAsync(cm => cm.MemberId == memberId);
        return Ok(ToMemberRecordDto(updated));
    }

    [HttpPost("{clubId:int}/members/terms")]
    public async Task<IActionResult> CreateMemberTerm(int clubId, [FromBody] CreateClubMemberTermRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanMaintainClubAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var club = access.Club!;
        if (!IsMaintainableClub(club))
        {
            return Conflict(new { message = "只有已通过审核且正在运营的社团可以维护成员任期。" });
        }

        var validationError = ValidateMemberTermRequest(
            req.UserId,
            req.DepartmentName,
            req.GroupName,
            req.PositionName,
            req.TermName,
            req.TermStart,
            req.TermEnd,
            req.MemberStatus,
            req.ContributionScore);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var targetUser = await _db.Users.FindAsync(req.UserId);
        if (targetUser is null)
        {
            return NotFound(new { message = "目标成员用户不存在。" });
        }

        if (!UsersController.IsActive(targetUser.AccountStatus))
        {
            return BadRequest(new { message = "目标成员账号不可用，不能加入社团任期。" });
        }

        var now = DateTime.UtcNow;
        var termStart = req.TermStart.Date;
        var termEnd = req.TermEnd?.Date;
        var memberStatus = NormalizeMemberStatus(req.MemberStatus) ?? MemberActive;

        if (req.CloseCurrentTerm)
        {
            var closeDate = termStart.AddDays(-1);
            var activeTerms = await _db.ClubMembers
                .Where(cm =>
                    cm.ClubId == clubId &&
                    cm.UserId == req.UserId &&
                    (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
                    (cm.TermEnd == null || cm.TermEnd >= termStart))
                .ToListAsync();

            foreach (var activeTerm in activeTerms)
            {
                activeTerm.MemberStatus = MemberEnded;
                activeTerm.TermEnd = activeTerm.TermStart is not null && closeDate < activeTerm.TermStart.Value.Date
                    ? activeTerm.TermStart.Value.Date
                    : closeDate;
            }
        }

        var member = new ClubMember
        {
            ClubId = clubId,
            UserId = req.UserId,
            DepartmentName = EmptyToNull(req.DepartmentName),
            GroupName = EmptyToNull(req.GroupName),
            PositionName = req.PositionName.Trim(),
            TermName = req.TermName.Trim(),
            TermStart = termStart,
            TermEnd = termEnd,
            MemberStatus = memberStatus,
            JoinAt = now,
            ContributionScore = req.ContributionScore ?? 0
        };

        if (IsCurrentMemberTerm(member) &&
            await CountCurrentMembershipClubsAsync(req.UserId, clubId) >= MaxStudentClubMemberships)
        {
            return Conflict(new { message = $"一个学生最多只能同时加入 {MaxStudentClubMemberships} 个社团，当前已达到上限。" });
        }

        _db.ClubMembers.Add(member);

        if (IsCurrentMemberTerm(member))
        {
            await EnsureClubMemberRoleAsync(clubId, req.UserId, now);
        }

        if (IsCurrentMemberTerm(member) && IsStrictPrincipalPosition(member.PositionName))
        {
            club.PresidentUserId = req.UserId;
            club.UpdatedAt = now;
            await EnsureSingleClubPresidentRoleAsync(clubId, req.UserId, now);
        }

        await _db.SaveChangesAsync();

        var created = await MemberQuery().FirstAsync(cm => cm.MemberId == member.MemberId);
        return Created(
            $"/api/clubs/{clubId}/members?includeHistory=true",
            ToMemberRecordDto(created));
    }

    [HttpPatch("{clubId:int}/members/{memberId:int}")]
    public async Task<IActionResult> UpdateMemberTerm(
        int clubId,
        int memberId,
        [FromBody] UpdateClubMemberTermRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanMaintainMemberTermAsync(clubId, memberId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var club = access.Club!;
        if (!IsMaintainableClub(club))
        {
            return Conflict(new { message = "只有已通过审核且正在运营的社团可以维护成员任期。" });
        }

        var member = access.Member!;

        var termStart = req.TermStart?.Date ?? member.TermStart?.Date;
        var termEnd = req.TermEnd?.Date ?? member.TermEnd?.Date;
        var validationError = ValidateMemberTermRequest(
            member.UserId,
            req.DepartmentName ?? member.DepartmentName,
            req.GroupName ?? member.GroupName,
            req.PositionName ?? member.PositionName,
            req.TermName ?? member.TermName,
            termStart,
            termEnd,
            req.MemberStatus ?? member.MemberStatus,
            req.ContributionScore ?? member.ContributionScore);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        if (req.DepartmentName is not null) member.DepartmentName = EmptyToNull(req.DepartmentName);
        if (req.GroupName is not null) member.GroupName = EmptyToNull(req.GroupName);
        if (req.PositionName is not null) member.PositionName = req.PositionName.Trim();
        if (req.TermName is not null) member.TermName = req.TermName.Trim();
        if (req.TermStart is not null) member.TermStart = req.TermStart.Value.Date;
        if (req.TermEnd is not null) member.TermEnd = req.TermEnd.Value.Date;
        if (req.MemberStatus is not null) member.MemberStatus = NormalizeMemberStatus(req.MemberStatus);
        if (req.ContributionScore is not null) member.ContributionScore = req.ContributionScore;

        var now = DateTime.UtcNow;
        if (IsCurrentMemberTerm(member) && IsStrictPrincipalPosition(member.PositionName))
        {
            club.PresidentUserId = member.UserId;
            club.UpdatedAt = now;
            await EnsureSingleClubPresidentRoleAsync(clubId, member.UserId, now);
        }
        else if (club.PresidentUserId == member.UserId)
        {
            await RefreshClubPresidentAsync(club, member.MemberId, now);
        }

        if (IsCurrentMemberTerm(member) || await HasOtherCurrentMemberTermAsync(clubId, member.UserId, member.MemberId))
        {
            await EnsureClubMemberRoleAsync(clubId, member.UserId, now);
        }
        else
        {
            await RemoveClubMembershipRolesAsync(clubId, member.UserId);
        }

        await _db.SaveChangesAsync();

        var updated = await MemberQuery().FirstAsync(cm => cm.MemberId == memberId);
        return Ok(ToMemberRecordDto(updated));
    }

    [HttpPatch("{clubId:int}/members/self/exit")]
    public async Task<IActionResult> ExitCurrentMember(int clubId, [FromBody] ExitClubMemberRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var viewer = await LoadUserAsync(currentUserId.Value);
        if (viewer is null)
        {
            return NotFound(new { message = "当前用户不存在。" });
        }

        return await ExitOrRemoveMemberAsync(clubId, viewer.UserId, viewer, true);
    }

    [HttpPatch("{clubId:int}/members/{memberId:int}/exit")]
    public async Task<IActionResult> RemoveMember(int clubId, int memberId, [FromBody] ExitClubMemberRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var viewer = await LoadUserAsync(currentUserId.Value);
        if (viewer is null)
        {
            return NotFound(new { message = "当前用户不存在。" });
        }

        var member = await _db.ClubMembers.FirstOrDefaultAsync(cm =>
            cm.ClubId == clubId && cm.MemberId == memberId);
        if (member is null)
        {
            return NotFound(new { message = "社团成员任期记录不存在。" });
        }
        if (!IsCurrentMemberTerm(member))
        {
            return Conflict(new { message = "只有当前有效成员身份可以退出或移出。" });
        }

        return await ExitOrRemoveMemberAsync(clubId, member.UserId, viewer, member.UserId == viewer.UserId);
    }

    [HttpGet("{clubId:int}/evaluations")]
    public async Task<IActionResult> GetEvaluations(
        int clubId,
        [FromQuery] string? termName = null,
        [FromQuery] string? evaluationType = null)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewEvaluationsAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var normalizedType = NormalizeEvaluationType(evaluationType);
        if (!string.IsNullOrWhiteSpace(evaluationType) && normalizedType is null)
        {
            return BadRequest(new { message = "评价类型只能是 semester 或 award。" });
        }

        var normalizedTerm = EmptyToNull(termName);
        var evaluations = await EvaluationQuery()
            .Where(ev => ev.ClubId == clubId)
            .Where(ev => normalizedTerm == null || ev.TermName == normalizedTerm)
            .Where(ev => normalizedType == null || ev.EvaluationType == normalizedType)
            .OrderByDescending(ev => ev.CreatedAt)
            .ThenBy(ev => ev.UserId)
            .ToListAsync();

        var visible = evaluations
            .Where(ev => CanViewEvaluationRecord(access.Viewer!, clubId, ev))
            .Select(ToEvaluationRecordDto)
            .ToList();
        return Ok(visible);
    }

    [HttpGet("{clubId:int}/evaluations/score-preview")]
    public async Task<IActionResult> PreviewEvaluationScores(
        int clubId,
        [FromQuery] int userId,
        [FromQuery] string termName)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        if (userId <= 0)
        {
            return BadRequest(new { message = "请选择被考核成员。" });
        }

        if (string.IsNullOrWhiteSpace(termName))
        {
            return BadRequest(new { message = "考核学期不能为空。" });
        }

        var access = await EnsureCanMaintainEvaluationAsync(clubId, currentUserId.Value, userId);
        if (access.Result is not null) return access.Result;

        var normalizedTermName = termName.Trim();
        var termValidationError = ValidateSemesterEvaluationTermName(normalizedTermName);
        if (termValidationError is not null)
        {
            return BadRequest(new { message = termValidationError });
        }

        var scores = await CalculateSemesterEvaluationScoresAsync(clubId, userId, normalizedTermName);
        var totalScore = CalculateEvaluationTotal(
            scores.ActivityScore,
            scores.TaskScore,
            scores.LearningScore,
            scores.AwardScore);

        return Ok(new ClubEvaluationScorePreviewDto(
            scores.ActivityScore,
            scores.TaskScore,
            scores.LearningScore,
            scores.AwardScore,
            totalScore,
            EvaluationGrade(totalScore)));
    }

    [HttpPost("{clubId:int}/evaluations/generate")]
    public async Task<IActionResult> GenerateEvaluations(
        int clubId,
        [FromBody] GenerateClubEvaluationsRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        if (string.IsNullOrWhiteSpace(req.TermName))
        {
            return BadRequest(new { message = "考核学期不能为空。" });
        }

        if (TextTooLong(req.TermName))
        {
            return BadRequest(new { message = "考核学期不能超过 255 个字符。" });
        }

        var viewer = await LoadUserAsync(currentUserId.Value);
        if (viewer is null)
        {
            return NotFound(new { message = "当前用户不存在。" });
        }

        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能生成成员考核。" });
        }

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return NotFound(new { message = "社团不存在。" });
        }

        if (!IsMaintainableClub(club))
        {
            return Conflict(new { message = "只有已通过审核且正在运营的社团可以生成成员考核。" });
        }

        if (!IsClubEvaluationPrincipal(viewer, clubId))
        {
            return StatusCode(403, new { message = "只有系统管理员、本社团负责人或指导老师可以批量生成成员考核。" });
        }

        var termName = req.TermName.Trim();
        var termValidationError = ValidateSemesterEvaluationTermName(termName);
        if (termValidationError is not null)
        {
            return BadRequest(new { message = termValidationError });
        }

        var today = BusinessToday();
        var members = await _db.ClubMembers
            .Include(cm => cm.User)
            .Where(cm => cm.ClubId == clubId)
            .Where(cm => cm.TermStart == null || cm.TermStart <= today)
            .Where(cm => cm.TermEnd == null || cm.TermEnd >= today)
            .OrderByDescending(cm => cm.TermStart)
            .ThenByDescending(cm => cm.JoinAt)
            .ToListAsync();
        members = members
            .Where(IsCurrentMemberTerm)
            .GroupBy(cm => cm.UserId)
            .Select(group => group.First())
            .ToList();

        var existingEvaluations = await _db.Evaluations
            .Where(ev =>
                ev.ClubId == clubId &&
                ev.EvaluationType == EvaluationSemester &&
                ev.TermName == termName)
            .OrderByDescending(ev => ev.CreatedAt)
            .ToListAsync();
        var existingByUser = existingEvaluations
            .GroupBy(ev => ev.UserId)
            .ToDictionary(group => group.Key, group => group.First());

        var now = DateTime.UtcNow;
        var createdCount = 0;
        var refreshedCount = 0;
        var skippedPublishedCount = 0;
        var scoreByUser = await CalculateSemesterEvaluationScoresForUsersAsync(
            clubId,
            members.Select(member => member.UserId).ToList(),
            termName);

        foreach (var member in members)
        {
            var scores = scoreByUser[member.UserId];
            var totalScore = CalculateEvaluationTotal(
                scores.ActivityScore,
                scores.TaskScore,
                scores.LearningScore,
                scores.AwardScore);

            if (existingByUser.TryGetValue(member.UserId, out var existing))
            {
                if (NormalizeEvaluationPublicStatus(existing.PublicStatus) == EvaluationPublished)
                {
                    skippedPublishedCount++;
                    continue;
                }

                if (!req.OverwriteDrafts)
                {
                    continue;
                }

                existing.ActivityScore = scores.ActivityScore;
                existing.TaskScore = scores.TaskScore;
                existing.LearningScore = scores.LearningScore;
                existing.AwardScore = scores.AwardScore;
                existing.TotalScore = totalScore;
                existing.Grade = EvaluationGrade(totalScore);
                existing.PublicStatus = EvaluationDraft;
                existing.EvaluatorUserId = currentUserId.Value;
                existing.CommentText = EmptyToNull(existing.CommentText) ?? "系统自动生成，待负责人或指导老师确认。";
                refreshedCount++;
                continue;
            }

            _db.Evaluations.Add(new Evaluation
            {
                EvaluationType = EvaluationSemester,
                ClubId = clubId,
                UserId = member.UserId,
                EvaluatorUserId = currentUserId.Value,
                TermName = termName,
                ActivityScore = scores.ActivityScore,
                TaskScore = scores.TaskScore,
                LearningScore = scores.LearningScore,
                AwardScore = scores.AwardScore,
                TotalScore = totalScore,
                Grade = EvaluationGrade(totalScore),
                PublicStatus = EvaluationDraft,
                CommentText = "系统自动生成，待负责人或指导老师确认。",
                CreatedAt = now
            });
            createdCount++;
        }

        await _db.SaveChangesAsync();

        var records = await EvaluationQuery()
            .Where(ev =>
                ev.ClubId == clubId &&
                ev.EvaluationType == EvaluationSemester &&
                ev.TermName == termName)
            .OrderBy(ev => ev.User!.RealName ?? ev.User!.Username)
            .ThenBy(ev => ev.UserId)
            .ToListAsync();

        return Ok(new GenerateClubEvaluationsResultDto(
            termName,
            members.Count,
            createdCount,
            refreshedCount,
            skippedPublishedCount,
            records.Select(ToEvaluationRecordDto).ToList()));
    }

    [HttpPost("{clubId:int}/evaluations")]
    public async Task<IActionResult> CreateEvaluation(
        int clubId,
        [FromBody] CreateClubEvaluationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanMaintainEvaluationAsync(clubId, currentUserId.Value, req.UserId);
        if (access.Result is not null) return access.Result;

        var validationError = ValidateEvaluationRequest(
            req.UserId,
            req.EvaluationType,
            req.TermName,
            req.AwardTitle,
            req.AwardLevel,
            req.AwardReason,
            req.ActivityScore,
            req.TaskScore,
            req.LearningScore,
            req.AwardScore,
            req.PublicStatus,
            req.CommentText,
            requireScores: true);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var now = DateTime.UtcNow;
        var normalizedType = NormalizeEvaluationType(req.EvaluationType)!;
        var termName = req.TermName.Trim();
        var scores = await ResolveEvaluationScoresAsync(
            clubId,
            req.UserId,
            termName,
            normalizedType,
            req.ActivityScore,
            req.TaskScore,
            req.LearningScore,
            req.AwardScore);
        var totalScore = CalculateEvaluationTotal(
            scores.ActivityScore,
            scores.TaskScore,
            scores.LearningScore,
            scores.AwardScore);
        var evaluation = new Evaluation
        {
            EvaluationType = normalizedType,
            ClubId = clubId,
            UserId = req.UserId,
            EvaluatorUserId = currentUserId.Value,
            TermName = termName,
            AwardTitle = EmptyToNull(req.AwardTitle),
            AwardLevel = EmptyToNull(req.AwardLevel),
            AwardReason = EmptyToNull(req.AwardReason),
            ActivityScore = scores.ActivityScore,
            TaskScore = scores.TaskScore,
            LearningScore = scores.LearningScore,
            AwardScore = scores.AwardScore,
            TotalScore = totalScore,
            Grade = EvaluationGrade(totalScore),
            PublicStatus = NormalizeEvaluationPublicStatus(req.PublicStatus) ?? EvaluationDraft,
            CommentText = EmptyToNull(req.CommentText),
            CreatedAt = now
        };

        _db.Evaluations.Add(evaluation);
        await _db.SaveChangesAsync();

        var created = await EvaluationQuery().FirstAsync(ev => ev.EvaluationId == evaluation.EvaluationId);
        return Created(
            $"/api/clubs/{clubId}/evaluations",
            ToEvaluationRecordDto(created));
    }

    [HttpPatch("{clubId:int}/evaluations/{evaluationId:int}")]
    public async Task<IActionResult> UpdateEvaluation(
        int clubId,
        int evaluationId,
        [FromBody] UpdateClubEvaluationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var evaluation = await _db.Evaluations.FirstOrDefaultAsync(ev =>
            ev.ClubId == clubId && ev.EvaluationId == evaluationId);
        if (evaluation is null)
        {
            return NotFound(new { message = "评价考核记录不存在。" });
        }

        var access = await EnsureCanMaintainEvaluationAsync(clubId, currentUserId.Value, evaluation.UserId);
        if (access.Result is not null) return access.Result;

        var nextEvaluationType = req.EvaluationType ?? evaluation.EvaluationType;
        var nextTermName = req.TermName ?? evaluation.TermName;
        var existingEvaluationType = NormalizeEvaluationType(evaluation.EvaluationType) ?? EvaluationSemester;
        var normalizedNextEvaluationType = NormalizeEvaluationType(nextEvaluationType);
        var isEvaluationTypeChanged =
            normalizedNextEvaluationType is not null &&
            !string.Equals(existingEvaluationType, normalizedNextEvaluationType, StringComparison.Ordinal);

        var nextAwardTitle = isEvaluationTypeChanged ? req.AwardTitle : req.AwardTitle ?? evaluation.AwardTitle;
        var nextAwardLevel = isEvaluationTypeChanged ? req.AwardLevel : req.AwardLevel ?? evaluation.AwardLevel;
        var nextAwardReason = isEvaluationTypeChanged ? req.AwardReason : req.AwardReason ?? evaluation.AwardReason;
        var nextActivityScore = isEvaluationTypeChanged ? req.ActivityScore : req.ActivityScore ?? evaluation.ActivityScore;
        var nextTaskScore = isEvaluationTypeChanged ? req.TaskScore : req.TaskScore ?? evaluation.TaskScore;
        var nextLearningScore = isEvaluationTypeChanged ? req.LearningScore : req.LearningScore ?? evaluation.LearningScore;
        var nextAwardScore = isEvaluationTypeChanged ? req.AwardScore : req.AwardScore ?? evaluation.AwardScore;
        var nextPublicStatus = req.PublicStatus ?? evaluation.PublicStatus;
        var nextCommentText = req.CommentText ?? evaluation.CommentText;

        var validationError = ValidateEvaluationRequest(
            evaluation.UserId,
            nextEvaluationType,
            nextTermName,
            nextAwardTitle,
            nextAwardLevel,
            nextAwardReason,
            nextActivityScore,
            nextTaskScore,
            nextLearningScore,
            nextAwardScore,
            nextPublicStatus,
            nextCommentText,
            requireScores: true);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var normalizedType = normalizedNextEvaluationType!;
        var termName = nextTermName!.Trim();
        var scores = await ResolveEvaluationScoresAsync(
            clubId,
            evaluation.UserId,
            termName,
            normalizedType,
            nextActivityScore,
            nextTaskScore,
            nextLearningScore,
            nextAwardScore,
            evaluationId);
        var totalScore = CalculateEvaluationTotal(
            scores.ActivityScore,
            scores.TaskScore,
            scores.LearningScore,
            scores.AwardScore);

        evaluation.EvaluationType = normalizedType;
        evaluation.TermName = termName;
        evaluation.AwardTitle = EmptyToNull(nextAwardTitle);
        evaluation.AwardLevel = EmptyToNull(nextAwardLevel);
        evaluation.AwardReason = EmptyToNull(nextAwardReason);
        evaluation.ActivityScore = scores.ActivityScore;
        evaluation.TaskScore = scores.TaskScore;
        evaluation.LearningScore = scores.LearningScore;
        evaluation.AwardScore = scores.AwardScore;
        evaluation.TotalScore = totalScore;
        evaluation.Grade = EvaluationGrade(totalScore);
        evaluation.PublicStatus = NormalizeEvaluationPublicStatus(nextPublicStatus) ?? EvaluationDraft;
        evaluation.CommentText = EmptyToNull(nextCommentText);
        evaluation.EvaluatorUserId = currentUserId.Value;

        await _db.SaveChangesAsync();

        var updated = await EvaluationQuery().FirstAsync(ev => ev.EvaluationId == evaluationId);
        return Ok(ToEvaluationRecordDto(updated));
    }

    [HttpPatch("{clubId:int}/dissolve")]
    public async Task<IActionResult> Dissolve(int clubId, [FromBody] DissolveClubRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var viewer = await LoadUserAsync(currentUserId.Value);
        if (viewer is null)
        {
            return NotFound(new { message = "当前用户不存在。" });
        }

        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能解散社团。" });
        }

        if (!UsersController.IsPlatformAdmin(viewer))
        {
            return StatusCode(403, new { message = "只有社团管理员或系统管理员可以解散社团。" });
        }

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return NotFound(new { message = "社团不存在。" });
        }

        if (club.ClubStatus == ClubInactive)
        {
            return Conflict(new { message = "社团已解散。" });
        }

        if (club.ClubStatus != ClubActive)
        {
            return Conflict(new { message = "只有运营中的社团可以解散。" });
        }

        club.ClubStatus = ClubInactive;
        club.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<(IActionResult? Result, Club? Club, User? Viewer)> EnsureCanMaintainClubAsync(
        int clubId,
        int currentUserId)
    {
        if (currentUserId <= 0)
        {
            return (BadRequest(new { message = "请选择当前操作用户。" }), null, null);
        }

        var viewer = await LoadUserAsync(currentUserId);
        if (viewer is null)
        {
            return (NotFound(new { message = "当前用户不存在。" }), null, null);
        }

        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return (BadRequest(new { message = "当前用户账号不可用，不能维护社团信息。" }), null, viewer);
        }

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return (NotFound(new { message = "社团不存在。" }), null, viewer);
        }

        if (!UsersController.IsSystemAdmin(viewer) &&
            !IsClubPrincipal(viewer, club) &&
            !IsClubAdvisor(viewer, clubId))
        {
            return (StatusCode(403, new { message = "只有系统管理员、本社团负责人或指导老师可以维护该社团。" }), club, viewer);
        }

        return (null, club, viewer);
    }

    private async Task<(IActionResult? Result, Club? Club, User? Viewer, ClubMember? Member)>
        EnsureCanMaintainMemberTermAsync(
            int clubId,
            int memberId,
            int currentUserId)
    {
        if (currentUserId <= 0)
        {
            return (BadRequest(new { message = "请选择当前操作用户。" }), null, null, null);
        }

        var viewer = await LoadUserAsync(currentUserId);
        if (viewer is null)
        {
            return (NotFound(new { message = "当前用户不存在。" }), null, null, null);
        }

        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return (BadRequest(new { message = "当前用户账号不可用，不能维护成员任期。" }), null, viewer, null);
        }

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return (NotFound(new { message = "社团不存在。" }), null, viewer, null);
        }

        var member = await _db.ClubMembers.FirstOrDefaultAsync(cm =>
            cm.ClubId == clubId && cm.MemberId == memberId);
        if (member is null)
        {
            return (NotFound(new { message = "社团成员任期记录不存在。" }), club, viewer, null);
        }

        if (UsersController.IsSystemAdmin(viewer))
        {
            return (null, club, viewer, member);
        }

        if (!IsClubPrincipal(viewer, club) && !IsClubAdvisor(viewer, clubId))
        {
            return (StatusCode(403, new { message = "只有系统管理员、本社团负责人或指导老师可以维护成员任期。" }), club, viewer, member);
        }

        if (member.UserId == viewer.UserId && !IsClubAdvisor(viewer, clubId))
        {
            return (StatusCode(403, new { message = "负责人不能修改自己的任期，请由指导老师或系统管理员处理。" }), club, viewer, member);
        }

        return (null, club, viewer, member);
    }

    private async Task<(IActionResult? Result, Club? Club, User? Viewer)> EnsureCanViewMembersAsync(
        int clubId,
        int viewerUserId)
    {
        if (viewerUserId <= 0)
        {
            return (BadRequest(new { message = "请选择当前查看用户。" }), null, null);
        }

        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null)
        {
            return (NotFound(new { message = "当前用户不存在。" }), null, null);
        }

        var club = await _db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return (NotFound(new { message = "社团不存在。" }), null, viewer);
        }

        var canView =
            UsersController.IsPlatformAdmin(viewer) ||
            IsClubPrincipal(viewer, club) ||
            HasClubParticipantRole(viewer, clubId) ||
            viewer.ClubMemberships.Any(cm =>
                cm.ClubId == clubId &&
                UsersController.IsActive(cm.MemberStatus));
        if (!canView)
        {
            return (StatusCode(403, new { message = "只有社团管理员、系统管理员、本社团负责人、成员、干部或指导老师可以查看成员任期。" }), club, viewer);
        }

        return (null, club, viewer);
    }

    private async Task<IActionResult> ExitOrRemoveMemberAsync(
        int clubId,
        int targetUserId,
        User viewer,
        bool isSelfExit)
    {
        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能变更社团成员身份。" });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return NotFound(new { message = "社团不存在。" });
        }

        if (!IsMaintainableClub(club))
        {
            return Conflict(new { message = "只有已通过审核且正在运营的社团可以办理退出或移出。" });
        }

        var canRemove =
            UsersController.IsSystemAdmin(viewer) ||
            IsClubPrincipal(viewer, club) ||
            HasClubOfficerRole(viewer, clubId);
        if (!isSelfExit && !canRemove)
        {
            return StatusCode(403, new { message = "只有系统管理员、本社团负责人或干部可以移出社团成员。" });
        }

        if (isSelfExit && viewer.UserId != targetUserId)
        {
            return StatusCode(403, new { message = "只能退出自己的社团成员身份。" });
        }

        var today = BusinessToday();
        var activeTerms = await _db.ClubMembers
            .Where(cm =>
                cm.ClubId == clubId &&
                cm.UserId == targetUserId &&
                (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
                (cm.TermStart == null || cm.TermStart <= today) &&
                (cm.TermEnd == null || cm.TermEnd >= today))
            .ToListAsync();
        if (activeTerms.Count == 0)
        {
            return NotFound(new { message = "当前有效社团成员身份不存在或已结束。" });
        }

        if (club.PresidentUserId == targetUserId || activeTerms.Any(term => IsStrictPrincipalPosition(term.PositionName)))
        {
            return StatusCode(403, new { message = "社团负责人不能直接退出或被移出，请先在社团档案中转交负责人。" });
        }

        foreach (var term in activeTerms)
        {
            term.MemberStatus = MemberEnded;
            term.TermEnd = term.TermStart is not null && today < term.TermStart.Value.Date
                ? term.TermStart.Value.Date
                : today;
        }

        await RemoveOngoingAcceptedRecruitmentApplicationsAsync(clubId, targetUserId);
        await RemoveClubMembershipRolesAsync(clubId, targetUserId);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
    }

    private async Task<(IActionResult? Result, ClubMember? Member)> EnsureCanUpdateMemberGroupingAsync(
        int clubId,
        int memberId,
        int currentUserId,
        UpdateClubMemberGroupingRequest req)
    {
        var viewer = await LoadUserAsync(currentUserId);
        if (viewer is null)
        {
            return (NotFound(new { message = "当前用户不存在。" }), null);
        }

        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return (BadRequest(new { message = "当前用户账号不可用，不能维护成员分组。" }), null);
        }

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return (NotFound(new { message = "社团不存在。" }), null);
        }

        if (!IsMaintainableClub(club))
        {
            return (Conflict(new { message = "只有已通过审核且正在运营的社团可以维护成员分组。" }), null);
        }

        var member = await _db.ClubMembers.FirstOrDefaultAsync(cm =>
            cm.ClubId == clubId && cm.MemberId == memberId);
        if (member is null)
        {
            return (NotFound(new { message = "社团成员任期记录不存在。" }), null);
        }

        var validationError = ValidateMemberGroupingRequest(req.DepartmentName, req.GroupName);
        if (validationError is not null)
        {
            return (BadRequest(new { message = validationError }), null);
        }

        if (UsersController.IsSystemAdmin(viewer) ||
            IsClubPrincipal(viewer, club) ||
            IsClubAdvisor(viewer, clubId))
        {
            return (null, member);
        }

        if (!IsCurrentMemberTerm(member))
        {
            return (Conflict(new { message = "干部只能调整当前有效成员的分组。" }), null);
        }

        var targetDepartment = EmptyToNull(req.DepartmentName);
        var targetGroup = EmptyToNull(req.GroupName);
        var scopes = GetCadreGroupingScopes(viewer, clubId).ToList();
        if (scopes.Count == 0)
        {
            return (StatusCode(403, new { message = "只有系统管理员、本社团负责人、指导老师或已登记部门、小组的干部可以维护成员分组。" }), null);
        }

        var canAssignToOwnGroup = scopes.Any(scope => GroupingMatchesScope(
            targetDepartment,
            targetGroup,
            scope.DepartmentName,
            scope.GroupName));
        var canManageCurrentGroup = scopes.Any(scope => GroupingMatchesScope(
            member.DepartmentName,
            member.GroupName,
            scope.DepartmentName,
            scope.GroupName));
        if (!canAssignToOwnGroup || !canManageCurrentGroup)
        {
            return (StatusCode(403, new { message = "干部只能将成员纳入自己所在小组，部长只能维护本部门小组。" }), null);
        }

        return (null, member);
    }

    private async Task<(IActionResult? Result, Club? Club, User? Viewer)> EnsureCanViewEvaluationsAsync(
        int clubId,
        int viewerUserId)
    {
        if (viewerUserId <= 0)
        {
            return (BadRequest(new { message = "请选择当前查看用户。" }), null, null);
        }

        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null)
        {
            return (NotFound(new { message = "当前用户不存在。" }), null, null);
        }

        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return (BadRequest(new { message = "当前用户账号不可用，不能查看评价考核。" }), null, viewer);
        }

        var club = await _db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return (NotFound(new { message = "社团不存在。" }), null, viewer);
        }

        var canView =
            UsersController.IsPlatformAdmin(viewer) ||
            IsClubEvaluationPrincipal(viewer, clubId) ||
            HasClubParticipantRole(viewer, clubId) ||
            GetCadreGroupingScopes(viewer, clubId).Any() ||
            viewer.ClubMemberships.Any(cm => cm.ClubId == clubId && IsCurrentMemberTerm(cm));
        if (!canView)
        {
            return (StatusCode(403, new { message = "只有本社团成员、干部、负责人、指导教师或系统管理员可以查看评价考核。" }), club, viewer);
        }

        return (null, club, viewer);
    }

    private async Task<(IActionResult? Result, Club? Club, User? Viewer, ClubMember? TargetMember)>
        EnsureCanMaintainEvaluationAsync(
            int clubId,
            int currentUserId,
            int targetUserId)
    {
        if (currentUserId <= 0)
        {
            return (BadRequest(new { message = "请选择当前操作用户。" }), null, null, null);
        }

        if (targetUserId <= 0)
        {
            return (BadRequest(new { message = "请选择被评价成员。" }), null, null, null);
        }

        var viewer = await LoadUserAsync(currentUserId);
        if (viewer is null)
        {
            return (NotFound(new { message = "当前用户不存在。" }), null, null, null);
        }

        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return (BadRequest(new { message = "当前用户账号不可用，不能维护评价考核。" }), null, viewer, null);
        }

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null)
        {
            return (NotFound(new { message = "社团不存在。" }), null, viewer, null);
        }

        if (!IsMaintainableClub(club))
        {
            return (Conflict(new { message = "只有已通过审核且正在运营的社团可以维护评价考核。" }), club, viewer, null);
        }

        var targetMember = await LoadCurrentClubMemberAsync(clubId, targetUserId);
        if (targetMember is null)
        {
            return (NotFound(new { message = "被评价用户不是本社团当前有效成员。" }), club, viewer, null);
        }

        if (IsClubEvaluationPrincipal(viewer, clubId))
        {
            return (null, club, viewer, targetMember);
        }

        var scopes = GetCadreGroupingScopes(viewer, clubId).ToList();
        if (scopes.Count == 0)
        {
            return (StatusCode(403, new { message = "只有本社团负责人、指导教师或已登记部门、小组的干部可以维护评价考核。" }), club, viewer, targetMember);
        }

        var canMaintain = scopes.Any(scope => GroupingMatchesScope(
            targetMember.DepartmentName,
            targetMember.GroupName,
            scope.DepartmentName,
            scope.GroupName));
        if (!canMaintain)
        {
            return (StatusCode(403, new { message = "干部只能维护自己管辖部门或小组成员的评价考核。" }), club, viewer, targetMember);
        }

        return (null, club, viewer, targetMember);
    }

    private IQueryable<Club> ClubQuery() =>
        _db.Clubs
            .AsNoTracking()
            .Include(c => c.Applicant)
            .Include(c => c.Reviewer)
            .Include(c => c.President)
            .Include(c => c.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(c => c.UserRoles)
                .ThenInclude(ur => ur.User)
            .Include(c => c.Members);

    private IQueryable<ClubMember> MemberQuery() =>
        _db.ClubMembers
            .AsNoTracking()
            .Include(cm => cm.Club)
            .Include(cm => cm.User);

    private IQueryable<Evaluation> EvaluationQuery() =>
        _db.Evaluations
            .AsNoTracking()
            .Include(ev => ev.Club)
            .Include(ev => ev.User)
                .ThenInclude(u => u!.ClubMemberships)
            .Include(ev => ev.Evaluator);

    private static bool HasClubParticipantRole(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            ClubParticipantRoleCodes.Contains((ur.Role.RoleCode ?? string.Empty).Trim()));

    private static bool HasClubOfficerRole(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            ClubManagementRoleCodes.Contains((ur.Role.RoleCode ?? string.Empty).Trim()));

    private async Task<User?> LoadUserAsync(int userId) =>
        await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId);

    private async Task<bool> HasOtherCurrentMemberTermAsync(int clubId, int userId, int ignoredMemberId)
    {
        var today = BusinessToday();
        return await _db.ClubMembers.AnyAsync(cm =>
            cm.ClubId == clubId &&
            cm.UserId == userId &&
            cm.MemberId != ignoredMemberId &&
            (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
            (cm.TermStart == null || cm.TermStart <= today) &&
            (cm.TermEnd == null || cm.TermEnd >= today));
    }

    private async Task<int> CountCurrentMembershipClubsAsync(int userId, int? excludingClubId = null)
    {
        var today = BusinessToday();
        var memberClubIds = await _db.ClubMembers
            .Where(cm =>
                cm.UserId == userId &&
                (excludingClubId == null || cm.ClubId != excludingClubId.Value) &&
                (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
                (cm.TermStart == null || cm.TermStart <= today) &&
                (cm.TermEnd == null || cm.TermEnd >= today))
            .Select(cm => cm.ClubId)
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
                .Where(ur => ur.Role is not null &&
                             ClubMembershipRoleCodes.Contains((ur.Role.RoleCode ?? string.Empty).Trim()))
                .Select(ur => ur.ClubId!.Value))
            .Distinct()
            .Count();
    }

    private async Task RemoveOngoingAcceptedRecruitmentApplicationsAsync(int clubId, int userId)
    {
        var now = BusinessNow();
        var applications = await _db.RecruitmentApplications
            .Include(a => a.Recruitment)
            .Where(a =>
                a.UserId == userId &&
                a.ApplicationStatus == ApplicationAccepted &&
                a.Recruitment != null &&
                a.Recruitment.ClubId == clubId &&
                a.Recruitment.RecruitStatus == RecruitmentStatuses.Published &&
                (a.Recruitment.StartAt == null || a.Recruitment.StartAt <= now) &&
                (a.Recruitment.EndAt == null || a.Recruitment.EndAt >= now))
            .ToListAsync();

        var reviewedAt = DateTime.UtcNow;
        foreach (var application in applications)
        {
            application.ApplicationStatus = ApplicationRejected;
            application.ReviewedAt = reviewedAt;
        }
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
            UserId = userId,
            RoleId = role.RoleId,
            ClubId = clubId,
            AssignedAt = now
        });
    }

    private async Task RemoveClubMembershipRolesAsync(int clubId, int userId)
    {
        var staleRoles = await _db.UserRoles
            .Include(ur => ur.Role)
            .Where(ur =>
                ur.UserId == userId &&
                ur.ClubId == clubId &&
                ur.Role != null &&
                ClubMembershipRoleCodes.Contains((ur.Role.RoleCode ?? string.Empty).Trim()))
            .ToListAsync();

        _db.UserRoles.RemoveRange(staleRoles);
    }

    private async Task<(IActionResult? Result, User? User)> ValidateAdvisorAsync(int? advisorUserId)
    {
        if (advisorUserId is null) return (null, null);

        var advisor = await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == advisorUserId.Value);
        if (advisor is null)
        {
            return (NotFound(new { message = "指定的指导老师不存在。" }), null);
        }

        if (!UsersController.IsActive(advisor.AccountStatus))
        {
            return (BadRequest(new { message = "指导老师账号不可用，不能关联到社团。" }), null);
        }

        if (!IsTeacherCandidate(advisor))
        {
            return (BadRequest(new { message = "指导老师必须选择教师账号。" }), null);
        }

        return (null, advisor);
    }

    private async Task EnsureSingleClubPresidentRoleAsync(int clubId, int userId, DateTime now)
    {
        var role = await EnsureClubRoleAsync(
            ClubLeaderRoleCode,
            "社团负责人",
            "指定社团内最高业务角色，可维护社团信息、成员、社团内部角色和运营统计。",
            now);
        await RemoveClubPresidentRolesExceptAsync(clubId, userId);

        var hasRole = await _db.UserRoles.AnyAsync(ur =>
            ur.UserId == userId &&
            ur.ClubId == clubId &&
            ur.Role != null &&
            ur.Role.RoleCode.ToUpper() == ClubLeaderRoleCode);
        if (hasRole) return;

        _db.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = role.RoleId,
            ClubId = clubId,
            AssignedAt = now
        });
    }

    private async Task RemoveClubPresidentRolesExceptAsync(int clubId, int? userId)
    {
        var staleRoles = await _db.UserRoles
            .Include(ur => ur.Role)
            .Where(ur =>
                ur.ClubId == clubId &&
                (userId == null || ur.UserId != userId.Value) &&
                ur.Role != null &&
                (ur.Role.RoleCode.ToUpper() == ClubLeaderRoleCode ||
                 ur.Role.RoleCode.ToLower() == "club_president"))
            .ToListAsync();

        _db.UserRoles.RemoveRange(staleRoles);
    }

    private async Task EnsureSingleClubAdvisorRoleAsync(int clubId, int userId, DateTime now)
    {
        var role = await EnsureClubRoleAsync(
            ClubAdvisorRoleCode,
            "指导老师",
            "指定社团指导角色，可查看社团运营、维护并审核学习资源，以及审核活动、项目、经费和评价。",
            now);
        await RemoveClubAdvisorRolesExceptAsync(clubId, userId);

        var hasRole = await _db.UserRoles.AnyAsync(ur =>
            ur.UserId == userId &&
            ur.ClubId == clubId &&
            ur.Role != null &&
            ur.Role.RoleCode.ToUpper() == ClubAdvisorRoleCode);
        if (hasRole) return;

        _db.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = role.RoleId,
            ClubId = clubId,
            AssignedAt = now
        });
    }

    private async Task RemoveClubAdvisorRolesExceptAsync(int clubId, int? userId)
    {
        var staleRoles = await _db.UserRoles
            .Include(ur => ur.Role)
            .Where(ur =>
                ur.ClubId == clubId &&
                (userId == null || ur.UserId != userId.Value) &&
                ur.Role != null &&
                ur.Role.RoleCode.ToUpper() == ClubAdvisorRoleCode)
            .ToListAsync();

        _db.UserRoles.RemoveRange(staleRoles);
    }

    private async Task<Role> EnsureClubRoleAsync(string roleCode, string roleName, string permissionDesc, DateTime now)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleCode.ToUpper() == roleCode);
        if (role is not null)
        {
            if (!string.Equals(role.PermissionDesc, permissionDesc, StringComparison.Ordinal))
            {
                role.PermissionDesc = permissionDesc;
            }

            return role;
        }

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

    private async Task RefreshClubPresidentAsync(Club club, int ignoredMemberId, DateTime now)
    {
        var today = BusinessDate(now);
        var nextPresident = await _db.ClubMembers
            .Where(cm =>
                cm.ClubId == club.ClubId &&
                cm.MemberId != ignoredMemberId &&
                (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
                (cm.TermStart == null || cm.TermStart <= today) &&
                (cm.TermEnd == null || cm.TermEnd >= today))
            .OrderByDescending(cm => cm.TermStart)
            .ThenByDescending(cm => cm.JoinAt)
            .ToListAsync();

        var selected = nextPresident.FirstOrDefault(cm => IsStrictPrincipalPosition(cm.PositionName));
        club.PresidentUserId = selected?.UserId;
        club.UpdatedAt = now;

        if (club.PresidentUserId is null)
        {
            await RemoveClubPresidentRolesExceptAsync(club.ClubId, null);
            return;
        }

        await EnsureSingleClubPresidentRoleAsync(club.ClubId, club.PresidentUserId.Value, now);
    }

    private async Task<IActionResult?> EnsurePresidentMembershipAsync(Club club, DateTime now)
    {
        if (club.ApplicantUserId is null) return null;

        var today = BusinessDate(now);
        var hasMember = await _db.ClubMembers.AnyAsync(cm =>
            cm.UserId == club.ApplicantUserId.Value &&
            cm.ClubId == club.ClubId &&
            (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
            (cm.TermStart == null || cm.TermStart <= today) &&
            (cm.TermEnd == null || cm.TermEnd >= today));
        if (!hasMember &&
            await CountCurrentMembershipClubsAsync(club.ApplicantUserId.Value, club.ClubId) >= MaxStudentClubMemberships)
        {
            return Conflict(new { message = $"一个学生最多只能同时加入 {MaxStudentClubMemberships} 个社团，社团申请人已达到上限。" });
        }

        await EnsureClubMemberRoleAsync(club.ClubId, club.ApplicantUserId.Value, now);
        await EnsureSingleClubPresidentRoleAsync(club.ClubId, club.ApplicantUserId.Value, now);

        if (!hasMember)
        {
            var academicTerm = AcademicTermHelper.FromDate(today);
            _db.ClubMembers.Add(new ClubMember
            {
                ClubId = club.ClubId,
                UserId = club.ApplicantUserId.Value,
                PositionName = "负责人",
                TermName = academicTerm.Label,
                TermStart = academicTerm.Start,
                TermEnd = academicTerm.End,
                MemberStatus = "active",
                JoinAt = now,
                ContributionScore = 0
            });
        }

        return null;
    }

    private async Task<ClubMember?> LoadCurrentClubMemberAsync(int clubId, int userId)
    {
        var today = BusinessToday();
        return await _db.ClubMembers
            .Include(cm => cm.User)
            .Where(cm =>
                cm.ClubId == clubId &&
                cm.UserId == userId &&
                (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
                (cm.TermStart == null || cm.TermStart <= today) &&
                (cm.TermEnd == null || cm.TermEnd >= today))
            .OrderByDescending(cm => cm.TermStart)
            .ThenByDescending(cm => cm.JoinAt)
            .FirstOrDefaultAsync();
    }

    private static ClubDto ToClubDto(Club club)
    {
        var advisor = CurrentAdvisorAssignment(club);

        return new ClubDto(
            club.ClubId,
            club.ClubName,
            club.Description,
            club.Category,
            club.ClubStatus,
            ClubStatusText(club.ClubStatus),
            club.LogoUrl,
            club.PresidentUserId,
            DisplayUser(club.President),
            advisor?.UserId,
            advisor is null ? club.AdvisorName : DisplayUser(advisor.User),
            club.ContactPhone,
            club.AuditStatus,
            AuditStatusText(club.AuditStatus),
            club.ApplicantUserId,
            DisplayUser(club.Applicant),
            club.ApplyReason,
            club.MaterialUrl,
            club.ReviewerUserId,
            DisplayUser(club.Reviewer),
            club.ReviewComment,
            club.FoundedAt,
            club.CreatedAt,
            club.UpdatedAt);
    }

    private static ClubApplicationDto ToApplicationDto(Club club)
    {
        var advisor = CurrentAdvisorAssignment(club);

        return new ClubApplicationDto(
            club.ClubId,
            club.ClubName,
            club.Category,
            club.Description,
            club.ApplicantUserId,
            DisplayUser(club.Applicant),
            advisor?.UserId,
            advisor is null ? club.AdvisorName : DisplayUser(advisor.User),
            club.ApplyReason ?? string.Empty,
            club.MaterialUrl ?? string.Empty,
            club.AuditStatus ?? AuditPending,
            AuditStatusText(club.AuditStatus),
            club.ReviewerUserId,
            DisplayUser(club.Reviewer),
            club.ReviewComment,
            club.ContactPhone,
            club.ClubStatus,
            ClubStatusText(club.ClubStatus),
            club.FoundedAt,
            club.CreatedAt,
            club.UpdatedAt);
    }

    private static string? ValidateApplicationRequest(CreateClubApplicationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "社团名称不能为空。";
        if (string.IsNullOrWhiteSpace(req.Category)) return "社团类别不能为空。";
        if (string.IsNullOrWhiteSpace(req.ApplyReason)) return "申请理由不能为空。";
        if (string.IsNullOrWhiteSpace(req.MaterialUrl)) return "材料地址不能为空。";
        return null;
    }

    private static ClubMemberRecordDto ToMemberRecordDto(ClubMember member) => new(
        member.MemberId,
        member.ClubId,
        member.Club?.ClubName ?? $"社团 {member.ClubId}",
        member.UserId,
        DisplayUser(member.User) ?? $"用户 {member.UserId}",
        member.User?.StudentNo,
        member.DepartmentName,
        member.GroupName,
        member.PositionName,
        member.TermName,
        member.TermStart,
        member.TermEnd,
        member.MemberStatus,
        member.JoinAt,
        member.ContributionScore,
        IsCurrentMemberTerm(member));

    private static ClubEvaluationRecordDto ToEvaluationRecordDto(Evaluation evaluation)
    {
        var member = CurrentMembershipForUser(evaluation.User, evaluation.ClubId);
        var evaluationType = NormalizeEvaluationType(evaluation.EvaluationType) ?? EvaluationSemester;
        var publicStatus = NormalizeEvaluationPublicStatus(evaluation.PublicStatus) ?? EvaluationDraft;

        return new ClubEvaluationRecordDto(
            evaluation.EvaluationId,
            evaluationType,
            EvaluationTypeText(evaluationType),
            evaluation.ClubId,
            evaluation.Club?.ClubName ?? $"社团 {evaluation.ClubId}",
            evaluation.UserId,
            DisplayUser(evaluation.User) ?? $"用户 {evaluation.UserId}",
            evaluation.User?.StudentNo,
            member?.DepartmentName,
            member?.GroupName,
            member?.PositionName,
            evaluation.EvaluatorUserId,
            DisplayUser(evaluation.Evaluator),
            evaluation.TermName ?? string.Empty,
            evaluation.AwardTitle,
            evaluation.AwardLevel,
            evaluation.AwardReason,
            evaluation.ActivityScore ?? 0,
            evaluation.TaskScore ?? 0,
            evaluation.LearningScore ?? 0,
            evaluation.AwardScore ?? 0,
            evaluation.TotalScore ?? 0,
            evaluation.Grade ?? EvaluationGrade(evaluation.TotalScore ?? 0),
            publicStatus,
            EvaluationPublicStatusText(publicStatus),
            evaluation.CommentText,
            evaluation.CreatedAt);
    }

    private static string? ValidateMemberTermRequest(
        int userId,
        string? departmentName,
        string? groupName,
        string? positionName,
        string? termName,
        DateTime? termStart,
        DateTime? termEnd,
        string? memberStatus,
        decimal? contributionScore)
    {
        if (userId <= 0) return "请选择成员用户。";
        if (string.IsNullOrWhiteSpace(positionName)) return "成员职位不能为空。";
        if (string.IsNullOrWhiteSpace(termName)) return "任期名称不能为空。";
        if (TextTooLong(departmentName)) return "部门名称不能超过 255 个字符。";
        if (TextTooLong(groupName)) return "小组名称不能超过 255 个字符。";
        if (TextTooLong(positionName)) return "成员职位不能超过 255 个字符。";
        if (TextTooLong(termName)) return "任期名称不能超过 255 个字符。";
        if (contributionScore is < 0) return "贡献分不能为负数。";
        if (termStart is null) return "任期开始时间不能为空。";
        if (termStart.Value == default) return "任期开始时间不能为空。";
        if (termEnd is not null && termEnd.Value.Date < termStart.Value.Date)
        {
            return "任期结束时间不能早于开始时间。";
        }

        if (NormalizeMemberStatus(memberStatus) is null)
        {
            return "成员状态只能是 active、ended 或 suspended。";
        }

        return null;
    }

    private static string? ValidateEvaluationRequest(
        int userId,
        string? evaluationType,
        string? termName,
        string? awardTitle,
        string? awardLevel,
        string? awardReason,
        decimal? activityScore,
        decimal? taskScore,
        decimal? learningScore,
        decimal? awardScore,
        string? publicStatus,
        string? commentText,
        bool requireScores)
    {
        if (userId <= 0) return "请选择被评价成员。";

        var normalizedType = NormalizeEvaluationType(evaluationType);
        if (normalizedType is null) return "评价类型只能是 semester 或 award。";
        if (string.IsNullOrWhiteSpace(termName)) return "考核学期不能为空。";
        if (TextTooLong(termName)) return "考核学期不能超过 255 个字符。";
        if (normalizedType == EvaluationSemester)
        {
            var termValidationError = ValidateSemesterEvaluationTermName(termName.Trim());
            if (termValidationError is not null) return termValidationError;
        }

        if (TextTooLong(awardTitle)) return "奖项标题不能超过 255 个字符。";
        if (TextTooLong(awardLevel)) return "奖项等级不能超过 255 个字符。";
        if (TextTooLong(awardReason)) return "获奖原因不能超过 255 个字符。";
        if (TextTooLong(commentText)) return "评价说明不能超过 255 个字符。";

        if (normalizedType == EvaluationAward)
        {
            if (string.IsNullOrWhiteSpace(awardTitle)) return "评优评奖标题不能为空。";
            if (string.IsNullOrWhiteSpace(awardLevel)) return "评优评奖等级不能为空。";
            if (string.IsNullOrWhiteSpace(awardReason)) return "评优评奖原因不能为空。";
        }

        var status = NormalizeEvaluationPublicStatus(publicStatus);
        if (status is null) return "公示状态只能是 draft 或 published。";

        if (requireScores)
        {
            if (normalizedType == EvaluationAward && awardScore is null)
            {
                return "奖项分不能为空。";
            }
        }

        return ValidateEvaluationScore(activityScore, "参与分") ??
               ValidateEvaluationScore(taskScore, "任务分") ??
               ValidateEvaluationScore(learningScore, "学习分") ??
               ValidateEvaluationScore(awardScore, "奖项分");
    }

    private static string? ValidateMemberGroupingRequest(string? departmentName, string? groupName)
    {
        if (TextTooLong(departmentName)) return "部门名称不能超过 255 个字符。";
        if (TextTooLong(groupName)) return "小组名称不能超过 255 个字符。";
        return null;
    }

    private static bool IsMaintainableClub(Club club) =>
        club.AuditStatus == AuditApproved && club.ClubStatus == ClubActive;

    private static bool IsCurrentMemberTerm(ClubMember member)
    {
        var today = BusinessToday();
        return (member.MemberStatus == null || member.MemberStatus == MemberActive) &&
               (member.TermStart == null || member.TermStart.Value.Date <= today) &&
               (member.TermEnd == null || member.TermEnd.Value.Date >= today);
    }

    private static bool IsStrictPrincipalPosition(string? positionName)
    {
        if (string.IsNullOrWhiteSpace(positionName)) return false;

        var normalized = positionName.Trim();
        if (normalized.StartsWith("\u526f", StringComparison.Ordinal)) return false;

        return PrincipalPositionNames.Contains(normalized);
    }

    private static bool IsCadrePosition(string? positionName)
    {
        if (string.IsNullOrWhiteSpace(positionName)) return false;

        var normalized = positionName.Trim();
        return CadrePositionNames.Contains(normalized);
    }

    private static IEnumerable<GroupingScope> GetCadreGroupingScopes(User user, int clubId)
    {
        var hasOfficerRole = HasClubOfficerRole(user, clubId);
        return user.ClubMemberships
            .Where(cm =>
                cm.ClubId == clubId &&
                IsCurrentMemberTerm(cm) &&
                (hasOfficerRole || IsCadrePosition(cm.PositionName)) &&
                (!string.IsNullOrWhiteSpace(cm.GroupName) ||
                 (IsDepartmentManagerPosition(cm.PositionName) && !string.IsNullOrWhiteSpace(cm.DepartmentName))))
            .Select(cm => IsDepartmentManagerPosition(cm.PositionName)
                ? new GroupingScope(cm.DepartmentName, null)
                : new GroupingScope(cm.DepartmentName, cm.GroupName));
    }

    private static bool GroupingMatchesScope(
        string? targetDepartment,
        string? targetGroup,
        string? scopeDepartment,
        string? scopeGroup)
    {
        if (string.IsNullOrWhiteSpace(scopeGroup))
        {
            return !string.IsNullOrWhiteSpace(scopeDepartment) &&
                   string.Equals(
                       (targetDepartment ?? string.Empty).Trim(),
                       scopeDepartment.Trim(),
                       StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(targetGroup))
        {
            return false;
        }

        var groupMatches = string.Equals(
            targetGroup.Trim(),
            scopeGroup.Trim(),
            StringComparison.OrdinalIgnoreCase);
        var departmentMatches =
            string.IsNullOrWhiteSpace(scopeDepartment) ||
            string.Equals(
                (targetDepartment ?? string.Empty).Trim(),
                scopeDepartment.Trim(),
                StringComparison.OrdinalIgnoreCase);

        return groupMatches && departmentMatches;
    }

    private static bool IsDepartmentManagerPosition(string? positionName)
    {
        if (string.IsNullOrWhiteSpace(positionName)) return false;

        var normalized = positionName.Trim();
        return DepartmentManagerPositionNames.Contains(normalized);
    }

    private static bool CanViewEvaluationRecord(User viewer, int clubId, Evaluation evaluation)
    {
        if (UsersController.IsPlatformAdmin(viewer) ||
            IsClubEvaluationPrincipal(viewer, clubId))
        {
            return true;
        }

        if (NormalizeEvaluationType(evaluation.EvaluationType) == EvaluationAward &&
            NormalizeEvaluationPublicStatus(evaluation.PublicStatus) == EvaluationPublished &&
            (HasClubParticipantRole(viewer, clubId) ||
             viewer.ClubMemberships.Any(cm => cm.ClubId == clubId && IsCurrentMemberTerm(cm))))
        {
            return true;
        }

        var targetMember = CurrentMembershipForUser(evaluation.User, clubId);
        if (targetMember is not null)
        {
            var scopes = GetCadreGroupingScopes(viewer, clubId);
            if (scopes.Any(scope => GroupingMatchesScope(
                targetMember.DepartmentName,
                targetMember.GroupName,
                scope.DepartmentName,
                scope.GroupName)))
            {
                return true;
            }
        }

        return evaluation.UserId == viewer.UserId &&
               NormalizeEvaluationPublicStatus(evaluation.PublicStatus) == EvaluationPublished;
    }

    private static bool IsClubEvaluationPrincipal(User viewer, int clubId) =>
        UsersController.IsSystemAdmin(viewer) ||
        UsersController.IsClubPrincipal(viewer, clubId) ||
        IsClubAdvisor(viewer, clubId);

    private static bool IsClubAdvisor(User viewer, int clubId) =>
        viewer.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            string.Equals(ur.Role.RoleCode, ClubAdvisorRoleCode, StringComparison.OrdinalIgnoreCase));

    private static bool IsClubPrincipal(User viewer, Club club) =>
        club.PresidentUserId == viewer.UserId || UsersController.IsClubPrincipal(viewer, club.ClubId);

    private sealed record GroupingScope(string? DepartmentName, string? GroupName);
    private sealed record EvaluationScoreSnapshot(
        decimal ActivityScore,
        decimal TaskScore,
        decimal LearningScore,
        decimal AwardScore);
    private sealed record EvaluationTermWindow(DateTime Start, DateTime End);

    private static ClubMember? CurrentMembershipForUser(User? user, int clubId) =>
        user?.ClubMemberships
            .Where(cm => cm.ClubId == clubId)
            .OrderByDescending(IsCurrentMemberTerm)
            .ThenByDescending(cm => cm.TermStart)
            .ThenByDescending(cm => cm.JoinAt)
            .FirstOrDefault();

    private static decimal CalculateEvaluationTotal(
        decimal activityScore,
        decimal taskScore,
        decimal learningScore,
        decimal awardScore) =>
        activityScore + taskScore + learningScore + awardScore;

    private async Task<EvaluationScoreSnapshot> ResolveEvaluationScoresAsync(
        int clubId,
        int userId,
        string termName,
        string normalizedType,
        decimal? activityScore,
        decimal? taskScore,
        decimal? learningScore,
        decimal? awardScore,
        int? ignoredEvaluationId = null)
    {
        if (normalizedType == EvaluationAward)
        {
            return new EvaluationScoreSnapshot(
                ClampEvaluationScore(activityScore ?? 0),
                ClampEvaluationScore(taskScore ?? 0),
                ClampEvaluationScore(learningScore ?? 0),
                ClampEvaluationScore(awardScore ?? 0));
        }

        if (activityScore is not null &&
            taskScore is not null &&
            learningScore is not null &&
            awardScore is not null)
        {
            return new EvaluationScoreSnapshot(
                ClampEvaluationScore(activityScore.Value),
                ClampEvaluationScore(taskScore.Value),
                ClampEvaluationScore(learningScore.Value),
                ClampEvaluationScore(awardScore.Value));
        }

        var generated = await CalculateSemesterEvaluationScoresAsync(clubId, userId, termName, ignoredEvaluationId);
        return new EvaluationScoreSnapshot(
            ClampEvaluationScore(activityScore ?? generated.ActivityScore),
            ClampEvaluationScore(taskScore ?? generated.TaskScore),
            ClampEvaluationScore(learningScore ?? generated.LearningScore),
            ClampEvaluationScore(awardScore ?? generated.AwardScore));
    }

    private async Task<EvaluationScoreSnapshot> CalculateSemesterEvaluationScoresAsync(
        int clubId,
        int userId,
        string termName,
        int? ignoredEvaluationId = null)
    {
        var termWindow = ResolveEvaluationTermWindow(termName)
            ?? throw new InvalidOperationException("Semester evaluation termName must be validated before calculating scores.");
        var activityScore = await CalculateSemesterActivityScoreAsync(clubId, userId, termWindow);
        var taskScore = await CalculateSemesterTaskScoreAsync(clubId, userId, termWindow);
        var learningScore = await CalculateSemesterLearningScoreAsync(clubId, userId, termWindow);
        var awardScore = await CalculateSemesterAwardScoreAsync(
            clubId,
            userId,
            termName,
            ignoredEvaluationId);

        return new EvaluationScoreSnapshot(activityScore, taskScore, learningScore, awardScore);
    }

    private async Task<Dictionary<int, EvaluationScoreSnapshot>> CalculateSemesterEvaluationScoresForUsersAsync(
        int clubId,
        IReadOnlyCollection<int> userIds,
        string termName)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, EvaluationScoreSnapshot>();
        }

        var termWindow = ResolveEvaluationTermWindow(termName)
            ?? throw new InvalidOperationException("Semester evaluation termName must be validated before calculating scores.");
        var activityScores = await CalculateSemesterActivityScoresAsync(clubId, ids, termWindow);
        var taskScores = await CalculateSemesterTaskScoresAsync(clubId, ids, termWindow);
        var learningScores = await CalculateSemesterLearningScoresAsync(clubId, ids, termWindow);
        var awardScores = await CalculateSemesterAwardScoresAsync(clubId, ids, termName);

        return ids.ToDictionary(
            userId => userId,
            userId => new EvaluationScoreSnapshot(
                activityScores.GetValueOrDefault(userId),
                taskScores.GetValueOrDefault(userId),
                learningScores.GetValueOrDefault(userId),
                awardScores.GetValueOrDefault(userId)));
    }

    private async Task<decimal> CalculateSemesterActivityScoreAsync(
        int clubId,
        int userId,
        EvaluationTermWindow? termWindow)
    {
        var query =
            from participation in _db.ActivityParticipations.AsNoTracking()
            join activity in _db.Activities.AsNoTracking()
                on participation.ActivityId equals activity.ActivityId
            where activity.ClubId == clubId && participation.UserId == userId
            select new
            {
                activity.StartAt,
                activity.EndAt,
                activity.CreatedAt,
                participation.RegisterStatus,
                participation.SignStatus,
                participation.CheckinAt,
                participation.CheckoutAt
            };

        if (termWindow is not null)
        {
            query = query.Where(row =>
                (row.StartAt ?? row.CreatedAt) <= termWindow.End &&
                (row.EndAt ?? row.StartAt ?? row.CreatedAt) >= termWindow.Start);
        }

        var participations = await query.ToListAsync();
        return ClampEvaluationScore(participations.Sum(row => ActivityParticipationScore(
            row.RegisterStatus,
            row.SignStatus,
            row.CheckinAt,
            row.CheckoutAt)));
    }

    private async Task<Dictionary<int, decimal>> CalculateSemesterActivityScoresAsync(
        int clubId,
        IReadOnlyCollection<int> userIds,
        EvaluationTermWindow? termWindow)
    {
        var query =
            from participation in _db.ActivityParticipations.AsNoTracking()
            join activity in _db.Activities.AsNoTracking()
                on participation.ActivityId equals activity.ActivityId
            where activity.ClubId == clubId && userIds.Contains(participation.UserId)
            select new
            {
                participation.UserId,
                activity.StartAt,
                activity.EndAt,
                activity.CreatedAt,
                participation.RegisterStatus,
                participation.SignStatus,
                participation.CheckinAt,
                participation.CheckoutAt
            };

        if (termWindow is not null)
        {
            query = query.Where(row =>
                (row.StartAt ?? row.CreatedAt) <= termWindow.End &&
                (row.EndAt ?? row.StartAt ?? row.CreatedAt) >= termWindow.Start);
        }

        var participations = await query.ToListAsync();
        return participations
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => ClampEvaluationScore(group.Sum(row => ActivityParticipationScore(
                    row.RegisterStatus,
                    row.SignStatus,
                    row.CheckinAt,
                    row.CheckoutAt))));
    }

    private async Task<decimal> CalculateSemesterTaskScoreAsync(
        int clubId,
        int userId,
        EvaluationTermWindow? termWindow)
    {
        var query =
            from task in _db.ProjectTasks.AsNoTracking()
            join project in _db.Projects.AsNoTracking()
                on task.ProjectId equals (int?)project.ProjectId
            where project.ClubId == clubId &&
                  (task.AssigneeUserId == userId ||
                   task.DeliverableSubmitterId == userId ||
                   _db.ProjectTaskAssignees
                       .AsNoTracking()
                       .Any(assignee => assignee.TaskId == task.TaskId && assignee.UserId == userId))
            select new
            {
                StartAt = task.StartDate ?? project.StartDate ?? project.CreatedAt,
                EndAt = task.FinishDate ??
                    task.DueDate ??
                    project.EndDate ??
                    project.StartDate ??
                    project.CreatedAt,
                task.TaskStatus,
                task.DeliverableStatus,
                task.Progress,
                task.FinishDate,
                task.DeliverableSubmittedAt
            };

        if (termWindow is not null)
        {
            query = query.Where(row => row.StartAt <= termWindow.End && row.EndAt >= termWindow.Start);
        }

        var taskScores = await query
            .Select(row => new
            {
                row.TaskStatus,
                row.DeliverableStatus,
                row.Progress,
                row.FinishDate,
                row.DeliverableSubmittedAt
            })
            .ToListAsync();

        return AverageEvaluationScore(taskScores.Select(row => ProjectTaskScore(
            row.TaskStatus,
            row.DeliverableStatus,
            row.Progress,
            row.FinishDate,
            row.DeliverableSubmittedAt)));
    }

    private async Task<Dictionary<int, decimal>> CalculateSemesterTaskScoresAsync(
        int clubId,
        IReadOnlyCollection<int> userIds,
        EvaluationTermWindow? termWindow)
    {
        var assigneeTaskQuery =
            from task in _db.ProjectTasks.AsNoTracking()
            join project in _db.Projects.AsNoTracking()
                on task.ProjectId equals (int?)project.ProjectId
            where project.ClubId == clubId &&
                  task.AssigneeUserId != null &&
                  userIds.Contains(task.AssigneeUserId.Value)
            select new
            {
                UserId = task.AssigneeUserId ?? 0,
                task.TaskId,
                StartAt = task.StartDate ?? project.StartDate ?? project.CreatedAt,
                EndAt = task.FinishDate ??
                    task.DueDate ??
                    project.EndDate ??
                    project.StartDate ??
                    project.CreatedAt,
                task.TaskStatus,
                task.DeliverableStatus,
                task.Progress,
                task.FinishDate,
                task.DeliverableSubmittedAt
            };

        var deliverableTaskQuery =
            from task in _db.ProjectTasks.AsNoTracking()
            join project in _db.Projects.AsNoTracking()
                on task.ProjectId equals (int?)project.ProjectId
            where project.ClubId == clubId &&
                  task.DeliverableSubmitterId != null &&
                  userIds.Contains(task.DeliverableSubmitterId.Value)
            select new
            {
                UserId = task.DeliverableSubmitterId ?? 0,
                task.TaskId,
                StartAt = task.StartDate ?? project.StartDate ?? project.CreatedAt,
                EndAt = task.FinishDate ??
                    task.DueDate ??
                    project.EndDate ??
                    project.StartDate ??
                    project.CreatedAt,
                task.TaskStatus,
                task.DeliverableStatus,
                task.Progress,
                task.FinishDate,
                task.DeliverableSubmittedAt
            };

        var extraAssigneeTaskQuery =
            from assignee in _db.ProjectTaskAssignees.AsNoTracking()
            join task in _db.ProjectTasks.AsNoTracking()
                on assignee.TaskId equals task.TaskId
            join project in _db.Projects.AsNoTracking()
                on task.ProjectId equals (int?)project.ProjectId
            where project.ClubId == clubId && userIds.Contains(assignee.UserId)
            select new
            {
                assignee.UserId,
                task.TaskId,
                StartAt = task.StartDate ?? project.StartDate ?? project.CreatedAt,
                EndAt = task.FinishDate ??
                    task.DueDate ??
                    project.EndDate ??
                    project.StartDate ??
                    project.CreatedAt,
                task.TaskStatus,
                task.DeliverableStatus,
                task.Progress,
                task.FinishDate,
                task.DeliverableSubmittedAt
            };

        var query = assigneeTaskQuery
            .Concat(deliverableTaskQuery)
            .Concat(extraAssigneeTaskQuery);

        if (termWindow is not null)
        {
            query = query.Where(row => row.StartAt <= termWindow.End && row.EndAt >= termWindow.Start);
        }

        var taskScores = await query.ToListAsync();
        return taskScores
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => AverageEvaluationScore(group
                    .GroupBy(row => row.TaskId)
                    .Select(taskGroup =>
                    {
                        var row = taskGroup.First();
                        return ProjectTaskScore(
                            row.TaskStatus,
                            row.DeliverableStatus,
                            row.Progress,
                            row.FinishDate,
                            row.DeliverableSubmittedAt);
                    })));
    }

    private async Task<decimal> CalculateSemesterLearningScoreAsync(
        int clubId,
        int userId,
        EvaluationTermWindow? termWindow)
    {
        var query =
            from record in _db.LearningRecords.AsNoTracking()
            join item in _db.LearningItems.AsNoTracking()
                on record.ItemId equals item.ItemId
            where item.ClubId == clubId && record.UserId == userId
            select new
            {
                StartAt = item.StartAt ?? record.EnrolledAt ?? item.CreatedAt,
                EndAt = item.EndAt ??
                    record.CompletedAt ??
                    record.LastLearnAt ??
                    record.EnrolledAt ??
                    item.StartAt ??
                    item.CreatedAt,
                record.EnrollStatus,
                record.Progress,
                record.CompletedAt,
                record.DownloadedAt
            };

        if (termWindow is not null)
        {
            query = query.Where(row => row.StartAt <= termWindow.End && row.EndAt >= termWindow.Start);
        }

        var recordScores = await query
            .Select(row => new
            {
                row.EnrollStatus,
                row.Progress,
                row.CompletedAt,
                row.DownloadedAt
            })
            .ToListAsync();

        return AverageEvaluationScore(recordScores.Select(row => LearningRecordScore(
            row.EnrollStatus,
            row.Progress,
            row.CompletedAt,
            row.DownloadedAt)));
    }

    private async Task<Dictionary<int, decimal>> CalculateSemesterLearningScoresAsync(
        int clubId,
        IReadOnlyCollection<int> userIds,
        EvaluationTermWindow? termWindow)
    {
        var query =
            from record in _db.LearningRecords.AsNoTracking()
            join item in _db.LearningItems.AsNoTracking()
                on record.ItemId equals item.ItemId
            where item.ClubId == clubId && userIds.Contains(record.UserId)
            select new
            {
                record.UserId,
                StartAt = item.StartAt ?? record.EnrolledAt ?? item.CreatedAt,
                EndAt = item.EndAt ??
                    record.CompletedAt ??
                    record.LastLearnAt ??
                    record.EnrolledAt ??
                    item.StartAt ??
                    item.CreatedAt,
                record.EnrollStatus,
                record.Progress,
                record.CompletedAt,
                record.DownloadedAt
            };

        if (termWindow is not null)
        {
            query = query.Where(row => row.StartAt <= termWindow.End && row.EndAt >= termWindow.Start);
        }

        var recordScores = await query.ToListAsync();
        return recordScores
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => AverageEvaluationScore(group.Select(row => LearningRecordScore(
                    row.EnrollStatus,
                    row.Progress,
                    row.CompletedAt,
                    row.DownloadedAt))));
    }

    private async Task<decimal> CalculateSemesterAwardScoreAsync(
        int clubId,
        int userId,
        string termName,
        int? ignoredEvaluationId = null)
    {
        var normalizedTerm = termName.Trim();
        var query = _db.Evaluations
            .AsNoTracking()
            .Where(ev =>
                ev.ClubId == clubId &&
                ev.UserId == userId &&
                ev.EvaluationType == EvaluationAward &&
                ev.TermName == normalizedTerm &&
                ev.PublicStatus == EvaluationPublished);

        if (ignoredEvaluationId is not null)
        {
            query = query.Where(ev => ev.EvaluationId != ignoredEvaluationId.Value);
        }

        var total = await query.SumAsync(ev => ev.AwardScore ?? 0);
        return ClampEvaluationScore(total);
    }

    private async Task<Dictionary<int, decimal>> CalculateSemesterAwardScoresAsync(
        int clubId,
        IReadOnlyCollection<int> userIds,
        string termName)
    {
        var normalizedTerm = termName.Trim();
        var awardScores = await _db.Evaluations
            .AsNoTracking()
            .Where(ev =>
                ev.ClubId == clubId &&
                userIds.Contains(ev.UserId) &&
                ev.EvaluationType == EvaluationAward &&
                ev.TermName == normalizedTerm &&
                ev.PublicStatus == EvaluationPublished)
            .GroupBy(ev => ev.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                Score = group.Sum(ev => ev.AwardScore ?? 0)
            })
            .ToListAsync();

        return awardScores.ToDictionary(
            row => row.UserId,
            row => ClampEvaluationScore(row.Score));
    }

    private static decimal ActivityParticipationScore(
        string? registerStatus,
        string? signStatus,
        DateTime? checkinAt,
        DateTime? checkoutAt)
    {
        var normalizedSignStatus = NormalizeScoreStatus(signStatus);
        var normalizedRegisterStatus = NormalizeScoreStatus(registerStatus);

        if (checkoutAt is not null || normalizedSignStatus is "checked_out" or "checkout" or "signed_out")
        {
            return ActivityCheckedOutScore;
        }

        if (checkinAt is not null ||
            normalizedSignStatus is "checked_in" or "checkin" or "signed_in" or "onsite" ||
            normalizedRegisterStatus == "onsite")
        {
            return ActivityCheckedInScore;
        }

        return normalizedRegisterStatus is "accepted" or "approved"
            ? ActivityAcceptedScore
            : 0;
    }

    private static decimal ProjectTaskScore(
        string? taskStatus,
        string? deliverableStatus,
        decimal? progress,
        DateTime? finishDate,
        DateTime? deliverableSubmittedAt)
    {
        var normalizedTaskStatus = NormalizeScoreStatus(taskStatus);
        var normalizedDeliverableStatus = NormalizeScoreStatus(deliverableStatus);

        if (finishDate is not null ||
            normalizedTaskStatus is "finished" or "completed" or "done" or "closed" ||
            normalizedDeliverableStatus is "approved" or "accepted" or "passed")
        {
            return MaxEvaluationScore;
        }

        if (progress is not null)
        {
            return ClampEvaluationScore(progress.Value);
        }

        if (deliverableSubmittedAt is not null ||
            normalizedDeliverableStatus is "submitted" or "pending" or "reviewing")
        {
            return 80m;
        }

        return normalizedTaskStatus is "running" or "ongoing" or "in_progress" ? 50m : 0;
    }

    private static decimal LearningRecordScore(
        string? enrollStatus,
        decimal? progress,
        DateTime? completedAt,
        DateTime? downloadedAt)
    {
        var normalizedStatus = LearningWorkflow.NormalizeRecordStatus(enrollStatus);
        if (completedAt is not null || normalizedStatus == LearningWorkflow.RecordStatusCompleted)
        {
            return MaxEvaluationScore;
        }

        if (progress is not null)
        {
            return ClampEvaluationScore(progress.Value);
        }

        if (downloadedAt is not null)
        {
            return 10m;
        }

        return 0;
    }

    private static decimal AverageEvaluationScore(IEnumerable<decimal> scores)
    {
        var materialized = scores.ToList();
        if (materialized.Count == 0) return 0;
        return ClampEvaluationScore(materialized.Average());
    }

    private static decimal ClampEvaluationScore(decimal score) =>
        decimal.Round(Math.Clamp(score, 0, MaxEvaluationScore), 1, MidpointRounding.AwayFromZero);

    private static string? ValidateSemesterEvaluationTermName(string termName) =>
        ResolveEvaluationTermWindow(termName) is null
            ? "考核学期格式无法识别，请填写如 2026-2027学年、2026秋季或 2027春季。"
            : null;

    private static EvaluationTermWindow? ResolveEvaluationTermWindow(string termName)
    {
        var normalized = termName.Trim();
        var hasSpring = normalized.Contains("春", StringComparison.Ordinal);
        var hasFall = normalized.Contains("秋", StringComparison.Ordinal);
        var hasAcademicYear = normalized.Contains("学年", StringComparison.Ordinal);

        if (hasSpring && hasFall)
        {
            return null;
        }

        var yearRange = Regex.Match(normalized, @"(?<start>20\d{2})\s*[-—~至]\s*(?<end>20\d{2})");
        if (yearRange.Success &&
            int.TryParse(yearRange.Groups["start"].Value, out var startYear) &&
            int.TryParse(yearRange.Groups["end"].Value, out var endYear))
        {
            if (endYear < startYear)
            {
                return null;
            }

            if (hasSpring)
            {
                return new EvaluationTermWindow(
                    new DateTime(endYear, 2, 1),
                    EndOfDay(new DateTime(endYear, 7, 31)));
            }

            if (hasFall)
            {
                return new EvaluationTermWindow(
                    new DateTime(startYear, 9, 1),
                    EndOfDay(new DateTime(startYear + 1, 1, 31)));
            }

            return new EvaluationTermWindow(
                new DateTime(startYear, 9, 1),
                EndOfDay(new DateTime(endYear, 6, 30)));
        }

        var yearMatch = Regex.Match(normalized, @"20\d{2}");
        if (!yearMatch.Success || !int.TryParse(yearMatch.Value, out var year))
        {
            return null;
        }

        if (hasSpring)
        {
            var springYear = hasAcademicYear ? year + 1 : year;
            return new EvaluationTermWindow(
                new DateTime(springYear, 2, 1),
                EndOfDay(new DateTime(springYear, 7, 31)));
        }

        if (hasFall)
        {
            return new EvaluationTermWindow(
                new DateTime(year, 9, 1),
                EndOfDay(new DateTime(year + 1, 1, 31)));
        }

        if (hasAcademicYear)
        {
            return new EvaluationTermWindow(
                new DateTime(year, 9, 1),
                EndOfDay(new DateTime(year + 1, 6, 30)));
        }

        return new EvaluationTermWindow(
            new DateTime(year, 1, 1),
            EndOfDay(new DateTime(year, 12, 31)));
    }

    private static DateTime EndOfDay(DateTime date) => date.Date.AddDays(1).AddTicks(-1);

    private static string NormalizeScoreStatus(string? status) =>
        (status ?? string.Empty).Trim().ToLowerInvariant();

    private static string EvaluationGrade(decimal totalScore)
    {
        if (totalScore >= 320) return "优秀";
        if (totalScore >= 260) return "良好";
        if (totalScore >= 200) return "合格";
        return "待提升";
    }

    private static string? ValidateEvaluationScore(decimal? score, string fieldName)
    {
        if (score is null) return null;
        if (score < 0) return $"{fieldName}不能为负数。";
        if (score > MaxEvaluationScore) return $"{fieldName}不能超过 100。";
        return null;
    }

    private static string? NormalizeEvaluationType(string? evaluationType)
    {
        if (string.IsNullOrWhiteSpace(evaluationType)) return null;

        var normalized = evaluationType.Trim().ToLowerInvariant();
        if (normalized is "award" or "honor" or "prize" or "评奖评优" or "评优评奖")
        {
            return EvaluationAward;
        }

        return normalized switch
        {
            "semester" or "term" or "assessment" or "学期考核" or "成员考核" => EvaluationSemester,
            "award" or "honor" or "prize" or "评优评奖" or "奖项" => EvaluationAward,
            _ => null
        };
    }

    private static string EvaluationTypeText(string evaluationType) => evaluationType switch
    {
        EvaluationAward => "评优评奖",
        _ => "学期考核"
    };

    private static string? NormalizeEvaluationPublicStatus(string? publicStatus)
    {
        if (string.IsNullOrWhiteSpace(publicStatus)) return EvaluationDraft;

        return publicStatus.Trim().ToLowerInvariant() switch
        {
            "draft" or "private" or "草稿" or "未公示" => EvaluationDraft,
            "published" or "public" or "公示" or "已公示" => EvaluationPublished,
            _ => null
        };
    }

    private static string EvaluationPublicStatusText(string publicStatus) => publicStatus switch
    {
        EvaluationPublished => "已公示",
        _ => "草稿"
    };

    private static bool TextTooLong(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length > ClubMemberTextMaxLength;

    private static DateTime BusinessToday() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone).Date;

    private static DateTime BusinessNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone);

    private static DateTime BusinessDate(DateTime utcDateTime)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, BusinessTimeZone).Date;
    }

    private static DateTime BusinessDateBoundaryUtc(DateTime businessDate)
    {
        var localBoundary = DateTime.SpecifyKind(businessDate.Date, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localBoundary, BusinessTimeZone);
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

    private static bool IsTeacherCandidate(User user)
    {
        if (IsStaffNumber(user.StudentNo)) return true;

        return user.UserRoles.Any(ur =>
            ur.Role is not null &&
            ((ur.Role.RoleCode ?? string.Empty).Equals("TEACHER", StringComparison.OrdinalIgnoreCase) ||
             (ur.Role.RoleCode ?? string.Empty).Equals(ClubAdvisorRoleCode, StringComparison.OrdinalIgnoreCase) ||
             (ur.Role.RoleName ?? string.Empty).Contains("教师", StringComparison.Ordinal) ||
             (ur.Role.RoleName ?? string.Empty).Contains("老师", StringComparison.Ordinal)));
    }

    private static bool IsStaffNumber(string? studentNo) =>
        !string.IsNullOrWhiteSpace(studentNo) &&
        studentNo.Trim().Length == 5 &&
        studentNo.Trim().All(char.IsDigit);

    private static UserRole? CurrentAdvisorAssignment(Club club) =>
        club.UserRoles
            .Where(ur =>
                ur.Role is not null &&
                (ur.Role.RoleCode ?? string.Empty).Equals(ClubAdvisorRoleCode, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ur => ur.AssignedAt)
            .ThenBy(ur => ur.UserRoleId)
            .FirstOrDefault();

    private static string? NormalizeMemberStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return MemberActive;

        return status.Trim().ToLowerInvariant() switch
        {
            "active" or "normal" or "enabled" or "在任" or "正常" => MemberActive,
            "ended" or "left" or "finished" or "离任" or "已结束" => MemberEnded,
            "suspended" or "paused" or "disabled" or "暂停" or "停用" => MemberSuspended,
            _ => null
        };
    }

    private static bool IsKnownAuditStatus(string status) =>
        status is AuditPending or AuditApproved or AuditRejected;

    private static string AuditStatusText(string? status) => status switch
    {
        AuditPending => "待审核",
        AuditApproved => "已通过",
        AuditRejected => "已退回",
        _ => "未提交"
    };

    private static string ClubStatusText(string? status) => status switch
    {
        ClubPending => "待生效",
        ClubActive => "运营中",
        ClubRejected => "未通过",
        ClubInactive => "已解散",
        _ => "未设置"
    };

    private static string? DisplayUser(User? user)
    {
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(user.RealName)) return user.RealName;
        if (!string.IsNullOrWhiteSpace(user.Username)) return user.Username;
        return $"用户 {user.UserId}";
    }

    private static string AdvisorCandidateDisplayName(User user)
    {
        var name = DisplayUser(user) ?? $"用户 {user.UserId}";
        var staffNumber = user.StudentNo?.Trim();
        return string.IsNullOrWhiteSpace(staffNumber) ? name : $"{name}（{staffNumber}）";
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly HashSet<string> ClubParticipantRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ClubMemberRoleCode,
        ClubOfficerRoleCode,
        ClubLeaderRoleCode,
        "club_president",
        "advisor"
    };

    private static readonly HashSet<string> ClubManagementRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ClubOfficerRoleCode,
        ClubLeaderRoleCode,
        "club_president"
    };

    private static readonly HashSet<string> ClubMembershipRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ClubMemberRoleCode,
        ClubOfficerRoleCode,
        ClubLeaderRoleCode,
        "club_president"
    };
}

public record ClubDto(
    int Id,
    string Name,
    string? Description,
    string? Category,
    string? Status,
    string StatusText,
    string? LogoUrl,
    int? PresidentUserId,
    string? PresidentName,
    int? AdvisorUserId,
    string? AdvisorName,
    string? ContactPhone,
    string? AuditStatus,
    string AuditStatusText,
    int? ApplicantUserId,
    string? ApplicantName,
    string? ApplyReason,
    string? MaterialUrl,
    int? ReviewerUserId,
    string? ReviewerName,
    string? ReviewComment,
    DateTime? FoundedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ClubApplicationDto(
    int Id,
    string Name,
    string? Category,
    string? Description,
    int? ApplicantUserId,
    string? ApplicantName,
    int? AdvisorUserId,
    string? AdvisorName,
    string ApplyReason,
    string MaterialUrl,
    string AuditStatus,
    string AuditStatusText,
    int? ReviewerUserId,
    string? ReviewerName,
    string? ReviewComment,
    string? ContactPhone,
    string? ClubStatus,
    string ClubStatusText,
    DateTime? FoundedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public class CreateClubRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "CurrentUserId 必须大于或等于 1")]
    public int CurrentUserId { get; set; }

    [Required, StringLength(255, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(255, MinimumLength = 1)]
    public string Category { get; set; } = string.Empty;

    public string? Description { get; set; }
}

public class UpdateClubRequest
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
}

public class UpdateClubProfileRequest
{
    public int CurrentUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public int? PresidentUserId { get; set; }
    public int? AdvisorUserId { get; set; }
    public string? AdvisorName { get; set; }
    public string? ContactPhone { get; set; }
}

public class DissolveClubRequest
{
    public int CurrentUserId { get; set; }
}

public record ClubMemberRecordDto(
    int MemberId,
    int ClubId,
    string ClubName,
    int UserId,
    string UserName,
    string? StudentNo,
    string? DepartmentName,
    string? GroupName,
    string? PositionName,
    string? TermName,
    DateTime? TermStart,
    DateTime? TermEnd,
    string? MemberStatus,
    DateTime? JoinAt,
    decimal? ContributionScore,
    bool IsCurrent);

public class CreateClubMemberTermRequest
{
    public int CurrentUserId { get; set; }
    public int UserId { get; set; }
    public string? DepartmentName { get; set; }
    public string? GroupName { get; set; }
    public string PositionName { get; set; } = string.Empty;
    public string TermName { get; set; } = string.Empty;
    public DateTime TermStart { get; set; }
    public DateTime? TermEnd { get; set; }
    public string? MemberStatus { get; set; } = "active";
    public decimal? ContributionScore { get; set; }
    public bool CloseCurrentTerm { get; set; } = true;
}

public class UpdateClubMemberTermRequest
{
    public int CurrentUserId { get; set; }
    public string? DepartmentName { get; set; }
    public string? GroupName { get; set; }
    public string? PositionName { get; set; }
    public string? TermName { get; set; }
    public DateTime? TermStart { get; set; }
    public DateTime? TermEnd { get; set; }
    public string? MemberStatus { get; set; }
    public decimal? ContributionScore { get; set; }
}

public record ClubEvaluationRecordDto(
    int EvaluationId,
    string EvaluationType,
    string EvaluationTypeText,
    int ClubId,
    string ClubName,
    int UserId,
    string UserName,
    string? StudentNo,
    string? DepartmentName,
    string? GroupName,
    string? PositionName,
    int? EvaluatorUserId,
    string? EvaluatorName,
    string TermName,
    string? AwardTitle,
    string? AwardLevel,
    string? AwardReason,
    decimal ActivityScore,
    decimal TaskScore,
    decimal LearningScore,
    decimal AwardScore,
    decimal TotalScore,
    string Grade,
    string PublicStatus,
    string PublicStatusText,
    string? CommentText,
    DateTime? CreatedAt);

public record ClubEvaluationScorePreviewDto(
    decimal ActivityScore,
    decimal TaskScore,
    decimal LearningScore,
    decimal AwardScore,
    decimal TotalScore,
    string Grade);

public class GenerateClubEvaluationsRequest
{
    public string TermName { get; set; } = string.Empty;
    public bool OverwriteDrafts { get; set; } = true;
}

public record GenerateClubEvaluationsResultDto(
    string TermName,
    int TotalMembers,
    int CreatedCount,
    int RefreshedCount,
    int SkippedPublishedCount,
    List<ClubEvaluationRecordDto> Records);

public class CreateClubEvaluationRequest
{
    public int CurrentUserId { get; set; }
    public string EvaluationType { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string TermName { get; set; } = string.Empty;
    public string? AwardTitle { get; set; }
    public string? AwardLevel { get; set; }
    public string? AwardReason { get; set; }
    public decimal? ActivityScore { get; set; }
    public decimal? TaskScore { get; set; }
    public decimal? LearningScore { get; set; }
    public decimal? AwardScore { get; set; }
    public string? PublicStatus { get; set; } = "draft";
    public string? CommentText { get; set; }
}

public class UpdateClubEvaluationRequest
{
    public int CurrentUserId { get; set; }
    public string? EvaluationType { get; set; }
    public string? TermName { get; set; }
    public string? AwardTitle { get; set; }
    public string? AwardLevel { get; set; }
    public string? AwardReason { get; set; }
    public decimal? ActivityScore { get; set; }
    public decimal? TaskScore { get; set; }
    public decimal? LearningScore { get; set; }
    public decimal? AwardScore { get; set; }
    public string? PublicStatus { get; set; }
    public string? CommentText { get; set; }
}

public class CreateClubApplicationRequest
{
    public int CurrentUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ApplyReason { get; set; } = string.Empty;
    public string MaterialUrl { get; set; } = string.Empty;
    public int? AdvisorUserId { get; set; }
    public string? AdvisorName { get; set; }
    public string? ContactPhone { get; set; }
}

public class ReviewClubApplicationRequest
{
    public int CurrentUserId { get; set; }
    public string? Decision { get; set; }
    public string? ReviewComment { get; set; }
}
