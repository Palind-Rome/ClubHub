using ClubHub.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiProject = Org.OpenAPITools.Models.Project;
using AssignProjectLeaderRequest = Org.OpenAPITools.Models.AssignProjectLeaderRequest;
using CreateProjectRequest = Org.OpenAPITools.Models.CreateProjectRequest;
using DbProject = ClubHub.Api.Data.Entities.Project;
using ReviewProjectRequest = Org.OpenAPITools.Models.ReviewProjectRequest;

namespace ClubHub.Api.Controllers;

/// <summary>
/// Project initiation and leader assignment APIs for requirement 1.10.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private const int MaxCreateRetries = 3;
    private const string PendingStatus = "pending";
    private const string RunningStatus = "running";
    private const string ClosedStatus = "closed";
    private const string NormalAccountStatus = "normal";
    private const string ActiveMemberStatus = "active";

    private static readonly HashSet<string> ReviewStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        RunningStatus,
        ClosedStatus
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

        for (var attempt = 1; attempt <= MaxCreateRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
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
    /// Assigns or updates the project leader. The leader must be an active member of the project club.
    /// </summary>
    [HttpPut("{projectId:int}/leader")]
    public async Task<IActionResult> AssignLeader(int projectId, [FromBody] AssignProjectLeaderRequest? req)
    {
        if (req is null) return BadRequest("Request body is required.");

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound("Project does not exist.");

        var validation = await ValidateProjectLeader(project.ClubId, req.LeaderUserId);
        if (validation is not null) return validation;

        project.LeaderUserId = req.LeaderUserId;
        await _db.SaveChangesAsync();

        return Ok(ToProjectDto(project));
    }

    /// <summary>
    /// Reviews a project initiation application. Requirement 1.10 only allows running or closed.
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

        if (string.Equals(project.ProjectStatus, normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return Ok(ToProjectDto(project));
        }

        if (!string.Equals(project.ProjectStatus, PendingStatus, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only pending projects can be reviewed.");
        }

        var reviewerExists = await ActiveUserExists(req.ReviewerUserId);
        if (!reviewerExists) return BadRequest("Reviewer does not exist or is disabled.");

        project.ProjectStatus = normalizedStatus;
        project.ReviewerUserId = req.ReviewerUserId;
        project.ReviewComment = NormalizeOptionalText(req.ReviewComment);

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
        // Keep all create-time checks together so later project fields can be added without scattering validation.
        if (clubId <= 0) return BadRequest("Club id must be greater than 0.");
        if (string.IsNullOrWhiteSpace(projectName)) return BadRequest("Project name is required.");
        if (projectName.Trim().Length > 100) return BadRequest("Project name cannot exceed 100 characters.");
        if (endDate is not null && endDate < startDate) return BadRequest("Project end date cannot be earlier than start date.");

        var clubExists = await _db.Clubs.AnyAsync(c => c.ClubId == clubId);
        if (!clubExists) return BadRequest("Club does not exist.");

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

        var isClubMember = await _db.ClubMembers.AnyAsync(m =>
            m.ClubId == clubId
            && m.UserId == leaderUserId
            && (m.MemberStatus == null || m.MemberStatus.ToLower() == ActiveMemberStatus));

        if (!isClubMember) return BadRequest("Project leader must be an active member of the club.");

        return null;
    }

    /// <summary>
    /// Checks whether a user exists and is not disabled. Null status is allowed for early seed data.
    /// </summary>
    private Task<bool> ActiveUserExists(int userId)
    {
        return _db.Users.AnyAsync(u =>
            u.UserId == userId
            && (u.AccountStatus == null || u.AccountStatus.ToLower() == NormalAccountStatus));
    }

    private async Task<int> GetNextProjectId()
    {
        // The current course schema has no identity/sequence for PROJECTS.
        // Creation wraps max(id)+1 in a transaction and retries on insert collision.
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

    private static ApiProject ToProjectDto(DbProject project) => new()
    {
        Id = project.ProjectId,
        ClubId = project.ClubId,
        ProjectName = project.ProjectName,
        Description = project.Description,
        LeaderUserId = project.LeaderUserId,
        StartDate = project.StartDate ?? default,
        EndDate = project.EndDate,
        ProjectStatus = ToProjectStatusEnum(project.ProjectStatus),
        ReviewerUserId = project.ReviewerUserId,
        ReviewComment = project.ReviewComment,
        CreatedAt = project.CreatedAt
    };

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
            "finished" => ApiProject.ProjectStatusEnum.FinishedEnum,
            "delayed" => ApiProject.ProjectStatusEnum.DelayedEnum,
            ClosedStatus => ApiProject.ProjectStatusEnum.ClosedEnum,
            _ => ApiProject.ProjectStatusEnum.PendingEnum
        };
    }
}
