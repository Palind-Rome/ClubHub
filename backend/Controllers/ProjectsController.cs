using System.Data;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiProject = Org.OpenAPITools.Models.Project;
using AssignProjectLeaderRequest = Org.OpenAPITools.Models.AssignProjectLeaderRequest;
using CancelProjectRequest = Org.OpenAPITools.Models.CancelProjectRequest;
using CreateProjectRequest = Org.OpenAPITools.Models.CreateProjectRequest;
using DbProject = ClubHub.Api.Data.Entities.Project;
using ReviewProjectRequest = Org.OpenAPITools.Models.ReviewProjectRequest;

namespace ClubHub.Api.Controllers;

/// <summary>
/// Project initiation, leader assignment, advisor review, and cancellation APIs for requirement 1.10.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private const int MaxCreateRetries = 3;
    private const string PendingStatus = "pending";
    private const string RunningStatus = "running";
    private const string ClosedStatus = "closed";
    private const string FinishedStatus = "finished";
    private const string DelayedStatus = "delayed";
    private const string ActiveMemberStatus = "active";
    private const string NormalAccountStatus = "normal";
    private const string EnabledStatus = "enabled";

    private static readonly HashSet<string> ReviewStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        RunningStatus,
        ClosedStatus
    };

    private static readonly HashSet<string> ClubOfficerRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "club_officer",
        "officer",
        "club_manager"
    };

    private static readonly HashSet<string> AdvisorRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "advisor",
        "club_advisor",
        "teacher_advisor"
    };

    private static readonly HashSet<string> PlatformClubAdminRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "platform_admin",
        "club_admin",
        "admin",
        "club_reviewer"
    };

    private readonly ClubHubDbContext _db;

    public ProjectsController(ClubHubDbContext db) => _db = db;

    /// <summary>
    /// Gets project applications, optionally filtered by club, with bounded pagination.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? clubId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (clubId is not null and <= 0) return BadRequest("Club id must be greater than 0.");
        if (page <= 0) return BadRequest("Page must be greater than 0.");
        if (pageSize <= 0 || pageSize > 100) return BadRequest("Page size must be between 1 and 100.");

        var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue) return BadRequest("Pagination parameters are too large.");

        var query = _db.Projects.AsNoTracking();
        if (clubId is not null) query = query.Where(p => p.ClubId == clubId);

        var projects = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.ProjectId)
            .Skip((int)skip)
            .Take(pageSize)
            .Select(p => ToProjectDto(p))
            .ToListAsync();

        return Ok(projects);
    }

    /// <summary>
    /// Gets a single project application by id.
    /// </summary>
    [HttpGet("{projectId:int}")]
    public async Task<IActionResult> GetById(int projectId)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .Select(p => ToProjectDto(p))
            .FirstOrDefaultAsync();

        return project is null ? NotFound("Project does not exist.") : Ok(project);
    }

    /// <summary>
    /// Creates a project initiation application in pending status.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest? req)
    {
        if (req is null) return BadRequest("Request body is required.");

        var validation = await ValidateProjectInput(
            req.ClubId,
            req.ProjectName,
            req.LeaderUserId,
            req.StartDate,
            req.EndDate);
        if (validation is not null) return validation;

        var access = await EnsureProjectApplicantAsync(req.CurrentUserId, req.ClubId);
        if (access.Result is not null) return access.Result;

        for (var attempt = 1; attempt <= MaxCreateRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var project = new DbProject
            {
                ProjectId = await GetNextProjectId(),
                ClubId = req.ClubId,
                ProjectName = req.ProjectName.Trim(),
                Description = NormalizeOptionalText(req.Description),
                LeaderUserId = req.LeaderUserId,
                StartDate = req.StartDate,
                EndDate = req.EndDate,
                ProjectStatus = PendingStatus,
                CreatedAt = DateTime.UtcNow
            };

            _db.Projects.Add(project);

            try
            {
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetById), new { projectId = project.ProjectId }, ToProjectDto(project));
            }
            catch (DbUpdateException) when (attempt < MaxCreateRetries)
            {
                await transaction.RollbackAsync();
                _db.Entry(project).State = EntityState.Detached;
            }
        }

        return Conflict("Project id generation conflicted. Please retry.");
    }

    /// <summary>
    /// Assigns or updates the project leader. The operator must be a club maintainer.
    /// </summary>
    [HttpPut("{projectId:int}/leader")]
    public async Task<IActionResult> AssignLeader(int projectId, [FromBody] AssignProjectLeaderRequest? req)
    {
        if (req is null) return BadRequest("Request body is required.");

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound("Project does not exist.");

        var operatorAccess = await EnsureActiveUserAsync(req.CurrentUserId, "assign project leader");
        if (operatorAccess.Result is not null) return operatorAccess.Result;
        if (!CanAssignProjectLeader(operatorAccess.User!, project.ClubId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Only club leaders or club officers can assign project leaders.");
        }

        var validation = await ValidateProjectLeader(project.ClubId, req.LeaderUserId);
        if (validation is not null) return validation;

        project.LeaderUserId = req.LeaderUserId;
        await _db.SaveChangesAsync();

        return Ok(ToProjectDto(project));
    }

    /// <summary>
    /// Reviews a project initiation application. Requirement 1.10 uses one advisor review round.
    /// </summary>
    [HttpPost("{projectId:int}/review")]
    public async Task<IActionResult> Review(int projectId, [FromBody] ReviewProjectRequest? req)
    {
        if (req is null) return BadRequest("Request body is required.");

        var normalizedStatus = ToReviewStatusValue(req.ProjectStatus);
        if (!ReviewStatuses.Contains(normalizedStatus))
        {
            return BadRequest("Project review status must be running or closed.");
        }

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound("Project does not exist.");

        var reviewerAccess = await EnsureActiveUserAsync(req.CurrentUserId, "review project application");
        if (reviewerAccess.Result is not null) return reviewerAccess.Result;
        if (!await CanReviewProjectAsync(reviewerAccess.User!, project.ClubId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Only the club advisor can review project applications.");
        }

        if (!string.Equals(project.ProjectStatus, PendingStatus, StringComparison.OrdinalIgnoreCase))
        {
            // Repeated review requests must not overwrite the original reviewer or comment.
            return string.Equals(project.ProjectStatus, normalizedStatus, StringComparison.OrdinalIgnoreCase)
                ? Ok(ToProjectDto(project))
                : BadRequest("Only pending projects can be reviewed.");
        }

        project.ProjectStatus = normalizedStatus;
        project.ReviewerUserId = req.CurrentUserId;
        project.ReviewComment = NormalizeOptionalText(req.ReviewComment);

        await _db.SaveChangesAsync();
        return Ok(ToProjectDto(project));
    }

    /// <summary>
    /// Allows system admins, club leaders, or platform club admins to cancel eligible projects.
    /// </summary>
    [HttpPost("{projectId:int}/cancel")]
    public async Task<IActionResult> Cancel(int projectId, [FromBody] CancelProjectRequest? req)
    {
        if (req is null) return BadRequest("Request body is required.");

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound("Project does not exist.");

        var projectStatus = NormalizeProjectStatus(project.ProjectStatus);
        if (!IsCancelableProjectStatus(projectStatus))
        {
            return BadRequest("Only pending or running projects can be canceled.");
        }

        var applicantAccess = await EnsureActiveUserAsync(req.CurrentUserId, "cancel project application");
        if (applicantAccess.Result is not null) return applicantAccess.Result;
        if (!CanCancelProject(applicantAccess.User!, project.ClubId, projectStatus))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Only system admins, club leaders, or platform club admins can cancel this project.");
        }

        project.ProjectStatus = ClosedStatus;
        if (projectStatus == PendingStatus)
        {
            project.ReviewerUserId = null;
            project.ReviewComment = BuildCancelComment(req.CancelReason);
        }

        await _db.SaveChangesAsync();
        return Ok(ToProjectDto(project));
    }

    private async Task<IActionResult?> ValidateProjectInput(
        int clubId,
        string projectName,
        int? leaderUserId,
        DateTime startDate,
        DateTime? endDate)
    {
        // Keep create-time checks centralized so later project fields can be added without scattering validation.
        if (clubId <= 0) return BadRequest("Club id must be greater than 0.");
        if (string.IsNullOrWhiteSpace(projectName)) return BadRequest("Project name is required.");
        if (projectName.Trim().Length > 100) return BadRequest("Project name cannot exceed 100 characters.");
        if (endDate is not null && endDate < startDate) return BadRequest("Project end date cannot be earlier than start date.");

        var club = await _db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null) return BadRequest("Club does not exist.");
        if (!UsersController.IsActive(club.ClubStatus)) return BadRequest("Only active clubs can submit project applications.");

        if (leaderUserId is not null)
        {
            var leaderValidation = await ValidateProjectLeader(clubId, leaderUserId.Value);
            if (leaderValidation is not null) return leaderValidation;
        }

        return null;
    }

    /// <summary>
    /// Validates that the project leader is an active user and active member of the club.
    /// </summary>
    private async Task<IActionResult?> ValidateProjectLeader(int clubId, int leaderUserId)
    {
        if (leaderUserId <= 0) return BadRequest("Leader user id must be greater than 0.");

        var userExists = await ActiveUserExists(leaderUserId);
        if (!userExists) return BadRequest("Leader user does not exist or is disabled.");

        var isClubMember = await IsActiveClubMember(clubId, leaderUserId);

        if (!isClubMember) return BadRequest("Project leader must be an active member of the club.");

        return null;
    }

    private async Task<(IActionResult? Result, User? User)> EnsureProjectApplicantAsync(int currentUserId, int clubId)
    {
        var access = await EnsureActiveUserAsync(currentUserId, "create project application");
        if (access.Result is not null) return access;

        if (!await CanSubmitProjectApplicationAsync(access.User!, clubId))
        {
            return (StatusCode(StatusCodes.Status403Forbidden, "Only club leaders or club advisors can submit project applications."), access.User);
        }

        return access;
    }

    private async Task<(IActionResult? Result, User? User)> EnsureActiveUserAsync(int currentUserId, string actionName)
    {
        if (currentUserId <= 0) return (BadRequest($"Current user id is required to {actionName}."), null);

        var user = await LoadUserAsync(currentUserId);
        if (user is null) return (NotFound("Current user does not exist."), null);
        if (!UsersController.IsActive(user.AccountStatus))
        {
            return (BadRequest("Current user account is disabled."), user);
        }

        return (null, user);
    }

    private Task<User?> LoadUserAsync(int userId)
    {
        return _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId);
    }

    private async Task<bool> CanSubmitProjectApplicationAsync(User user, int clubId)
    {
        if (IsProjectAdmin(user)) return false;

        return UsersController.IsClubPrincipal(user, clubId) ||
            HasClubRole(user, clubId, AdvisorRoleCodes) ||
            await IsNamedClubAdvisorAsync(user, clubId);
    }

    private static bool CanAssignProjectLeader(User user, int clubId)
    {
        if (IsProjectAdmin(user)) return false;

        return UsersController.IsClubPrincipal(user, clubId) ||
            HasClubRole(user, clubId, ClubOfficerRoleCodes);
    }

    private async Task<bool> CanReviewProjectAsync(User user, int clubId)
    {
        if (IsProjectAdmin(user)) return false;

        if (HasClubRole(user, clubId, AdvisorRoleCodes))
        {
            return true;
        }

        return await IsNamedClubAdvisorAsync(user, clubId);
    }

    private static bool CanCancelProject(User user, int clubId, string projectStatus)
    {
        if (UsersController.IsSystemAdmin(user))
        {
            return true;
        }

        if (IsPlatformClubAdmin(user))
        {
            return projectStatus == RunningStatus;
        }

        if (projectStatus == PendingStatus)
        {
            return UsersController.IsClubPrincipal(user, clubId);
        }

        return false;
    }

    private static bool IsCancelableProjectStatus(string projectStatus) =>
        projectStatus is PendingStatus or RunningStatus;

    private async Task<bool> IsNamedClubAdvisorAsync(User user, int clubId)
    {
        // New club profile flow stores advisors as scoped ADVISOR roles. Older seed/demo data may
        // only carry a display name on CLUBS, so allow a teacher account whose name matches it.
        if (!IsTeacherAccount(user)) return false;

        var advisorName = await _db.Clubs
            .AsNoTracking()
            .Where(club => club.ClubId == clubId)
            .Select(club => club.AdvisorName)
            .FirstOrDefaultAsync();

        return AdvisorNameMatchesUser(advisorName, user);
    }

    private static bool HasClubRole(User user, int clubId, IReadOnlySet<string> roleCodes)
    {
        return user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            roleCodes.Contains(NormalizeRoleCode(ur.Role.RoleCode)));
    }

    private static bool IsProjectAdmin(User user) =>
        UsersController.IsSystemAdmin(user) || IsPlatformClubAdmin(user);

    private static bool IsPlatformClubAdmin(User user) =>
        user.UserRoles.Any(ur => IsPlatformClubAdminRole(ur.Role));

    private static bool IsPlatformClubAdminRole(Role? role)
    {
        if (role is null) return false;

        var code = NormalizeRoleCode(role.RoleCode);
        if (code is "system_admin" or "sysadmin") return false;
        if (PlatformClubAdminRoleCodes.Contains(code)) return true;

        return (role.RoleName ?? string.Empty).Contains("社团管理员", StringComparison.Ordinal) &&
               ((role.RoleScope ?? string.Empty).Contains("平台", StringComparison.Ordinal) ||
                (role.PermissionDesc ?? string.Empty).Contains("审核", StringComparison.Ordinal));
    }

    private static bool IsTeacherRole(Role role)
    {
        var code = NormalizeRoleCode(role.RoleCode);
        return code is "teacher" or "advisor" or "club_advisor" or "teacher_advisor" ||
            (role.RoleName ?? string.Empty).Contains("教师", StringComparison.Ordinal) ||
            (role.RoleName ?? string.Empty).Contains("老师", StringComparison.Ordinal);
    }

    private static bool IsTeacherAccount(User user)
    {
        return IsStaffNumber(user.StudentNo) ||
            user.UserRoles.Any(ur => ur.Role is not null && IsTeacherRole(ur.Role));
    }

    private static bool IsStaffNumber(string? studentNo)
    {
        return !string.IsNullOrWhiteSpace(studentNo) &&
            studentNo.Trim().Length == 5 &&
            studentNo.Trim().All(char.IsDigit);
    }

    private static bool AdvisorNameMatchesUser(string? advisorName, User user)
    {
        if (string.IsNullOrWhiteSpace(advisorName)) return false;

        var normalizedAdvisor = advisorName.Trim();
        return !string.IsNullOrWhiteSpace(user.RealName) &&
            normalizedAdvisor.Contains(user.RealName.Trim(), StringComparison.Ordinal);
    }

    private Task<bool> IsActiveClubMember(int clubId, int userId)
    {
        return _db.ClubMembers.AnyAsync(m =>
            m.ClubId == clubId &&
            m.UserId == userId &&
            (m.MemberStatus == null ||
             m.MemberStatus == string.Empty ||
             m.MemberStatus.ToLower() == ActiveMemberStatus ||
             m.MemberStatus.ToLower() == NormalAccountStatus ||
             m.MemberStatus.ToLower() == EnabledStatus ||
             m.MemberStatus == "在任" ||
             m.MemberStatus == "正常"));
    }

    /// <summary>
    /// Checks whether a user exists and is not disabled. Null status is allowed for early seed data.
    /// </summary>
    private Task<bool> ActiveUserExists(int userId)
    {
        return _db.Users.AnyAsync(u =>
            u.UserId == userId &&
            (u.AccountStatus == null ||
             u.AccountStatus == string.Empty ||
             u.AccountStatus.ToLower() == ActiveMemberStatus ||
             u.AccountStatus.ToLower() == NormalAccountStatus ||
             u.AccountStatus.ToLower() == EnabledStatus ||
             u.AccountStatus == "在任" ||
             u.AccountStatus == "正常"));
    }

    private async Task<int> GetNextProjectId()
    {
        // The course schema has no PROJECTS sequence yet, so creation uses SERIALIZABLE + retry to avoid max(id)+1 races.
        var maxId = await _db.Projects.MaxAsync(p => (int?)p.ProjectId) ?? 0;
        return maxId + 1;
    }

    /// <summary>
    /// Normalizes optional text fields so blank strings are not stored as meaningful values.
    /// </summary>
    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeProjectStatus(string? status) =>
        (status ?? string.Empty).Trim().ToLowerInvariant();

    private static string BuildCancelComment(string? cancelReason)
    {
        var reason = NormalizeOptionalText(cancelReason);
        return reason is null ? "申请人撤销立项申请。" : $"申请人撤销立项申请：{reason}";
    }

    private static string NormalizeRoleCode(string? roleCode) =>
        (roleCode ?? string.Empty).Trim().ToLowerInvariant();

    private static ApiProject ToProjectDto(DbProject project)
    {
        if (project.StartDate is null)
        {
            throw new InvalidOperationException("Project.StartDate is required.");
        }

        return new ApiProject
        {
            Id = project.ProjectId,
            ClubId = project.ClubId,
            ProjectName = project.ProjectName,
            Description = project.Description,
            LeaderUserId = project.LeaderUserId,
            StartDate = project.StartDate.Value,
            EndDate = project.EndDate,
            ProjectStatus = ToProjectStatusEnum(project.ProjectStatus),
            ReviewerUserId = project.ReviewerUserId,
            ReviewComment = project.ReviewComment,
            CreatedAt = project.CreatedAt
        };
    }

    private static string ToReviewStatusValue(ReviewProjectRequest.ProjectStatusEnum status)
    {
        return status switch
        {
            ReviewProjectRequest.ProjectStatusEnum.RunningEnum => RunningStatus,
            ReviewProjectRequest.ProjectStatusEnum.ClosedEnum => ClosedStatus,
            _ => string.Empty
        };
    }

    private static ApiProject.ProjectStatusEnum ToProjectStatusEnum(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            PendingStatus => ApiProject.ProjectStatusEnum.PendingEnum,
            RunningStatus => ApiProject.ProjectStatusEnum.RunningEnum,
            FinishedStatus => ApiProject.ProjectStatusEnum.FinishedEnum,
            DelayedStatus => ApiProject.ProjectStatusEnum.DelayedEnum,
            ClosedStatus => ApiProject.ProjectStatusEnum.ClosedEnum,
            _ => ApiProject.ProjectStatusEnum.PendingEnum
        };
    }
}
