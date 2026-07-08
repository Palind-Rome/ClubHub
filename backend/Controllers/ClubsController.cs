using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
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
    private const string ClubLeaderRoleCode = "CLUB_LEADER";
    private const string ClubAdvisorRoleCode = "ADVISOR";

    private readonly ClubHubDbContext _db;

    public ClubsController(ClubHubDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? viewerUserId)
    {
        var query = ClubQuery();

        if (viewerUserId is not null)
        {
            var viewer = await LoadUserAsync(viewerUserId.Value);
            if (viewer is null) return NotFound(new { message = "当前用户不存在。" });

            if (!UsersController.IsPlatformAdmin(viewer))
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
        [FromQuery] int viewerUserId,
        [FromQuery] string? auditStatus)
    {
        if (viewerUserId <= 0) return BadRequest(new { message = "请选择当前用户。" });

        var viewer = await LoadUserAsync(viewerUserId);
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

        var applications = await query
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ThenByDescending(c => c.ClubId)
            .ToListAsync();

        return Ok(applications.Select(ToApplicationDto));
    }

    [HttpPost("applications")]
    public async Task<IActionResult> CreateApplication([FromBody] CreateClubApplicationRequest req)
    {
        var validationError = ValidateApplicationRequest(req);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var applicant = await LoadUserAsync(req.CurrentUserId);
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
        var maxId = await _db.Clubs.MaxAsync(c => (int?)c.ClubId) ?? 0;
        var club = new Club
        {
            ClubId = maxId + 1,
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

        _db.Clubs.Add(club);
        if (advisor.User is not null)
        {
            await EnsureSingleClubAdvisorRoleAsync(club.ClubId, advisor.User.UserId, now);
        }

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { clubId = club.ClubId }, ToApplicationDto(club));
    }

    [HttpPatch("applications/{clubId:int}/review")]
    public async Task<IActionResult> ReviewApplication(int clubId, [FromBody] ReviewClubApplicationRequest req)
    {
        var decision = req.Decision?.Trim().ToLowerInvariant();
        if (req.CurrentUserId <= 0)
        {
            return BadRequest(new { message = "请选择当前审核用户。" });
        }

        if (decision is not AuditApproved and not AuditRejected)
        {
            return BadRequest(new { message = "审核结果只能是通过或退回。" });
        }

        if (decision == AuditRejected && string.IsNullOrWhiteSpace(req.ReviewComment))
        {
            return BadRequest(new { message = "退回申请时必须填写审核意见。" });
        }

        var reviewer = await LoadUserAsync(req.CurrentUserId);
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
            await EnsurePresidentMembershipAsync(club, now);
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
        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return BadRequest(new { message = "社团名称不能为空。" });
        }

        if (string.IsNullOrWhiteSpace(req.Category))
        {
            return BadRequest(new { message = "社团类别不能为空。" });
        }

        var maxId = await _db.Clubs.MaxAsync(c => (int?)c.ClubId) ?? 0;
        var now = DateTime.UtcNow;
        var club = new Club
        {
            ClubId = maxId + 1,
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
        var access = await EnsureCanMaintainClubAsync(clubId, req.CurrentUserId);
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

            var today = DateTime.UtcNow.Date;
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
        [FromQuery] int viewerUserId,
        [FromQuery] bool includeHistory = false)
    {
        var access = await EnsureCanViewMembersAsync(clubId, viewerUserId);
        if (access.Result is not null) return access.Result;

        var today = DateTime.UtcNow.Date;
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

        var members = await query
            .OrderBy(cm => cm.DepartmentName)
            .ThenBy(cm => cm.GroupName)
            .ThenBy(cm => cm.PositionName)
            .ThenByDescending(cm => cm.TermStart)
            .ThenBy(cm => cm.UserId)
            .ToListAsync();

        return Ok(members.Select(ToMemberRecordDto));
    }

    [HttpPost("{clubId:int}/members/terms")]
    public async Task<IActionResult> CreateMemberTerm(int clubId, [FromBody] CreateClubMemberTermRequest req)
    {
        var access = await EnsureCanMaintainClubAsync(clubId, req.CurrentUserId);
        if (access.Result is not null) return access.Result;

        var club = access.Club!;
        if (!IsMaintainableClub(club))
        {
            return Conflict(new { message = "只有已通过审核且正在运营的社团可以维护成员任期。" });
        }

        var validationError = ValidateMemberTermRequest(
            req.UserId,
            req.PositionName,
            req.TermName,
            req.TermStart,
            req.TermEnd,
            req.MemberStatus);
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

        var maxId = await _db.ClubMembers.MaxAsync(cm => (int?)cm.MemberId) ?? 0;
        var member = new ClubMember
        {
            MemberId = maxId + 1,
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

        _db.ClubMembers.Add(member);

        if (IsCurrentMemberTerm(member) && IsPrincipalPosition(member.PositionName))
        {
            club.PresidentUserId = req.UserId;
            club.UpdatedAt = now;
            await EnsureSingleClubPresidentRoleAsync(clubId, req.UserId, now);
        }

        await _db.SaveChangesAsync();

        var created = await MemberQuery().FirstAsync(cm => cm.MemberId == member.MemberId);
        return Created(
            $"/api/clubs/{clubId}/members?viewerUserId={req.CurrentUserId}&includeHistory=true",
            ToMemberRecordDto(created));
    }

    [HttpPatch("{clubId:int}/members/{memberId:int}")]
    public async Task<IActionResult> UpdateMemberTerm(
        int clubId,
        int memberId,
        [FromBody] UpdateClubMemberTermRequest req)
    {
        var access = await EnsureCanMaintainClubAsync(clubId, req.CurrentUserId);
        if (access.Result is not null) return access.Result;

        var club = access.Club!;
        if (!IsMaintainableClub(club))
        {
            return Conflict(new { message = "只有已通过审核且正在运营的社团可以维护成员任期。" });
        }

        var member = await _db.ClubMembers.FirstOrDefaultAsync(cm =>
            cm.ClubId == clubId && cm.MemberId == memberId);
        if (member is null)
        {
            return NotFound(new { message = "社团成员任期记录不存在。" });
        }

        var termStart = req.TermStart?.Date ?? member.TermStart?.Date;
        var termEnd = req.TermEnd?.Date ?? member.TermEnd?.Date;
        var validationError = ValidateMemberTermRequest(
            member.UserId,
            req.PositionName ?? member.PositionName,
            req.TermName ?? member.TermName,
            termStart,
            termEnd,
            req.MemberStatus ?? member.MemberStatus);
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
        if (IsCurrentMemberTerm(member) && IsPrincipalPosition(member.PositionName))
        {
            club.PresidentUserId = member.UserId;
            club.UpdatedAt = now;
            await EnsureSingleClubPresidentRoleAsync(clubId, member.UserId, now);
        }
        else if (club.PresidentUserId == member.UserId)
        {
            await RefreshClubPresidentAsync(club, member.MemberId, now);
        }

        await _db.SaveChangesAsync();

        var updated = await MemberQuery().FirstAsync(cm => cm.MemberId == memberId);
        return Ok(ToMemberRecordDto(updated));
    }

    [HttpPatch("{clubId:int}/dissolve")]
    public async Task<IActionResult> Dissolve(int clubId, [FromBody] DissolveClubRequest req)
    {
        if (req.CurrentUserId <= 0)
        {
            return BadRequest(new { message = "请选择当前操作用户。" });
        }

        var viewer = await LoadUserAsync(req.CurrentUserId);
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

        if (!UsersController.IsSystemAdmin(viewer) && !UsersController.IsClubPrincipal(viewer, clubId))
        {
            return (StatusCode(403, new { message = "只有系统管理员或本社团负责人可以维护该社团。" }), club, viewer);
        }

        return (null, club, viewer);
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
            UsersController.IsSystemAdmin(viewer) ||
            UsersController.IsClubPrincipal(viewer, clubId) ||
            HasClubParticipantRole(viewer, clubId) ||
            viewer.ClubMemberships.Any(cm =>
                cm.ClubId == clubId &&
                UsersController.IsActive(cm.MemberStatus));
        if (!canView)
        {
            return (StatusCode(403, new { message = "只有系统管理员、本社团负责人、成员、干部或指导老师可以查看成员任期。" }), club, viewer);
        }

        return (null, club, viewer);
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

    private static bool HasClubParticipantRole(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            ClubParticipantRoleCodes.Contains((ur.Role.RoleCode ?? string.Empty).Trim()));

    private async Task<User?> LoadUserAsync(int userId) =>
        await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId);

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

        var nextUserRoleId = (await _db.UserRoles.MaxAsync(ur => (int?)ur.UserRoleId) ?? 0) + 1;
        _db.UserRoles.Add(new UserRole
        {
            UserRoleId = nextUserRoleId,
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
            "指定社团指导角色，可查看社团运营并审核活动、项目、经费和评价。",
            now);
        await RemoveClubAdvisorRolesExceptAsync(clubId, userId);

        var hasRole = await _db.UserRoles.AnyAsync(ur =>
            ur.UserId == userId &&
            ur.ClubId == clubId &&
            ur.Role != null &&
            ur.Role.RoleCode.ToUpper() == ClubAdvisorRoleCode);
        if (hasRole) return;

        var nextUserRoleId = (await _db.UserRoles.MaxAsync(ur => (int?)ur.UserRoleId) ?? 0) + 1;
        _db.UserRoles.Add(new UserRole
        {
            UserRoleId = nextUserRoleId,
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
        if (role is not null) return role;

        var nextRoleId = (await _db.Roles.MaxAsync(r => (int?)r.RoleId) ?? 0) + 1;
        role = new Role
        {
            RoleId = nextRoleId,
            RoleCode = roleCode,
            RoleName = roleName,
            RoleScope = "club",
            PermissionDesc = permissionDesc,
            CreatedAt = now
        };
        _db.Roles.Add(role);
        return role;
    }

    private async Task RefreshClubPresidentAsync(Club club, int ignoredMemberId, DateTime now)
    {
        var today = now.Date;
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

        var selected = nextPresident.FirstOrDefault(cm => IsPrincipalPosition(cm.PositionName));
        club.PresidentUserId = selected?.UserId;
        club.UpdatedAt = now;

        if (club.PresidentUserId is null)
        {
            await RemoveClubPresidentRolesExceptAsync(club.ClubId, null);
            return;
        }

        await EnsureSingleClubPresidentRoleAsync(club.ClubId, club.PresidentUserId.Value, now);
    }

    private async Task EnsurePresidentMembershipAsync(Club club, DateTime now)
    {
        if (club.ApplicantUserId is null) return;

        var role = await EnsureClubRoleAsync(
            ClubLeaderRoleCode,
            "社团负责人",
            "指定社团内最高业务角色，可维护社团信息、成员、社团内部角色和运营统计。",
            now);

        var hasRole = await _db.UserRoles.AnyAsync(ur =>
            ur.UserId == club.ApplicantUserId.Value &&
            ur.ClubId == club.ClubId &&
            ur.Role != null &&
            ur.Role.RoleCode.ToUpper() == ClubLeaderRoleCode);
        if (!hasRole)
        {
            var nextUserRoleId = (await _db.UserRoles.MaxAsync(ur => (int?)ur.UserRoleId) ?? 0) + 1;
            _db.UserRoles.Add(new UserRole
            {
                UserRoleId = nextUserRoleId,
                UserId = club.ApplicantUserId.Value,
                RoleId = role.RoleId,
                ClubId = club.ClubId,
                AssignedAt = now
            });
        }

        var hasMember = await _db.ClubMembers.AnyAsync(cm =>
            cm.UserId == club.ApplicantUserId.Value &&
            cm.ClubId == club.ClubId &&
            cm.MemberStatus == "active");
        if (!hasMember)
        {
            var nextMemberId = (await _db.ClubMembers.MaxAsync(cm => (int?)cm.MemberId) ?? 0) + 1;
            _db.ClubMembers.Add(new ClubMember
            {
                MemberId = nextMemberId,
                ClubId = club.ClubId,
                UserId = club.ApplicantUserId.Value,
                PositionName = "负责人",
                TermName = $"{now.Year} 创始任期",
                TermStart = now.Date,
                MemberStatus = "active",
                JoinAt = now,
                ContributionScore = 0
            });
        }
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

    private static ClubApplicationDto ToApplicationDto(Club club) => new(
        club.ClubId,
        club.ClubName,
        club.Category,
        club.Description,
        club.ApplicantUserId,
        DisplayUser(club.Applicant),
        club.ApplyReason ?? string.Empty,
        club.MaterialUrl ?? string.Empty,
        club.AuditStatus ?? AuditPending,
        AuditStatusText(club.AuditStatus),
        club.ReviewerUserId,
        DisplayUser(club.Reviewer),
        club.ReviewComment,
        club.ClubStatus,
        ClubStatusText(club.ClubStatus),
        club.FoundedAt,
        club.CreatedAt,
        club.UpdatedAt);

    private static string? ValidateApplicationRequest(CreateClubApplicationRequest req)
    {
        if (req.CurrentUserId <= 0) return "请选择当前申请人。";
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

    private static string? ValidateMemberTermRequest(
        int userId,
        string? positionName,
        string? termName,
        DateTime? termStart,
        DateTime? termEnd,
        string? memberStatus)
    {
        if (userId <= 0) return "请选择成员用户。";
        if (string.IsNullOrWhiteSpace(positionName)) return "成员职位不能为空。";
        if (string.IsNullOrWhiteSpace(termName)) return "任期名称不能为空。";
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

    private static bool IsMaintainableClub(Club club) =>
        club.AuditStatus == AuditApproved && club.ClubStatus == ClubActive;

    private static bool IsCurrentMemberTerm(ClubMember member)
    {
        var today = DateTime.UtcNow.Date;
        return (member.MemberStatus == null || member.MemberStatus == MemberActive) &&
               (member.TermStart == null || member.TermStart.Value.Date <= today) &&
               (member.TermEnd == null || member.TermEnd.Value.Date >= today);
    }

    private static bool IsPrincipalPosition(string? positionName)
    {
        if (string.IsNullOrWhiteSpace(positionName)) return false;

        return positionName.Contains("负责人", StringComparison.Ordinal) ||
               positionName.Contains("会长", StringComparison.Ordinal) ||
               positionName.Contains("社长", StringComparison.Ordinal) ||
               positionName.Contains("president", StringComparison.OrdinalIgnoreCase) ||
               positionName.Contains("leader", StringComparison.OrdinalIgnoreCase);
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

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly HashSet<string> ClubParticipantRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "club_member",
        "club_officer",
        "club_leader",
        "club_president",
        "advisor"
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
    string ApplyReason,
    string MaterialUrl,
    string AuditStatus,
    string AuditStatusText,
    int? ReviewerUserId,
    string? ReviewerName,
    string? ReviewComment,
    string? ClubStatus,
    string ClubStatusText,
    DateTime? FoundedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public class CreateClubRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
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
