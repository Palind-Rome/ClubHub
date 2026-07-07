using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Controllers;

/// <summary>
/// 项目立项申请与负责人分配接口。对应数据库设计文档 1.10 功能点。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    // 项目状态取值来自数据库设计文档 PROJECTS.project_status。
    private const string PendingStatus = "pending";
    private const string RunningStatus = "running";
    private const string ClosedStatus = "closed";

    // 用户与社团成员状态分别来自 USERS.account_status 和 CLUB_MEMBERS.member_status。
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
    /// 获取项目列表，可按社团编号筛选。
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
    /// 获取单个项目立项申请详情。
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
    /// 提交项目立项申请，初始状态固定为 pending，等待审核后进入执行或关闭。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest? req)
    {
        if (req is null) return BadRequest("请求体不能为空。");

        var validation = await ValidateProjectInput(req.ClubId, req.ProjectName, req.LeaderUserId, req.StartDate, req.EndDate);
        if (validation is not null) return validation;

        var project = new Project
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
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { projectId = project.ProjectId }, ToProjectDto(project));
    }

    /// <summary>
    /// 分配或调整项目负责人；负责人必须是该社团的有效成员。
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
    /// 审核项目立项申请。1.10 只允许审核为 running 或 closed。
    /// </summary>
    [HttpPost("{projectId:int}/review")]
    public async Task<IActionResult> Review(int projectId, [FromBody] ReviewProjectRequest? req)
    {
        if (req is null) return BadRequest("请求体不能为空。");
        if (string.IsNullOrWhiteSpace(req.ProjectStatus)) return BadRequest("项目审核状态不能为空。");

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound("项目不存在。");

        var normalizedStatus = req.ProjectStatus.Trim().ToLowerInvariant();
        if (!ReviewStatuses.Contains(normalizedStatus))
        {
            return BadRequest("项目审核状态只能为 running 或 closed。");
        }

        var reviewerExists = await ActiveUserExists(req.ReviewerUserId);
        if (!reviewerExists) return BadRequest("审核人不存在或账号不可用。");

        // 立项审核是从 pending 进入执行或关闭；保留幂等重审能力，避免重复点击造成异常。
        if (!string.Equals(project.ProjectStatus, PendingStatus, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(project.ProjectStatus, normalizedStatus, StringComparison.OrdinalIgnoreCase))
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
        DateTime? startDate,
        DateTime? endDate)
    {
        // 立项申请的字段校验集中在这里，避免 Create 中混入过多分支判断。
        if (clubId <= 0) return BadRequest("所属社团不能为空。");
        if (string.IsNullOrWhiteSpace(projectName)) return BadRequest("项目名称不能为空。");
        if (projectName.Trim().Length > 100) return BadRequest("项目名称不能超过 100 个字符。");
        if (startDate is null) return BadRequest("项目开始日期不能为空。");
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
    /// 校验项目负责人是否为可用用户，并且属于项目所在社团。
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
    /// 判断用户是否存在且账号未禁用。空状态兼容早期种子数据。
    /// </summary>
    private Task<bool> ActiveUserExists(int userId)
    {
        return _db.Users.AnyAsync(u =>
            u.UserId == userId
            && (u.AccountStatus == null || u.AccountStatus.ToLower() == NormalAccountStatus));
    }

    private async Task<int> GetNextProjectId()
    {
        // 当前 schema 未使用 identity；沿用仓库已有 Controller 的 max(id)+1 方式，保持实现风格一致。
        var maxId = await _db.Projects.MaxAsync(p => (int?)p.ProjectId) ?? 0;
        return maxId + 1;
    }

    /// <summary>
    /// 将空白字符串统一归一化为空值，避免数据库中出现无意义空白内容。
    /// </summary>
    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ProjectDto ToProjectDto(Project project) => new(
        project.ProjectId,
        project.ClubId,
        project.ProjectName,
        project.Description,
        project.LeaderUserId,
        project.StartDate,
        project.EndDate,
        project.ProjectStatus,
        project.ReviewerUserId,
        project.ReviewComment,
        project.CreatedAt
    );
}

/// <summary>
/// 项目立项申请响应模型，与 PROJECTS 表核心字段对应。
/// </summary>
public record ProjectDto(
    int Id,
    int ClubId,
    string ProjectName,
    string? Description,
    int? LeaderUserId,
    DateTime? StartDate,
    DateTime? EndDate,
    string? ProjectStatus,
    int? ReviewerUserId,
    string? ReviewComment,
    DateTime CreatedAt
);

/// <summary>
/// 创建项目立项申请的请求模型。
/// </summary>
public class CreateProjectRequest
{
    public int ClubId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? LeaderUserId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

/// <summary>
/// 分配或调整项目负责人的请求模型。
/// </summary>
public class AssignProjectLeaderRequest
{
    public int LeaderUserId { get; set; }
}

/// <summary>
/// 审核项目立项申请的请求模型。
/// </summary>
public class ReviewProjectRequest
{
    public string ProjectStatus { get; set; } = string.Empty;
    public int ReviewerUserId { get; set; }
    public string? ReviewComment { get; set; }
}
