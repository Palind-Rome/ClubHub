using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Rest;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CreateRecruitmentApplicationRequest = Org.OpenAPITools.Models.CreateRecruitmentApplicationRequest;
using CreateRecruitmentRequest = Org.OpenAPITools.Models.CreateRecruitmentRequest;
using ReviewRecruitmentApplicationRequest = Org.OpenAPITools.Models.ReviewRecruitmentApplicationRequest;
using ReviewRecruitmentRequest = Org.OpenAPITools.Models.ReviewRecruitmentRequest;
using UpdateRecruitmentRequest = Org.OpenAPITools.Models.UpdateRecruitmentRequest;
using static ClubHub.Api.Services.RecruitmentWorkflow;

namespace ClubHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class RecruitmentsController : ControllerBase
{
    private readonly ClubHubDbContext _db;
    private readonly RecruitmentApplicationService _applicationService;

    public RecruitmentsController(ClubHubDbContext db, RecruitmentApplicationService applicationService)
    {
        _db = db;
        _applicationService = applicationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? clubId,
        [FromQuery] string? status)
    {
        var viewerUserId = User.GetUserId();
        if (viewerUserId is null) return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var viewer = await LoadUserAsync(viewerUserId.Value);
        if (viewer is null) return NotFound(new { message = "当前用户不存在。" });

        var normalizedStatus = NormalizeRecruitmentStatusFilter(status);
        if (!string.IsNullOrWhiteSpace(status) && normalizedStatus is null)
        {
            return BadRequest(new { message = "招募状态只能是 draft、pending_review、not_started、accepting 或 ended。" });
        }

        var query = RecruitmentQuery(asNoTracking: true);
        if (clubId is not null)
        {
            query = query.Where(r => r.ClubId == clubId.Value);
        }

        var draftStatuses = new[] { "draft", "草稿" };
        var pendingStatuses = new[] { "pending_review", "pending", "reviewing", "审核中", "待审核" };
        var publishedStatuses = new[] { "published", "open", "approved", "报名中", "申请中", "发布", "已通过" };
        var closedStatuses = new[] { "closed", "ended", "finished", "结束", "已结束" };
        var knownStatuses = draftStatuses
            .Concat(pendingStatuses)
            .Concat(publishedStatuses)
            .Concat(closedStatuses)
            .ToArray();
        var managedClubIds = viewer.UserRoles
            .Where(role => role.ClubId is not null && IsRecruitmentManagerRole(role.Role))
            .Select(role => role.ClubId!.Value)
            .Distinct()
            .ToArray();
        var canManageAll = UsersController.IsSystemAdmin(viewer);
        var canReview = UsersController.IsPlatformAdmin(viewer) || canManageAll;

        query = query.Where(recruitment =>
            canManageAll ||
            (!draftStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower()) &&
             knownStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower())) ||
            managedClubIds.Contains(recruitment.ClubId) ||
            (canReview &&
             pendingStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower())));

        var now = BusinessNow();
        query = normalizedStatus switch
        {
            RecruitmentStatuses.Draft => query.Where(recruitment =>
                draftStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower()) ||
                !knownStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower())),
            RecruitmentStatuses.PendingReview => query.Where(recruitment =>
                pendingStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower())),
            RecruitmentStatuses.NotStarted => query.Where(recruitment =>
                publishedStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower()) &&
                (recruitment.EndAt == null || recruitment.EndAt >= now) &&
                recruitment.StartAt > now),
            RecruitmentStatuses.Accepting => query.Where(recruitment =>
                publishedStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower()) &&
                (recruitment.EndAt == null || recruitment.EndAt >= now) &&
                (recruitment.StartAt == null || recruitment.StartAt <= now)),
            RecruitmentStatuses.Ended => query.Where(recruitment =>
                closedStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower()) ||
                (publishedStatuses.Contains((recruitment.RecruitStatus ?? string.Empty).Trim().ToLower()) &&
                 recruitment.EndAt != null && recruitment.EndAt < now)),
            _ => query
        };

        var page = await ApiPaginationQuery.MaterializeAsync(
            query
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.RecruitId),
            HttpContext,
            HttpContext.RequestAborted);
        if (page.Error is not null) return BadRequest(page.Error);

        return Ok(page.Items.Select(r => ToRecruitmentDto(r, viewer, now)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecruitmentRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var validationError = ValidateCreateRecruitmentRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var operatorUser = await LoadUserAsync(currentUserId.Value);
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
        if (requestedStatus is null)
        {
            return BadRequest(new { message = "纳新只能保存草稿或提交审核。" });
        }
        var recruitStatus = requestedStatus;

        var now = DateTime.UtcNow;
        var recruitment = new Recruitment
        {
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
        return CreatedAtAction(nameof(GetAll), ToRecruitmentDto(created, operatorUser, BusinessNow()));
    }

    [HttpPatch("{recruitId:int}")]
    public async Task<IActionResult> Update(int recruitId, [FromBody] UpdateRecruitmentRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var operatorUser = await LoadUserAsync(currentUserId.Value);
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
        if (req.RecruitStatus.HasValue && status is null)
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
    public async Task<IActionResult> Delete(int recruitId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var operatorUser = await LoadUserAsync(currentUserId.Value);
        if (operatorUser is null) return NotFound(new { message = "当前用户不存在。" });

        var recruitment = await RecruitmentQuery().FirstOrDefaultAsync(r => r.RecruitId == recruitId);
        if (recruitment is null) return NotFound(new { message = "招募不存在。" });

        if (NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) != RecruitmentStatuses.Draft)
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
    [HttpPost("{recruitId:int}/reviews")]
    [ClubHub.Api.Infrastructure.Idempotency.IdempotentOperation("reviewRecruitment")]
    public async Task<IActionResult> ReviewRecruitment(int recruitId, [FromBody] ReviewRecruitmentRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var decision = NormalizeRecruitmentReviewDecision(req.Decision);
        if (decision is not ReviewApproved and not ReviewRejected)
        {
            return BadRequest(new { message = "审核结果只能是 approved 或 rejected。" });
        }

        var reviewer = await LoadUserAsync(currentUserId.Value);
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

        if (NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) != RecruitmentStatuses.PendingReview)
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

        recruitment.RecruitStatus = decision == ReviewApproved ? RecruitmentStatuses.Published : RecruitmentStatuses.Draft;
        await _db.SaveChangesAsync();

        var reviewed = await RecruitmentQuery().FirstAsync(r => r.RecruitId == recruitId);
        return Ok(ToRecruitmentDto(reviewed, reviewer, BusinessNow()));
    }

    [HttpGet("{recruitId:int}/applications")]
    public async Task<IActionResult> GetApplications(int recruitId)
    {
        var viewerUserId = User.GetUserId();
        if (viewerUserId is null) return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var result = await _applicationService.GetApplicationsAsync(
            recruitId,
            viewerUserId.Value,
            HttpContext);
        return ToActionResult(result);
    }

    [HttpPost("{recruitId:int}/applications")]
    [ClubHub.Api.Infrastructure.Idempotency.IdempotentOperation("createRecruitmentApplication")]
    public async Task<IActionResult> CreateApplication(int recruitId, [FromBody] CreateRecruitmentApplicationRequest req)
    {
        var applicantUserId = User.GetUserId();
        if (applicantUserId is null) return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var result = await _applicationService.CreateApplicationAsync(recruitId, applicantUserId.Value, req);
        if (!result.Succeeded) return ToActionResult(result);

        return CreatedAtAction(
            nameof(GetApplications),
            new { recruitId },
            result.Value);
    }

    [HttpPatch("applications/{applicationId:int}/review")]
    [HttpPost("~/api/v1/applications/{applicationId:int}/reviews")]
    [ClubHub.Api.Infrastructure.Idempotency.IdempotentOperation("reviewRecruitmentApplication")]
    public async Task<IActionResult> ReviewApplication(int applicationId, [FromBody] ReviewRecruitmentApplicationRequest req)
    {
        var reviewerUserId = User.GetUserId();
        if (reviewerUserId is null) return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var result = await _applicationService.ReviewApplicationAsync(applicationId, reviewerUserId.Value, req);
        return ToActionResult(result);
    }

    private IQueryable<Recruitment> RecruitmentQuery(bool asNoTracking = false)
    {
        var query = _db.Recruitments
            .Include(r => r.Club)
            .Include(r => r.Applications);

        return asNoTracking ? query.AsNoTracking() : query;
    }

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
            r.RecruitStatus == RecruitmentStatuses.Published &&
            r.StartAt.HasValue &&
            r.EndAt.HasValue &&
            r.StartAt.Value < endAt &&
            r.EndAt.Value > startAt);

    private IActionResult ToActionResult<T>(ServiceResult<T> result)
    {
        if (result.Succeeded) return Ok(result.Value);

        return StatusCode(result.StatusCode, new { message = result.ErrorMessage });
    }
}
