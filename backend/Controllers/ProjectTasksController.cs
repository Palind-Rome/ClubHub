using System.Data;
using ClubHub.Api.Data;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiError = Org.OpenAPITools.Models.ApiError;
using ApiProjectTask = Org.OpenAPITools.Models.ProjectTask;
using ApiProjectTaskAssignee = Org.OpenAPITools.Models.ProjectTaskAssignee;
using ApiProjectTaskProgressReport = Org.OpenAPITools.Models.ProjectTaskProgressReport;
using CreateProjectTaskRequest = Org.OpenAPITools.Models.CreateProjectTaskRequest;
using DbProject = ClubHub.Api.Data.Entities.Project;
using DbProjectTask = ClubHub.Api.Data.Entities.ProjectTask;
using DbProjectTaskProgressReport = ClubHub.Api.Data.Entities.ProjectTaskProgressReport;
using DbRole = ClubHub.Api.Data.Entities.Role;
using DbUser = ClubHub.Api.Data.Entities.User;
using ReviewProjectTaskDeliverableRequest = Org.OpenAPITools.Models.ReviewProjectTaskDeliverableRequest;
using SubmitProjectTaskDeliverableRequest = Org.OpenAPITools.Models.SubmitProjectTaskDeliverableRequest;
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
    private const string DelayedProjectStatus = "delayed";
    private const string FinishedProjectStatus = "finished";
    private const string DeliverablePendingStatus = "pending";
    private const string DeliverableApprovedStatus = "approved";
    private const string DeliverableRejectedStatus = "rejected";
    private const int MaxWriteRetries = 3;

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
    private readonly ProjectMembershipService _membershipService;

    public ProjectTasksController(ClubHubDbContext db, ProjectMembershipService membershipService)
    {
        _db = db;
        _membershipService = membershipService;
    }

    [HttpGet]
    public async Task<IActionResult> GetTasks(int projectId, [FromQuery] bool completedOnly = false)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();

        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(item => item.ProjectId == projectId);
        if (project is null) return Error(404, "project_not_found", "项目不存在。");

        await _membershipService.EnsureLeaderMembershipAsync(project);

        var canViewAll = await CanViewAllTasksAsync(project, userId.Value);
        if (!canViewAll && !await _membershipService.IsActiveMemberAsync(projectId, userId.Value))
        {
            return Error(403, "project_task_view_forbidden", "只有正在参与该项目的成员可以查看项目任务。");
        }

        var query = _db.ProjectTasks
            .AsNoTracking()
            .Include(item => item.Assignees)
                .ThenInclude(assignee => assignee.User)
            .Include(item => item.DeliverableSubmitter)
            .Include(item => item.ReviewerUser)
            .Where(item => item.ProjectId == projectId && (completedOnly
                ? item.TaskStatus == CompletedStatus
                : item.TaskStatus != CompletedStatus));
        if (!canViewAll) query = query.Where(item => item.Assignees.Any(assignee => assignee.UserId == userId.Value));

        var tasks = await query
            .OrderBy(item => item.TaskStatus == CompletedStatus ? 1 : 0)
            .ThenBy(item => item.DueDate)
            .ThenBy(item => item.TaskId)
            .ToListAsync();
        return Ok(tasks.Select(ToDto).ToList());
    }

    [HttpDelete("{taskId:int}")]
    public async Task<IActionResult> DeleteTask(int projectId, int taskId)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var project = await _db.Projects.FirstOrDefaultAsync(item => item.ProjectId == projectId);
        if (project is null) return Error(404, "project_not_found", "项目不存在。");
        if (project.LeaderUserId != userId.Value)
        {
            await transaction.RollbackAsync();
            return Error(403, "project_task_delete_forbidden", "只有项目负责人可以删除任务。");
        }

        var task = await _db.ProjectTasks.FirstOrDefaultAsync(item => item.TaskId == taskId && item.ProjectId == projectId);
        if (task is null)
        {
            await transaction.RollbackAsync();
            return Error(404, "project_task_not_found", "项目任务不存在。");
        }

        _db.ProjectTaskProgressReports.RemoveRange(_db.ProjectTaskProgressReports.Where(item => item.TaskId == taskId));
        _db.ProjectTaskAssignees.RemoveRange(_db.ProjectTaskAssignees.Where(item => item.TaskId == taskId));
        _db.ProjectTasks.Remove(task);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
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
        var assigneeUserIds = request.AssigneeUserIds?.Distinct().ToList() ?? [];
        if (assigneeUserIds.Count == 0 || assigneeUserIds.Any(candidate => candidate <= 0))
        {
            return Error(400, "project_task_assignees_invalid", "请至少选择一名有效的任务执行人。");
        }

        var now = DateTime.UtcNow;
        var dueDate = NormalizeUtc(request.DueDate);
        if (dueDate <= now) return Error(400, "project_task_due_date_invalid", "任务截止时间必须晚于当前时间。");

        var projectForMembership = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId);
        if (projectForMembership is not null)
        {
            await _membershipService.EnsureLeaderMembershipAsync(projectForMembership);
        }

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
                foreach (var assigneeUserId in assigneeUserIds)
                {
                    if (!await _membershipService.IsActiveMemberAsync(projectId, assigneeUserId))
                    {
                        await transaction.RollbackAsync();
                        return Error(403, "project_task_assignee_not_member", "每位任务执行人都必须是正在参与该项目的成员。");
                    }
                }
                if (project.EndDate is not null && dueDate > NormalizeUtc(project.EndDate.Value))
                {
                    await transaction.RollbackAsync();
                    return Error(400, "project_task_due_date_outside_project", "任务截止时间不能晚于项目结束日期。");
                }

                var task = new DbProjectTask
                {
                    ProjectId = projectId,
                    AssigneeUserId = assigneeUserIds[0],
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
                foreach (var assigneeUserId in assigneeUserIds)
                {
                    task.Assignees.Add(new()
                    {
                        UserId = assigneeUserId,
                        AssignedAt = now
                    });
                }
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _db.Entry(task).Collection(item => item.Assignees).Query().Include(item => item.User).LoadAsync();
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

        var status = ToStatusValue(request.TaskStatus);
        if (status is null) return Error(400, "project_task_status_invalid", "请选择有效的任务状态。");

        for (var attempt = 1; attempt <= MaxWriteRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var task = await _db.ProjectTasks
                    .Include(item => item.Assignees)
                        .ThenInclude(assignee => assignee.User)
                    .FirstOrDefaultAsync(item => item.TaskId == taskId && item.ProjectId == projectId);
                if (task is null)
                {
                    await transaction.RollbackAsync();
                    return Error(404, "project_task_not_found", "项目任务不存在。");
                }
                if (!task.Assignees.Any(assignee => assignee.UserId == userId.Value) || !await _membershipService.IsActiveMemberAsync(projectId, userId.Value))
                {
                    await transaction.RollbackAsync();
                    return Error(403, "project_task_update_forbidden", "只有仍在参与项目的任务执行人可以更新该任务。");
                }
                if (string.Equals(task.TaskStatus, CompletedStatus, StringComparison.OrdinalIgnoreCase) && status != CompletedStatus)
                {
                    await transaction.RollbackAsync();
                    return Error(409, "project_task_completed", "已完成任务不能重新打开。");
                }

                var now = DateTime.UtcNow;
                var validation = ValidateProgressUpdate(task, request, status, now);
                if (validation is not null)
                {
                    await transaction.RollbackAsync();
                    return validation;
                }

                task.Progress = request.Progress;
                task.TaskStatus = status;
                task.FinishDate = status == CompletedStatus ? now : null;
                task.DelayReason = status == CompletedStatus ? null : NormalizeOptionalText(request.DelayReason);
                _db.ProjectTaskProgressReports.Add(new()
                {
                    TaskId = task.TaskId,
                    ReporterUserId = userId.Value,
                    Progress = request.Progress,
                    TaskStatus = status,
                    ReportContent = NormalizeOptionalText(request.ReportContent),
                    DelayReason = status == DelayedStatus ? NormalizeOptionalText(request.DelayReason) : null,
                    SubmittedAt = now
                });
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok(ToDto(task));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ProjectMembershipService.IsRetryableWriteConflict(ex))
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
                if (attempt == MaxWriteRetries) break;
            }
        }

        return Error(409, "project_task_write_conflict", "任务进度提交发生并发冲突，请稍后重试。");
    }

    [HttpGet("{taskId:int}/progress-reports")]
    public async Task<IActionResult> GetProgressReports(int projectId, int taskId)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();

        var task = await _db.ProjectTasks
            .AsNoTracking()
            .Include(item => item.Assignees)
            .FirstOrDefaultAsync(item => item.TaskId == taskId && item.ProjectId == projectId);
        if (task is null) return Error(404, "project_task_not_found", "项目任务不存在。");

        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(item => item.ProjectId == projectId);
        if (project is null) return Error(404, "project_not_found", "项目不存在。");
        var canViewAll = await CanViewAllTasksAsync(project, userId.Value);
        if (!canViewAll && (!task.Assignees.Any(assignee => assignee.UserId == userId.Value)
            || !await _membershipService.IsActiveMemberAsync(projectId, userId.Value)))
        {
            return Error(403, "project_task_report_view_forbidden", "只有项目负责人或仍在参与项目的任务执行人可以查看任务进度记录。");
        }

        var reports = await _db.ProjectTaskProgressReports
            .AsNoTracking()
            .Include(item => item.Reporter)
            .Where(item => item.TaskId == taskId)
            .OrderByDescending(item => item.SubmittedAt)
            .ThenByDescending(item => item.TaskProgressReportId)
            .ToListAsync();
        return Ok(reports.Select(ToReportDto).ToList());
    }

    [HttpPost("{taskId:int}/deliverable")]
    public async Task<IActionResult> SubmitDeliverable(
        int projectId,
        int taskId,
        [FromBody] SubmitProjectTaskDeliverableRequest? request)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();
        if (request is null) return Error(400, "project_task_deliverable_request_required", "请填写任务成果信息。");

        var title = request.DeliverableTitle?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 100)
        {
            return Error(400, "project_task_deliverable_title_invalid", "成果标题不能为空且不能超过 100 个字符。");
        }
        if (request.DeliverableUrl?.Trim().Length > 255)
        {
            return Error(400, "project_task_deliverable_url_invalid", "成果链接不能超过 255 个字符。");
        }
        var deliverableUrl = NormalizeOptionalText(request.DeliverableUrl);
        if (deliverableUrl is not null && !IsAllowedHttpUrl(deliverableUrl))
        {
            return Error(400, "project_task_deliverable_url_invalid", "成果链接必须是 http 或 https 地址。");
        }
        if (request.DeliverableDesc?.Trim().Length > 4000)
        {
            return Error(400, "project_task_deliverable_desc_invalid", "成果说明不能超过 4000 个字符。");
        }

        for (var attempt = 1; attempt <= MaxWriteRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var task = await _db.ProjectTasks
                    .Include(item => item.Project)
                    .Include(item => item.Assignees)
                        .ThenInclude(assignee => assignee.User)
                    .Include(item => item.DeliverableSubmitter)
                    .Include(item => item.ReviewerUser)
                    .FirstOrDefaultAsync(item => item.TaskId == taskId && item.ProjectId == projectId);
                if (task is null)
                {
                    await transaction.RollbackAsync();
                    return Error(404, "project_task_not_found", "项目任务不存在。");
                }
                if (task.Project is null)
                {
                    await transaction.RollbackAsync();
                    return Error(404, "project_not_found", "项目不存在。");
                }
                if (!IsProjectAcceptingDeliverables(task.Project.ProjectStatus))
                {
                    await transaction.RollbackAsync();
                    return Error(409, "project_task_deliverable_project_status_invalid", "只有执行中或已延期的项目可以提交任务成果。");
                }
                if (string.Equals(task.DeliverableStatus, DeliverableApprovedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return Error(409, "project_task_deliverable_approved", "成果已审核通过，不能重复提交。");
                }
                if (string.Equals(task.DeliverableStatus, DeliverablePendingStatus, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return Error(409, "project_task_deliverable_pending", "成果正在审核中，请等待审核结果后再重新提交。");
                }

                var isLeader = task.Project.LeaderUserId == userId.Value;
                var isAssignee = task.Assignees.Any(assignee => assignee.UserId == userId.Value) &&
                    await _membershipService.IsActiveMemberAsync(projectId, userId.Value);
                if (!isLeader && !isAssignee)
                {
                    await transaction.RollbackAsync();
                    return Error(403, "project_task_deliverable_submit_forbidden", "只有项目负责人或仍在参与项目的任务执行人可以提交任务成果。");
                }

                var now = DateTime.UtcNow;
                task.DeliverableTitle = title;
                task.DeliverableDescription = NormalizeOptionalText(request.DeliverableDesc);
                task.DeliverableUrl = deliverableUrl;
                task.DeliverableStatus = DeliverablePendingStatus;
                task.DeliverableSubmitterId = userId.Value;
                task.DeliverableSubmittedAt = now;
                task.ReviewerUserId = null;
                task.ReviewComment = null;
                task.DeliverableReviewedAt = null;
                task.Progress = Math.Max(task.Progress ?? 0, 90);
                if (string.Equals(task.TaskStatus, PendingStatus, StringComparison.OrdinalIgnoreCase))
                {
                    task.TaskStatus = InProgressStatus;
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                await _db.Entry(task).Reference(item => item.DeliverableSubmitter).LoadAsync();
                await _db.Entry(task).Reference(item => item.ReviewerUser).LoadAsync();
                return Ok(ToDto(task));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ProjectMembershipService.IsRetryableWriteConflict(ex))
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
                if (attempt == MaxWriteRetries) break;
            }
        }

        return Error(409, "project_task_deliverable_write_conflict", "任务成果提交发生并发冲突，请稍后重试。");
    }

    [HttpPost("{taskId:int}/deliverable/review")]
    public async Task<IActionResult> ReviewDeliverable(
        int projectId,
        int taskId,
        [FromBody] ReviewProjectTaskDeliverableRequest? request)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();
        if (request is null) return Error(400, "project_task_deliverable_review_required", "请选择任务成果审核结果。");
        if (!request.Approved && string.IsNullOrWhiteSpace(request.ReviewComment))
        {
            return Error(400, "project_task_deliverable_reject_comment_required", "驳回任务成果时必须填写审核意见。");
        }

        var reviewerAccess = await EnsureActiveUserAsync(userId.Value);
        if (reviewerAccess.Result is not null) return reviewerAccess.Result;

        for (var attempt = 1; attempt <= MaxWriteRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var task = await _db.ProjectTasks
                    .Include(item => item.Project)
                    .Include(item => item.Assignees)
                        .ThenInclude(assignee => assignee.User)
                    .Include(item => item.DeliverableSubmitter)
                    .Include(item => item.ReviewerUser)
                    .FirstOrDefaultAsync(item => item.TaskId == taskId && item.ProjectId == projectId);
                if (task is null)
                {
                    await transaction.RollbackAsync();
                    return Error(404, "project_task_not_found", "项目任务不存在。");
                }
                if (task.Project is null)
                {
                    await transaction.RollbackAsync();
                    return Error(404, "project_not_found", "项目不存在。");
                }
                if (!IsProjectAcceptingDeliverables(task.Project.ProjectStatus))
                {
                    await transaction.RollbackAsync();
                    return Error(409, "project_task_deliverable_review_project_status_invalid", "只有执行中或已延期的项目可以审核任务成果。");
                }
                if (!await CanReviewDeliverableAsync(reviewerAccess.User!, task.Project.ClubId))
                {
                    await transaction.RollbackAsync();
                    return Error(403, "project_task_deliverable_review_forbidden", "只有本社团指导老师或校级社团管理员可以审核任务成果。");
                }
                if (!string.Equals(task.DeliverableStatus, DeliverablePendingStatus, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return Error(400, "project_task_deliverable_not_pending", "只有待审核的任务成果可以保存审核结果。");
                }

                var now = DateTime.UtcNow;
                task.ReviewerUserId = userId.Value;
                task.ReviewComment = NormalizeOptionalText(request.ReviewComment);
                task.DeliverableReviewedAt = now;
                if (request.Approved)
                {
                    task.DeliverableStatus = DeliverableApprovedStatus;
                    task.TaskStatus = CompletedStatus;
                    task.Progress = 100;
                    task.FinishDate = now;
                    task.DelayReason = null;
                }
                else
                {
                    task.DeliverableStatus = DeliverableRejectedStatus;
                    task.TaskStatus = InProgressStatus;
                    task.Progress = Math.Min(task.Progress ?? 0, 90);
                    task.FinishDate = null;
                }

                await RefreshProjectStatusFromDeliverablesAsync(task.Project, task, now);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                await _db.Entry(task).Reference(item => item.ReviewerUser).LoadAsync();
                return Ok(ToDto(task));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ProjectMembershipService.IsRetryableWriteConflict(ex))
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
                if (attempt == MaxWriteRetries) break;
            }
        }

        return Error(409, "project_task_deliverable_review_write_conflict", "任务成果审核发生并发冲突，请稍后重试。");
    }

    private IActionResult? ValidateProgressUpdate(
        DbProjectTask task,
        UpdateProjectTaskProgressRequest request,
        string status,
        DateTime now)
    {
        var startDate = task.StartDate is null ? (DateTime?)null : NormalizeUtc(task.StartDate.Value);
        var delayReason = NormalizeOptionalText(request.DelayReason);
        var reportContent = NormalizeOptionalText(request.ReportContent);

        if (request.Progress is < 0 or > 100) return Error(400, "project_task_progress_invalid", "任务进度必须在 0% 到 100% 之间。");
        if (status == PendingStatus && request.Progress != 0) return Error(400, "project_task_pending_progress_invalid", "待开始任务的进度必须为 0%。");
        if (status == InProgressStatus && (request.Progress <= 0 || request.Progress >= 100)) return Error(400, "project_task_in_progress_invalid", "进行中任务的进度必须在 1% 到 99% 之间。");
        if (status == DelayedStatus && request.Progress >= 100) return Error(400, "project_task_delayed_progress_invalid", "延期任务的进度必须低于 100%。");
        if (status == CompletedStatus && request.Progress != 100) return Error(400, "project_task_completed_progress_invalid", "已完成任务的进度必须为 100%。");
        if (status == CompletedStatus && startDate is not null && now < startDate) return Error(400, "project_task_finish_date_before_start", "任务尚未开始，不能完成。");

        if (status == DelayedStatus && delayReason is null)
        {
            return Error(400, "project_task_delay_reason_required", "已延期任务必须填写延期原因。");
        }
        if (status != DelayedStatus && status != CompletedStatus && delayReason is not null) return Error(400, "project_task_delay_reason_not_allowed", "待开始或进行中任务无需填写延期原因。");
        if (status == CompletedStatus && delayReason is not null)
        {
            return Error(400, "project_task_delay_reason_not_allowed", "已完成任务不能保留延期原因。");
        }
        if (status == InProgressStatus && reportContent is null)
        {
            return Error(400, "project_task_report_required", "进行中任务必须填写本次进度汇报。");
        }
        return null;
    }

    private IActionResult AuthenticationRequired() => Error(401, "authentication_required", "登录状态已失效，请重新登录。");

    private ObjectResult Error(int statusCode, string code, string message) => StatusCode(statusCode, new ApiError { Code = code, Message = message });

    private static ApiProjectTask ToDto(DbProjectTask task) => new()
    {
        Id = task.TaskId,
        ProjectId = task.ProjectId ?? 0,
        Assignees = task.Assignees.OrderBy(assignee => assignee.TaskAssigneeId).Select(assignee => new ApiProjectTaskAssignee
        {
            UserId = assignee.UserId,
            DisplayName = assignee.User?.RealName?.Trim() is { Length: > 0 } name ? name : assignee.User?.Username?.Trim() ?? "未知成员"
        }).ToList(),
        Title = task.Title ?? "未命名任务",
        Content = task.Content,
        Priority = ToApiPriority(task.Priority),
        StartDate = task.StartDate is null ? DateTime.UnixEpoch : NormalizeUtc(task.StartDate.Value),
        DueDate = task.DueDate is null ? DateTime.UnixEpoch : NormalizeUtc(task.DueDate.Value),
        FinishDate = task.FinishDate is null ? null : NormalizeUtc(task.FinishDate.Value),
        Progress = task.Progress ?? 0,
        TaskStatus = ToApiStatus(task.TaskStatus),
        DelayReason = task.DelayReason,
        DeliverableTitle = task.DeliverableTitle,
        DeliverableDesc = task.DeliverableDescription,
        DeliverableUrl = task.DeliverableUrl,
        DeliverableStatus = ToApiDeliverableStatus(task.DeliverableStatus),
        ReviewerUserId = task.ReviewerUserId,
        ReviewerDisplayName = DisplayUser(task.ReviewerUser),
        ReviewComment = task.ReviewComment,
        DeliverableSubmitterId = task.DeliverableSubmitterId,
        DeliverableSubmitterDisplayName = DisplayUser(task.DeliverableSubmitter),
        DeliverableSubmittedAt = task.DeliverableSubmittedAt is null ? null : NormalizeUtc(task.DeliverableSubmittedAt.Value),
        DeliverableReviewedAt = task.DeliverableReviewedAt is null ? null : NormalizeUtc(task.DeliverableReviewedAt.Value)
    };

    private static ApiProjectTaskProgressReport ToReportDto(DbProjectTaskProgressReport report) => new()
    {
        Id = report.TaskProgressReportId,
        TaskId = report.TaskId,
        ReporterUserId = report.ReporterUserId,
        ReporterName = report.Reporter?.RealName?.Trim() is { Length: > 0 } name ? name : report.Reporter?.Username?.Trim() ?? "未知成员",
        Progress = report.Progress,
        TaskStatus = ToApiReportStatus(report.TaskStatus),
        ReportContent = report.ReportContent,
        DelayReason = report.DelayReason,
        SubmittedAt = NormalizeUtc(report.SubmittedAt)
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

    private static ApiProjectTaskProgressReport.TaskStatusEnum ToApiReportStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        InProgressStatus => ApiProjectTaskProgressReport.TaskStatusEnum.InProgressEnum,
        CompletedStatus => ApiProjectTaskProgressReport.TaskStatusEnum.CompletedEnum,
        DelayedStatus => ApiProjectTaskProgressReport.TaskStatusEnum.DelayedEnum,
        _ => ApiProjectTaskProgressReport.TaskStatusEnum.PendingEnum
    };

    private static ApiProjectTask.DeliverableStatusEnum ToApiDeliverableStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        DeliverablePendingStatus => ApiProjectTask.DeliverableStatusEnum.PendingEnum,
        DeliverableApprovedStatus => ApiProjectTask.DeliverableStatusEnum.ApprovedEnum,
        DeliverableRejectedStatus => ApiProjectTask.DeliverableStatusEnum.RejectedEnum,
        _ => ApiProjectTask.DeliverableStatusEnum.NotSubmittedEnum
    };

    private async Task RefreshProjectStatusFromDeliverablesAsync(DbProject project, DbProjectTask changedTask, DateTime now)
    {
        var tasks = await _db.ProjectTasks
            .AsNoTracking()
            .Where(item => item.ProjectId == project.ProjectId)
            .Select(item => new { item.TaskId, item.DueDate, item.DeliverableStatus })
            .ToListAsync();
        if (tasks.Count == 0) return;

        if (tasks.All(item => string.Equals(
            item.TaskId == changedTask.TaskId ? changedTask.DeliverableStatus : item.DeliverableStatus,
            DeliverableApprovedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            project.ProjectStatus = FinishedProjectStatus;
            return;
        }

        project.ProjectStatus = tasks.Any(item =>
            item.DueDate is not null &&
            NormalizeUtc(item.DueDate.Value) < now &&
            !string.Equals(
                item.TaskId == changedTask.TaskId ? changedTask.DeliverableStatus : item.DeliverableStatus,
                DeliverableApprovedStatus,
                StringComparison.OrdinalIgnoreCase))
            ? DelayedProjectStatus
            : RunningProjectStatus;
    }

    private async Task<(IActionResult? Result, DbUser? User)> EnsureActiveUserAsync(int userId)
    {
        var user = await _db.Users
            .Include(item => item.UserRoles)
                .ThenInclude(item => item.Role)
            .FirstOrDefaultAsync(item => item.UserId == userId);
        if (user is null) return (Error(404, "current_user_not_found", "当前登录用户不存在。"), null);
        if (!UsersController.IsActive(user.AccountStatus))
        {
            return (Error(400, "current_user_disabled", "当前账号不可用。"), user);
        }

        return (null, user);
    }

    private async Task<bool> CanReviewDeliverableAsync(DbUser user, int clubId)
    {
        if (UsersController.IsSystemAdmin(user)) return false;
        if (IsPlatformClubAdmin(user)) return true;
        return HasClubRole(user, clubId, AdvisorRoleCodes);
    }

    private async Task<bool> CanViewAllTasksAsync(DbProject project, int userId)
    {
        if (project.LeaderUserId == userId) return true;

        var access = await EnsureActiveUserAsync(userId);
        return access.Result is null &&
            access.User is not null &&
            await CanReviewDeliverableAsync(access.User, project.ClubId);
    }

    private static bool HasClubRole(DbUser user, int clubId, IReadOnlySet<string> roleCodes) =>
        user.UserRoles.Any(role =>
            role.ClubId == clubId &&
            role.Role is not null &&
            roleCodes.Contains(NormalizeRoleCode(role.Role.RoleCode)));

    private static bool IsPlatformClubAdmin(DbUser user) =>
        user.UserRoles.Any(role => role.ClubId is null && IsPlatformClubAdminRole(role.Role));

    private static bool IsPlatformClubAdminRole(DbRole? role)
    {
        if (role is null) return false;

        var code = NormalizeRoleCode(role.RoleCode);
        if (code is "system_admin" or "sysadmin") return false;
        if (!IsSystemScope(role)) return false;
        if (PlatformClubAdminRoleCodes.Contains(code)) return true;

        return (role.RoleName ?? string.Empty).Contains("社团管理员", StringComparison.Ordinal) &&
               (role.PermissionDesc ?? string.Empty).Contains("审核", StringComparison.Ordinal);
    }

    private static bool IsSystemScope(DbRole role) =>
        string.Equals(role.RoleScope?.Trim(), "system", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role.RoleScope?.Trim(), "平台", StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectAcceptingDeliverables(string? status) =>
        string.Equals(status, RunningProjectStatus, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, DelayedProjectStatus, StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? DisplayUser(DbUser? user)
    {
        if (user is null) return null;

        var name = !string.IsNullOrWhiteSpace(user.RealName) ? user.RealName.Trim() : user.Username.Trim();
        return string.IsNullOrWhiteSpace(user.StudentNo) ? name : $"{name}（{user.StudentNo.Trim()}）";
    }

    private static string NormalizeRoleCode(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string? NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
