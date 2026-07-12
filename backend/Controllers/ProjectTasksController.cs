using System.Data;
using ClubHub.Api.Data;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiError = Org.OpenAPITools.Models.ApiError;
using ApiProjectTask = Org.OpenAPITools.Models.ProjectTask;
using CreateProjectTaskRequest = Org.OpenAPITools.Models.CreateProjectTaskRequest;
using DbProjectTask = ClubHub.Api.Data.Entities.ProjectTask;
using UpdateProjectTaskProgressRequest = Org.OpenAPITools.Models.UpdateProjectTaskProgressRequest;

namespace ClubHub.Api.Controllers;

/// <summary>项目任务分配、查看和进度维护接口。</summary>
[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/tasks")]
public class ProjectTasksController : ControllerBase
{
    private const string PendingStatus = "pending";
    private const string InProgressStatus = "in_progress";
    private const string CompletedStatus = "completed";
    private const string DelayedStatus = "delayed";
    private const string RunningProjectStatus = "running";
    private const int MaxWriteRetries = 3;

    private readonly ClubHubDbContext _db;
    private readonly ProjectMembershipService _membershipService;

    public ProjectTasksController(ClubHubDbContext db, ProjectMembershipService membershipService)
    {
        _db = db;
        _membershipService = membershipService;
    }

    [HttpGet]
    public async Task<IActionResult> GetTasks(int projectId)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();

        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(item => item.ProjectId == projectId);
        if (project is null) return Error(404, "project_not_found", "项目不存在。");

        var isLeader = project.LeaderUserId == userId.Value;
        if (!isLeader && !await _membershipService.IsActiveMemberAsync(projectId, userId.Value))
        {
            return Error(403, "project_task_view_forbidden", "只有正在参与该项目的成员可以查看项目任务。");
        }

        var query = _db.ProjectTasks
            .AsNoTracking()
            .Include(item => item.AssigneeUser)
            .Where(item => item.ProjectId == projectId);
        if (!isLeader) query = query.Where(item => item.AssigneeUserId == userId.Value);

        var tasks = await query
            .OrderBy(item => item.TaskStatus == CompletedStatus ? 1 : 0)
            .ThenBy(item => item.DueDate)
            .ThenBy(item => item.TaskId)
            .ToListAsync();
        return Ok(tasks.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> CreateTask(int projectId, [FromBody] CreateProjectTaskRequest? request)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();
        if (request is null) return Error(400, "project_task_request_required", "请填写任务信息。");

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120)
        {
            return Error(400, "project_task_title_invalid", "任务标题不能为空且不能超过 120 个字符。");
        }
        if (request.Content?.Trim().Length > 4000)
        {
            return Error(400, "project_task_content_invalid", "任务说明不能超过 4000 个字符。");
        }
        var priority = ToPriorityValue(request.Priority);
        if (priority is null) return Error(400, "project_task_priority_invalid", "请选择有效的任务优先级。");

        var now = DateTime.UtcNow;
        var dueDate = NormalizeUtc(request.DueDate);
        if (dueDate <= now) return Error(400, "project_task_due_date_invalid", "任务截止时间必须晚于当前时间。");

        for (var attempt = 1; attempt <= MaxWriteRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var project = await _db.Projects.FirstOrDefaultAsync(item => item.ProjectId == projectId);
                if (project is null)
                {
                    await transaction.RollbackAsync();
                    return Error(404, "project_not_found", "项目不存在。");
                }
                if (!string.Equals(project.ProjectStatus, RunningProjectStatus, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return Error(409, "project_not_running", "只有执行中的项目可以创建任务。");
                }
                if (project.LeaderUserId != userId.Value)
                {
                    await transaction.RollbackAsync();
                    return Error(403, "project_task_create_forbidden", "只有项目负责人可以创建任务。");
                }
                if (!await _membershipService.IsActiveMemberAsync(projectId, request.AssigneeUserId))
                {
                    await transaction.RollbackAsync();
                    return Error(403, "project_task_assignee_not_member", "任务执行人必须是正在参与该项目的成员。");
                }
                if (project.EndDate is not null && dueDate > NormalizeUtc(project.EndDate.Value))
                {
                    await transaction.RollbackAsync();
                    return Error(400, "project_task_due_date_outside_project", "任务截止时间不能晚于项目结束日期。");
                }

                var task = new DbProjectTask
                {
                    TaskId = (await _db.ProjectTasks.MaxAsync(item => (int?)item.TaskId) ?? 0) + 1,
                    ProjectId = projectId,
                    AssigneeUserId = request.AssigneeUserId,
                    Title = title,
                    Content = NormalizeOptionalText(request.Content),
                    Priority = priority,
                    StartDate = now,
                    DueDate = dueDate,
                    Progress = 0,
                    TaskStatus = PendingStatus,
                    FinishDate = null,
                    DelayReason = null
                };
                _db.ProjectTasks.Add(task);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _db.Entry(task).Reference(item => item.AssigneeUser).LoadAsync();
                return StatusCode(StatusCodes.Status201Created, ToDto(task));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ProjectMembershipService.IsRetryableWriteConflict(ex))
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
                if (attempt == MaxWriteRetries) break;
            }
        }

        return Error(409, "project_task_write_conflict", "任务创建发生并发冲突，请稍后重试。");
    }

    [HttpPatch("{taskId:int}/progress")]
    public async Task<IActionResult> UpdateProgress(
        int projectId,
        int taskId,
        [FromBody] UpdateProjectTaskProgressRequest? request)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();
        if (request is null) return Error(400, "project_task_request_required", "请填写任务进度信息。");

        var task = await _db.ProjectTasks
            .Include(item => item.AssigneeUser)
            .FirstOrDefaultAsync(item => item.TaskId == taskId && item.ProjectId == projectId);
        if (task is null) return Error(404, "project_task_not_found", "项目任务不存在。");
        if (task.AssigneeUserId != userId.Value || !await _membershipService.IsActiveMemberAsync(projectId, userId.Value))
        {
            return Error(403, "project_task_update_forbidden", "只有仍在参与项目的任务执行人可以更新自己的任务。");
        }

        var status = ToStatusValue(request.TaskStatus);
        if (status is null) return Error(400, "project_task_status_invalid", "请选择有效的任务状态。");
        if (string.Equals(task.TaskStatus, CompletedStatus, StringComparison.OrdinalIgnoreCase) && status != CompletedStatus)
        {
            return Error(409, "project_task_completed", "已完成任务不能重新打开。");
        }

        var validation = ValidateProgressUpdate(task, request, status);
        if (validation is not null) return validation;

        task.Progress = request.Progress;
        task.TaskStatus = status;
        task.FinishDate = status == CompletedStatus && request.FinishDate is not null
            ? NormalizeUtc(request.FinishDate.Value)
            : null;
        task.DelayReason = status == CompletedStatus ? null : NormalizeOptionalText(request.DelayReason);
        await _db.SaveChangesAsync();
        return Ok(ToDto(task));
    }

    private IActionResult? ValidateProgressUpdate(
        DbProjectTask task,
        UpdateProjectTaskProgressRequest request,
        string status)
    {
        var startDate = task.StartDate is null ? (DateTime?)null : NormalizeUtc(task.StartDate.Value);
        var dueDate = task.DueDate is null ? (DateTime?)null : NormalizeUtc(task.DueDate.Value);
        var finishDate = request.FinishDate is null ? (DateTime?)null : NormalizeUtc(request.FinishDate.Value);
        var delayReason = NormalizeOptionalText(request.DelayReason);
        var now = DateTime.UtcNow;

        if (request.Progress is < 0 or > 100) return Error(400, "project_task_progress_invalid", "任务进度必须在 0% 到 100% 之间。");
        if (status == PendingStatus && request.Progress != 0) return Error(400, "project_task_pending_progress_invalid", "待开始任务的进度必须为 0%。");
        if (status == InProgressStatus && (request.Progress <= 0 || request.Progress >= 100)) return Error(400, "project_task_in_progress_invalid", "进行中任务的进度必须在 1% 到 99% 之间。");
        if (status == DelayedStatus && request.Progress >= 100) return Error(400, "project_task_delayed_progress_invalid", "延期任务的进度必须低于 100%。");
        if (status == CompletedStatus && request.Progress != 100) return Error(400, "project_task_completed_progress_invalid", "已完成任务的进度必须为 100%。");
        if (status == CompletedStatus && finishDate is null) return Error(400, "project_task_finish_date_required", "任务完成时必须填写完成日期。");
        if (status != CompletedStatus && finishDate is not null) return Error(400, "project_task_finish_date_not_allowed", "未完成任务不能填写完成日期。");
        if (finishDate is not null && finishDate > now) return Error(400, "project_task_finish_date_future", "完成日期不能晚于当前时间。");
        if (finishDate is not null && startDate is not null && finishDate < startDate) return Error(400, "project_task_finish_date_before_start", "完成日期不能早于任务开始日期。");

        var overdueUnfinished = dueDate is not null && dueDate < now && status != CompletedStatus;
        if ((status == DelayedStatus || overdueUnfinished) && delayReason is null)
        {
            return Error(400, "project_task_delay_reason_required", "延期或逾期未完成的任务必须填写延期原因。");
        }
        if (status == CompletedStatus && delayReason is not null)
        {
            return Error(400, "project_task_delay_reason_not_allowed", "已完成任务不能保留延期原因。");
        }
        return null;
    }

    private IActionResult AuthenticationRequired() => Error(401, "authentication_required", "登录状态已失效，请重新登录。");

    private ObjectResult Error(int statusCode, string code, string message) => StatusCode(statusCode, new ApiError { Code = code, Message = message });

    private static ApiProjectTask ToDto(DbProjectTask task) => new()
    {
        Id = task.TaskId,
        ProjectId = task.ProjectId ?? 0,
        AssigneeUserId = task.AssigneeUserId ?? 0,
        AssigneeName = task.AssigneeUser?.RealName?.Trim() is { Length: > 0 } name ? name : task.AssigneeUser?.Username?.Trim() ?? "未知成员",
        Title = task.Title ?? "未命名任务",
        Content = task.Content,
        Priority = ToApiPriority(task.Priority),
        StartDate = task.StartDate is null ? DateTime.UnixEpoch : NormalizeUtc(task.StartDate.Value),
        DueDate = task.DueDate is null ? DateTime.UnixEpoch : NormalizeUtc(task.DueDate.Value),
        FinishDate = task.FinishDate is null ? null : NormalizeUtc(task.FinishDate.Value),
        Progress = task.Progress ?? 0,
        TaskStatus = ToApiStatus(task.TaskStatus),
        DelayReason = task.DelayReason
    };

    private static string? ToPriorityValue(CreateProjectTaskRequest.PriorityEnum value) => value switch
    {
        CreateProjectTaskRequest.PriorityEnum.LowEnum => "low",
        CreateProjectTaskRequest.PriorityEnum.MediumEnum => "medium",
        CreateProjectTaskRequest.PriorityEnum.HighEnum => "high",
        CreateProjectTaskRequest.PriorityEnum.UrgentEnum => "urgent",
        _ => null
    };

    private static string? ToStatusValue(UpdateProjectTaskProgressRequest.TaskStatusEnum value) => value switch
    {
        UpdateProjectTaskProgressRequest.TaskStatusEnum.PendingEnum => PendingStatus,
        UpdateProjectTaskProgressRequest.TaskStatusEnum.InProgressEnum => InProgressStatus,
        UpdateProjectTaskProgressRequest.TaskStatusEnum.CompletedEnum => CompletedStatus,
        UpdateProjectTaskProgressRequest.TaskStatusEnum.DelayedEnum => DelayedStatus,
        _ => null
    };

    private static ApiProjectTask.PriorityEnum ToApiPriority(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => ApiProjectTask.PriorityEnum.LowEnum,
        "high" => ApiProjectTask.PriorityEnum.HighEnum,
        "urgent" => ApiProjectTask.PriorityEnum.UrgentEnum,
        _ => ApiProjectTask.PriorityEnum.MediumEnum
    };

    private static ApiProjectTask.TaskStatusEnum ToApiStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        InProgressStatus => ApiProjectTask.TaskStatusEnum.InProgressEnum,
        CompletedStatus => ApiProjectTask.TaskStatusEnum.CompletedEnum,
        DelayedStatus => ApiProjectTask.TaskStatusEnum.DelayedEnum,
        _ => ApiProjectTask.TaskStatusEnum.PendingEnum
    };

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string? NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
