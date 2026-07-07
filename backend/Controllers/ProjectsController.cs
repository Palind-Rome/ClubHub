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
    /// Gets project applications, optionally filtered by club.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? clubId)
    {
        var query = _db.Projects.AsNoTracking();
        if (clubId is not null) query = query.Where(p => p.ClubId == clubId);

        var projects = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.ProjectId)
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

        return project is null ? NotFound("项目不存在。") : Ok(project);
    }

    /// <summary>
    /// Creates a project initiation application in pending status.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest? req)
    {
        if (req is null) return BadRequest("请求体不能为空。");

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

        return Conflict("项目编号生成冲突，请重试。");
    }

    /// <summary>
    /// Assigns or updates the project leader. The leader must be an active member of the project club.
    /// </summary>
    [HttpPut("{projectId:int}/leader")]
    public async Task<IActionResult> AssignLeader(int projectId, [FromBody] AssignProjectLeaderRequest? req)
    {
        if (req is null) return BadRequest("请求体不能为空。");

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound("项目不存在。");

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
        if (req is null) return BadRequest("请求体不能为空。");

        var normalizedStatus = ToReviewStatusValue(req.ProjectStatus);
        if (!ReviewStatuses.Contains(normalizedStatus))
        {
            return BadRequest("项目审核状态只能为 running 或 closed。");
        }

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound("项目不存在。");

        var reviewerExists = await ActiveUserExists(req.ReviewerUserId);
        if (!reviewerExists) return BadRequest("审核人不存在或账号不可用。");

        if (string.Equals(project.ProjectStatus, normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return Ok(ToProjectDto(project));
        }

        if (!string.Equals(project.ProjectStatus, PendingStatus, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("只有待审核项目可以变更审核结果。");
        }

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
        if (clubId <= 0) return BadRequest("所属社团不能为空。");
        if (string.IsNullOrWhiteSpace(projectName)) return BadRequest("项目名称不能为空。");
        if (projectName.Trim().Length > 100) return BadRequest("项目名称不能超过 100 个字符。");
        if (endDate is not null && endDate < startDate) return BadRequest("项目结束日期不能早于开始日期。");

        var clubExists = await _db.Clubs.AnyAsync(c => c.ClubId == clubId);
        if (!clubExists) return BadRequest("所属社团不存在。");

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
        if (leaderUserId <= 0) return BadRequest("项目负责人不能为空。");

        var userExists = await ActiveUserExists(leaderUserId);
        if (!userExists) return BadRequest("项目负责人不存在或账号不可用。");

        var isClubMember = await _db.ClubMembers.AnyAsync(m =>
            m.ClubId == clubId
            && m.UserId == leaderUserId
            && (m.MemberStatus == null || m.MemberStatus.ToLower() == ActiveMemberStatus));

        if (!isClubMember) return BadRequest("项目负责人必须是该社团的有效成员。");

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
        Description = project.Description!,
        LeaderUserId = project.LeaderUserId,
        StartDate = project.StartDate,
        EndDate = project.EndDate,
        ProjectStatus = ToProjectStatusEnum(project.ProjectStatus),
        ReviewerUserId = project.ReviewerUserId,
        ReviewComment = project.ReviewComment!,
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

    private static ApiProject.ProjectStatusEnum? ToProjectStatusEnum(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            PendingStatus => ApiProject.ProjectStatusEnum.PendingEnum,
            RunningStatus => ApiProject.ProjectStatusEnum.RunningEnum,
            "finished" => ApiProject.ProjectStatusEnum.FinishedEnum,
            "delayed" => ApiProject.ProjectStatusEnum.DelayedEnum,
            ClosedStatus => ApiProject.ProjectStatusEnum.ClosedEnum,
            _ => null
        };
    }
}
