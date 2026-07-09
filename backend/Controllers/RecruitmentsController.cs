using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
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
        [FromQuery] int viewerUserId,
        [FromQuery] int? clubId,
        [FromQuery] string? status)
    {
        if (viewerUserId <= 0) return BadRequest(new { message = "请选择当前用户。" });

        var viewer = await LoadUserAsync(viewerUserId);
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

        var now = BusinessNow();
        var recruitments = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.RecruitId)
            .ToListAsync();

        return Ok(recruitments
            .Where(r => CanViewRecruitment(viewer, r))
            .Where(r => normalizedStatus is null || EffectiveRecruitmentStatus(r, now) == normalizedStatus)
            .Select(r => ToRecruitmentDto(r, viewer, now)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecruitmentRequest req)
    {
        var validationError = ValidateCreateRecruitmentRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var operatorUser = await LoadUserAsync(req.CurrentUserId);
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
        var nextId = (await _db.Recruitments.MaxAsync(r => (int?)r.RecruitId) ?? 0) + 1;
        var recruitment = new Recruitment
        {
            RecruitId = nextId,
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
        return CreatedAtAction(nameof(GetAll), new { viewerUserId = req.CurrentUserId }, ToRecruitmentDto(created, operatorUser, BusinessNow()));
    }

    [HttpPatch("{recruitId:int}")]
    public async Task<IActionResult> Update(int recruitId, [FromBody] UpdateRecruitmentRequest req)
    {
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请选择当前操作用户。" });

        var operatorUser = await LoadUserAsync(req.CurrentUserId);
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
    public async Task<IActionResult> Delete(int recruitId, [FromQuery] int currentUserId)
    {
        if (currentUserId <= 0) return BadRequest(new { message = "请选择当前操作用户。" });

        var operatorUser = await LoadUserAsync(currentUserId);
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
    public async Task<IActionResult> ReviewRecruitment(int recruitId, [FromBody] ReviewRecruitmentRequest req)
    {
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请选择当前审核用户。" });

        var decision = NormalizeRecruitmentReviewDecision(req.Decision);
        if (decision is not ReviewApproved and not ReviewRejected)
        {
            return BadRequest(new { message = "审核结果只能是 approved 或 rejected。" });
        }

        var reviewer = await LoadUserAsync(req.CurrentUserId);
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
    public async Task<IActionResult> GetApplications(int recruitId, [FromQuery] int viewerUserId)
    {
        var result = await _applicationService.GetApplicationsAsync(recruitId, viewerUserId);
        return ToActionResult(result);
    }

    [HttpPost("{recruitId:int}/applications")]
    public async Task<IActionResult> CreateApplication(int recruitId, [FromBody] CreateRecruitmentApplicationRequest req)
    {
        var result = await _applicationService.CreateApplicationAsync(recruitId, req);
        if (!result.Succeeded) return ToActionResult(result);

        return CreatedAtAction(
            nameof(GetApplications),
            new { recruitId, viewerUserId = req.CurrentUserId },
            result.Value);
    }

    [HttpPatch("applications/{applicationId:int}/review")]
    public async Task<IActionResult> ReviewApplication(int applicationId, [FromBody] ReviewRecruitmentApplicationRequest req)
    {
        var result = await _applicationService.ReviewApplicationAsync(applicationId, req);
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
