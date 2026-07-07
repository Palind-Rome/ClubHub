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
    private const string ClubPresidentRoleCode = "club_president";

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
                    c.ClubStatus == ClubActive ||
                    c.ApplicantUserId == viewer.UserId ||
                    c.Members.Any(m => m.UserId == viewer.UserId));
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
            AdvisorName = EmptyToNull(req.AdvisorName),
            ContactPhone = EmptyToNull(req.ContactPhone),
            ApplyReason = req.ApplyReason.Trim(),
            MaterialUrl = req.MaterialUrl.Trim(),
            AuditStatus = AuditPending,
            ClubStatus = ClubPending,
            CreatedAt = now,
            UpdatedAt = now,
            Applicant = applicant
        };

        _db.Clubs.Add(club);
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

    [HttpDelete("{clubId:int}")]
    public async Task<IActionResult> Delete(int clubId)
    {
        var club = await _db.Clubs.FindAsync(clubId);
        if (club is null) return NotFound();

        _db.Clubs.Remove(club);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<Club> ClubQuery() =>
        _db.Clubs
            .AsNoTracking()
            .Include(c => c.Applicant)
            .Include(c => c.Reviewer)
            .Include(c => c.President)
            .Include(c => c.Members);

    private async Task<User?> LoadUserAsync(int userId) =>
        await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId);

    private async Task EnsurePresidentMembershipAsync(Club club, DateTime now)
    {
        if (club.ApplicantUserId is null) return;

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleCode == ClubPresidentRoleCode);
        if (role is null)
        {
            var nextRoleId = (await _db.Roles.MaxAsync(r => (int?)r.RoleId) ?? 0) + 1;
            role = new Role
            {
                RoleId = nextRoleId,
                RoleCode = ClubPresidentRoleCode,
                RoleName = "社团负责人",
                RoleScope = "club",
                PermissionDesc = "维护本社团基础信息、成员与干部任期。",
                CreatedAt = now
            };
            _db.Roles.Add(role);
        }

        var hasRole = await _db.UserRoles.AnyAsync(ur =>
            ur.UserId == club.ApplicantUserId.Value &&
            ur.ClubId == club.ClubId &&
            ur.Role != null &&
            ur.Role.RoleCode == ClubPresidentRoleCode);
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

    private static ClubDto ToClubDto(Club club) => new(
        club.ClubId,
        club.ClubName,
        club.Description,
        club.Category,
        club.ClubStatus,
        ClubStatusText(club.ClubStatus),
        club.LogoUrl,
        club.PresidentUserId,
        DisplayUser(club.President),
        club.AdvisorName,
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
        "inactive" => "停用",
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

public class CreateClubApplicationRequest
{
    public int CurrentUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ApplyReason { get; set; } = string.Empty;
    public string MaterialUrl { get; set; } = string.Empty;
    public string? AdvisorName { get; set; }
    public string? ContactPhone { get; set; }
}

public class ReviewClubApplicationRequest
{
    public int CurrentUserId { get; set; }
    public string? Decision { get; set; }
    public string? ReviewComment { get; set; }
}
