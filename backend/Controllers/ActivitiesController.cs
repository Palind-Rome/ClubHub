using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Extensions;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ActivityRegistrationResult = Org.OpenAPITools.Models.ActivityRegistrationResult;
using ApiError = Org.OpenAPITools.Models.ApiError;
using ApplyActivityBudgetRequest = Org.OpenAPITools.Models.ApplyActivityBudgetRequest;
using ReviewActivityBudgetRequest = Org.OpenAPITools.Models.ReviewActivityBudgetRequest;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActivitiesController : ControllerBase
{
    private const int MaxRegisterRetries = 3;
    private const string ActivityStatusPublished = "published";
    private const string MemberStatusActive = "active";
    private const string RegisterStatusPending = "pending";
    private const string RegisterStatusAccepted = "accepted";
    private const string RegisterStatusOnsite = "onsite";
    private const string BudgetStatusPending = "pending";
    private const string BudgetStatusApproved = "approved";
    private const string BudgetStatusRejected = "rejected";
    private const int BudgetPurposeMaxLength = 255;
    private const int BudgetCommentMaxLength = 255;
    private const int BudgetDetailMaxLength = 4000;
    private const string ActivityCreatePermission = "activity:create";
    private const string ActivityReviewPermission = "activity:review";
    private const string ActivityCheckinManagePermission = "activity:checkin:manage";
    private const string ActivityCheckinPermission = "activity:checkin";
    private readonly ClubHubDbContext _db;
    private readonly AuthService _authService;

    public ActivitiesController(ClubHubDbContext db, AuthService authService)
    {
        _db = db;
        _authService = authService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? currentUserId)
    {
        var shouldCheckRegistration = currentUserId is > 0;
        var viewerUserId = currentUserId.GetValueOrDefault();

        var activities = await _db.Activities
            .OrderBy(a => a.ActivityId)
            .Select(a => new ActivityDto(
                a.ActivityId,
                a.Title,
                a.ActivityType,
                a.Description,
                a.Club != null ? a.Club.ClubName : "",
                a.ClubId,
                a.CreatorUserId,
                a.StartAt,
                a.EndAt,
                a.Location,
                a.ActivityStatus,
                a.Capacity,
                a.RegistrationDeadline,
                a.ReviewerUserId,
                a.ReviewComment,
                a.BudgetAmount,
                a.BudgetPurpose,
                a.BudgetDetail,
                a.BudgetStatus,
                a.BudgetReviewerId,
                a.BudgetComment,
                a.PublishedAt,
                a.CheckinStartAt,
                a.CheckinEndAt,
                a.CheckoutStartAt,
                a.CheckoutEndAt,
                _db.ActivityParticipations.Count(p =>
                    p.ActivityId == a.ActivityId &&
                    (p.RegisterStatus == RegisterStatusPending ||
                     p.RegisterStatus == RegisterStatusAccepted ||
                     p.RegisterStatus == RegisterStatusOnsite)),
                shouldCheckRegistration &&
                _db.ActivityParticipations.Any(p =>
                    p.ActivityId == a.ActivityId &&
                    p.UserId == viewerUserId &&
                    (p.RegisterStatus == RegisterStatusPending ||
                     p.RegisterStatus == RegisterStatusAccepted ||
                     p.RegisterStatus == RegisterStatusOnsite))
            ))
            .ToListAsync();

        return Ok(activities);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateActivityRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
        {
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });
        }

        if (string.IsNullOrWhiteSpace(req.Title))
        {
            return BadRequest(new { message = "活动标题不能为空。" });
        }

        if (req.EndTime <= req.StartTime)
        {
            return BadRequest(new { message = "活动结束时间必须晚于开始时间。" });
        }

        if (req.RegistrationDeadline is not null && req.RegistrationDeadline > req.StartTime)
        {
            return BadRequest(new { message = "报名截止时间不能晚于活动开始时间。" });
        }

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == req.ClubId);
        if (club is null)
        {
            return BadRequest(new { message = "指定社团不存在，不能创建活动。" });
        }

        var permissionError = await EnsurePermissionAsync(
            currentUserId.Value,
            ActivityCreatePermission,
            req.ClubId,
            "当前用户没有该社团的活动创建权限。");
        if (permissionError is not null)
        {
            return permissionError;
        }

        var maxId = await _db.Activities.MaxAsync(a => (int?)a.ActivityId) ?? 0;
        var now = DateTime.Now;
        var activity = new Activity
        {
            ActivityId = maxId + 1,
            ClubId = req.ClubId,
            CreatorUserId = currentUserId.Value,
            Title = req.Title.Trim(),
            ActivityType = req.ActivityType,
            Description = req.Description,
            Location = req.Location,
            StartAt = req.StartTime,
            EndAt = req.EndTime,
            Capacity = req.MaxParticipants,
            RegistrationDeadline = req.RegistrationDeadline,
            ActivityStatus = "pending_review",
            CreatedAt = now,
            Club = club
        };

        _db.Activities.Add(activity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { activityId = activity.ActivityId }, ToDto(activity, 0));
    }

    [HttpGet("{activityId:int}")]
    public async Task<IActionResult> GetById(int activityId, [FromQuery] int? currentUserId)
    {
        var shouldCheckRegistration = currentUserId is > 0;
        var viewerUserId = currentUserId.GetValueOrDefault();

        var activity = await _db.Activities
            .Where(a => a.ActivityId == activityId)
            .Select(a => new ActivityDto(
                a.ActivityId,
                a.Title,
                a.ActivityType,
                a.Description,
                a.Club != null ? a.Club.ClubName : "",
                a.ClubId,
                a.CreatorUserId,
                a.StartAt,
                a.EndAt,
                a.Location,
                a.ActivityStatus,
                a.Capacity,
                a.RegistrationDeadline,
                a.ReviewerUserId,
                a.ReviewComment,
                a.BudgetAmount,
                a.BudgetPurpose,
                a.BudgetDetail,
                a.BudgetStatus,
                a.BudgetReviewerId,
                a.BudgetComment,
                a.PublishedAt,
                a.CheckinStartAt,
                a.CheckinEndAt,
                a.CheckoutStartAt,
                a.CheckoutEndAt,
                _db.ActivityParticipations.Count(p =>
                    p.ActivityId == a.ActivityId &&
                    (p.RegisterStatus == RegisterStatusPending ||
                     p.RegisterStatus == RegisterStatusAccepted ||
                     p.RegisterStatus == RegisterStatusOnsite)),
                shouldCheckRegistration &&
                _db.ActivityParticipations.Any(p =>
                    p.ActivityId == a.ActivityId &&
                    p.UserId == viewerUserId &&
                    (p.RegisterStatus == RegisterStatusPending ||
                     p.RegisterStatus == RegisterStatusAccepted ||
                     p.RegisterStatus == RegisterStatusOnsite))
            ))
            .FirstOrDefaultAsync();

        return activity is null ? NotFound() : Ok(activity);
    }

    [HttpPost("{activityId:int}/registrations")]
    [Authorize]
    public async Task<IActionResult> Register(int activityId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "登录状态已失效，请重新登录。");
        }

        for (var attempt = 1; attempt <= MaxRegisterRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            var activity = await LockActivityForRegistration(activityId);
            if (activity is null)
            {
                return Error(StatusCodes.Status404NotFound, "ACTIVITY_NOT_FOUND", "活动不存在");
            }

            if (!string.Equals(activity.ActivityStatus, ActivityStatusPublished, StringComparison.OrdinalIgnoreCase))
            {
                return Error(StatusCodes.Status400BadRequest, "ACTIVITY_NOT_PUBLISHED", "活动未发布，暂不能报名");
            }

            var now = DateTime.Now;
            if (activity.RegistrationDeadline is not null && now > activity.RegistrationDeadline.Value)
            {
                return Error(StatusCodes.Status400BadRequest, "REGISTRATION_CLOSED", "报名已截止");
            }

            var userExists = await _db.Users.AnyAsync(u => u.UserId == currentUserId.Value);
            if (!userExists)
            {
                return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "用户不存在");
            }

            var isClubMember = await _db.ClubMembers.AnyAsync(m =>
                m.ClubId == activity.ClubId &&
                m.UserId == currentUserId.Value &&
                (m.MemberStatus == null || m.MemberStatus.ToLower() == MemberStatusActive));
            if (!isClubMember)
            {
                return Error(StatusCodes.Status400BadRequest, "NOT_CLUB_MEMBER", "当前用户不符合报名资格");
            }

            var alreadyRegistered = await _db.ActivityParticipations.AnyAsync(p =>
                p.ActivityId == activityId &&
                p.UserId == currentUserId.Value &&
                (p.RegisterStatus == RegisterStatusPending ||
                 p.RegisterStatus == RegisterStatusAccepted ||
                 p.RegisterStatus == RegisterStatusOnsite));
            if (alreadyRegistered)
            {
                return Error(StatusCodes.Status409Conflict, "ALREADY_REGISTERED", "你已报名该活动");
            }

            var currentParticipants = await CountActiveParticipants(activityId);
            if (activity.Capacity is not null && currentParticipants >= activity.Capacity.Value)
            {
                return Error(StatusCodes.Status409Conflict, "CAPACITY_FULL", "活动名额已满");
            }

            var participation = new ActivityParticipation
            {
                ParticipationId = await NextParticipationId(),
                ActivityId = activityId,
                UserId = currentUserId.Value,
                RegisterStatus = RegisterStatusAccepted,
                RegisteredAt = now,
                SignStatus = "registered"
            };

            _db.ActivityParticipations.Add(participation);

            try
            {
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var result = new ActivityRegistrationResult
                {
                    ParticipationId = participation.ParticipationId,
                    ActivityId = participation.ActivityId,
                    UserId = participation.UserId,
                    RegisterStatus = ActivityRegistrationResult.RegisterStatusEnum.AcceptedEnum,
                    RegisteredAt = now,
                    CurrentParticipants = currentParticipants + 1,
                    Message = "报名成功"
                };

                return CreatedAtAction(
                    nameof(GetById),
                    new { activityId, currentUserId = currentUserId.Value },
                    result);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                _db.Entry(participation).State = EntityState.Detached;
            }
        }

        return Error(StatusCodes.Status409Conflict, "REGISTRATION_CONFLICT", "报名写入冲突，请重试");
    }

    [HttpPost("{activityId:int}/review")]
    [Authorize]
    public async Task<IActionResult> Review(int activityId, [FromBody] ReviewActivityRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
        {
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });
        }

        var activity = await _db.Activities
            .Include(a => a.Club)
            .FirstOrDefaultAsync(a => a.ActivityId == activityId);

        if (activity is null) return NotFound();
        if (req.Approved is null)
        {
            return BadRequest(new { message = "必须提供审核结果 approved。" });
        }

        if (activity.ActivityStatus is "published" or "ongoing" or "finished" or "cancelled")
        {
            return BadRequest(new { message = "已发布或已结束的活动不能重复审核。" });
        }

        var permissionError = await EnsurePermissionAsync(
            currentUserId.Value,
            ActivityReviewPermission,
            activity.ClubId,
            "当前用户没有该社团的活动审核权限。");
        if (permissionError is not null)
        {
            return permissionError;
        }

        activity.ReviewerUserId = currentUserId.Value;
        activity.ReviewComment = req.Comment;
        activity.ActivityStatus = req.Approved.Value ? "published" : "rejected";
        activity.PublishedAt = req.Approved.Value ? DateTime.Now : null;

        await _db.SaveChangesAsync();
        var currentParticipants = await CountActiveParticipants(activityId);
        return Ok(ToDto(activity, currentParticipants));
    }

    [HttpPut("{activityId:int}/budget")]
    [Authorize]
    public async Task<IActionResult> ApplyBudget(int activityId, [FromBody] ApplyActivityBudgetRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
        {
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });
        }

        var budgetPurpose = req.BudgetPurpose?.Trim();
        var budgetDetail = string.IsNullOrWhiteSpace(req.BudgetDetail) ? null : req.BudgetDetail.Trim();
        if (req.BudgetAmount < 0.01)
        {
            return BadRequest(new { message = "预算金额必须大于 0。" });
        }

        if (string.IsNullOrWhiteSpace(budgetPurpose))
        {
            return BadRequest(new { message = "预算用途不能为空。" });
        }

        if (budgetPurpose.Length > BudgetPurposeMaxLength)
        {
            return BadRequest(new { message = $"预算用途不能超过 {BudgetPurposeMaxLength} 个字符。" });
        }

        if (budgetDetail?.Length > BudgetDetailMaxLength)
        {
            return BadRequest(new { message = $"经费明细不能超过 {BudgetDetailMaxLength} 个字符。" });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var activity = await LockActivityForRegistration(activityId);

        if (activity is null) return NotFound();

        var permission = await _authService.CheckPermissionAsync(currentUserId.Value, "budget:apply", activity.ClubId);
        if (permission.Value?.Allowed != true)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "当前用户没有该社团的经费申请权限。" });
        }

        if (activity.ActivityStatus is "finished" or "cancelled")
        {
            return BadRequest(new { message = "已结束或已取消的活动不能提交经费申请。" });
        }

        if (string.Equals(activity.BudgetStatus, BudgetStatusApproved, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "经费预算已审批通过，不能重复修改申请。" });
        }

        activity.BudgetAmount = Convert.ToDecimal(req.BudgetAmount);
        activity.BudgetPurpose = budgetPurpose;
        activity.BudgetDetail = budgetDetail;
        activity.BudgetStatus = BudgetStatusPending;
        activity.BudgetReviewerId = null;
        activity.BudgetComment = null;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var currentParticipants = await CountActiveParticipants(activityId);
        return Ok(ToDto(activity, currentParticipants));
    }

    [HttpPost("{activityId:int}/budget/review")]
    [Authorize]
    public async Task<IActionResult> ReviewBudget(int activityId, [FromBody] ReviewActivityBudgetRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
        {
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });
        }

        var budgetComment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
        if (budgetComment?.Length > BudgetCommentMaxLength)
        {
            return BadRequest(new { message = $"审批意见不能超过 {BudgetCommentMaxLength} 个字符。" });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var activity = await LockActivityForRegistration(activityId);

        if (activity is null) return NotFound();
        var permission = await _authService.CheckPermissionAsync(currentUserId.Value, "budget:review", activity.ClubId);
        if (permission.Value?.Allowed != true)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "当前用户没有该社团的经费审批权限。" });
        }

        if (!string.Equals(activity.BudgetStatus, BudgetStatusPending, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "只有待审批的经费申请才能审批。" });
        }

        activity.BudgetReviewerId = currentUserId.Value;
        activity.BudgetComment = budgetComment;
        activity.BudgetStatus = req.Approved ? BudgetStatusApproved : BudgetStatusRejected;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var currentParticipants = await CountActiveParticipants(activityId);
        return Ok(ToDto(activity, currentParticipants));
    }

    [HttpPut("{activityId:int}/checkin-settings")]
    [Authorize]
    public async Task<IActionResult> UpdateCheckinSettings(int activityId, [FromBody] UpdateCheckinSettingsRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
        {
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });
        }

        var activity = await _db.Activities
            .Include(a => a.Club)
            .FirstOrDefaultAsync(a => a.ActivityId == activityId);

        if (activity is null) return NotFound();

        var permissionError = await EnsurePermissionAsync(
            currentUserId.Value,
            ActivityCheckinManagePermission,
            activity.ClubId,
            "当前用户没有该社团的签到管理权限。");
        if (permissionError is not null)
        {
            return permissionError;
        }

        if (activity.ActivityStatus is not "published" and not "ongoing")
        {
            return BadRequest(new { message = "只有已发布或进行中的活动才能设置签到签退规则。" });
        }

        if (string.IsNullOrWhiteSpace(req.CheckinCode) || string.IsNullOrWhiteSpace(req.CheckoutCode))
        {
            return BadRequest(new { message = "签到码和签退码不能为空。" });
        }

        if (req.CheckinEndAt <= req.CheckinStartAt)
        {
            return BadRequest(new { message = "签到结束时间必须晚于签到开始时间。" });
        }

        if (req.CheckoutEndAt <= req.CheckoutStartAt)
        {
            return BadRequest(new { message = "签退结束时间必须晚于签退开始时间。" });
        }

        if (activity.StartAt is not null && req.CheckinStartAt < activity.StartAt)
        {
            return BadRequest(new { message = "签到开始时间不能早于活动开始时间。" });
        }

        if (activity.EndAt is not null && req.CheckinEndAt > activity.EndAt)
        {
            return BadRequest(new { message = "签到结束时间不能晚于活动结束时间。" });
        }

        if (activity.StartAt is not null && req.CheckoutStartAt < activity.StartAt)
        {
            return BadRequest(new { message = "签退开始时间不能早于活动开始时间。" });
        }

        if (activity.EndAt is not null && req.CheckoutEndAt > activity.EndAt)
        {
            return BadRequest(new { message = "签退结束时间不能晚于活动结束时间。" });
        }

        activity.CheckinCode = req.CheckinCode.Trim();
        activity.CheckinStartAt = req.CheckinStartAt;
        activity.CheckinEndAt = req.CheckinEndAt;
        activity.CheckoutCode = req.CheckoutCode.Trim();
        activity.CheckoutStartAt = req.CheckoutStartAt;
        activity.CheckoutEndAt = req.CheckoutEndAt;

        await _db.SaveChangesAsync();
        var currentParticipants = await CountActiveParticipants(activityId);
        return Ok(ToDto(activity, currentParticipants));
    }

    [HttpGet("{activityId:int}/participations")]
    [Authorize]
    public async Task<IActionResult> GetParticipations(int activityId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
        {
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });
        }

        var activity = await _db.Activities.FirstOrDefaultAsync(a => a.ActivityId == activityId);
        if (activity is null) return NotFound();

        var canManage = await IsPermissionAllowedAsync(
            currentUserId.Value,
            ActivityCheckinManagePermission,
            activity.ClubId);
        var canReview = await IsPermissionAllowedAsync(
            currentUserId.Value,
            ActivityReviewPermission,
            activity.ClubId);

        var query =
            from participation in _db.ActivityParticipations
            join user in _db.Users on participation.UserId equals user.UserId
            where participation.ActivityId == activityId
            select new { participation, user };

        if (!canManage && !canReview)
        {
            query = query.Where(row => row.participation.UserId == currentUserId.Value);
        }

        var participations = await query
            .OrderBy(row => row.participation.ParticipationId)
            .Select(row => new ActivityParticipationDto(
                row.participation.ParticipationId,
                row.participation.ActivityId,
                row.participation.UserId,
                string.IsNullOrWhiteSpace(row.user.RealName) ? row.user.Username : row.user.RealName,
                row.user.StudentNo,
                row.participation.RegisterStatus,
                row.participation.RegisteredAt,
                row.participation.CheckinAt,
                row.participation.CheckoutAt,
                row.participation.SignStatus,
                row.participation.Remark
            ))
            .ToListAsync();

        return Ok(participations);
    }

    [HttpPost("{activityId:int}/checkin")]
    [Authorize]
    public async Task<IActionResult> Checkin(int activityId, [FromBody] ActivitySignRequest req)
    {
        return await Sign(activityId, req, isCheckin: true);
    }

    [HttpPost("{activityId:int}/checkout")]
    [Authorize]
    public async Task<IActionResult> Checkout(int activityId, [FromBody] ActivitySignRequest req)
    {
        return await Sign(activityId, req, isCheckin: false);
    }

    private async Task<IActionResult> Sign(int activityId, ActivitySignRequest req, bool isCheckin)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
        {
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });
        }

        var activity = await _db.Activities
            .Include(a => a.Participations)
            .FirstOrDefaultAsync(a => a.ActivityId == activityId);

        if (activity is null) return NotFound();

        var permissionError = await EnsurePermissionAsync(
            currentUserId.Value,
            ActivityCheckinPermission,
            activity.ClubId,
            isCheckin ? "当前用户没有活动签到权限。" : "当前用户没有活动签退权限。");
        if (permissionError is not null)
        {
            return permissionError;
        }

        if (string.IsNullOrWhiteSpace(req.Code))
        {
            return BadRequest(new { message = isCheckin ? "签到码不能为空。" : "签退码不能为空。" });
        }

        if (activity.ActivityStatus is not "published" and not "ongoing")
        {
            return BadRequest(new { message = "只有已发布或进行中的活动可以签到或签退。" });
        }

        var participantUser = await _db.Users
            .Where(user => user.UserId == currentUserId.Value)
            .Select(user => new
            {
                UserName = string.IsNullOrWhiteSpace(user.RealName) ? user.Username : user.RealName,
                user.StudentNo
            })
            .FirstOrDefaultAsync();
        if (participantUser is null)
        {
            return BadRequest(new { message = "用户不存在，不能签到或签退。" });
        }

        var now = DateTime.Now;
        var expectedCode = isCheckin ? activity.CheckinCode : activity.CheckoutCode;
        var windowStart = isCheckin ? activity.CheckinStartAt : activity.CheckoutStartAt;
        var windowEnd = isCheckin ? activity.CheckinEndAt : activity.CheckoutEndAt;

        if (string.IsNullOrWhiteSpace(expectedCode) || windowStart is null || windowEnd is null)
        {
            return BadRequest(new { message = isCheckin ? "活动尚未设置签到信息。" : "活动尚未设置签退信息。" });
        }

        if (!string.Equals(expectedCode, req.Code.Trim(), StringComparison.Ordinal))
        {
            return BadRequest(new { message = isCheckin ? "签到码不正确。" : "签退码不正确。" });
        }

        if (now < windowStart || now > windowEnd)
        {
            return BadRequest(new { message = isCheckin ? "当前不在签到有效时间内。" : "当前不在签退有效时间内。" });
        }

        var participation = activity.Participations.FirstOrDefault(p => p.UserId == currentUserId.Value);
        if (participation is null)
        {
            var maxId = await _db.ActivityParticipations.MaxAsync(p => (int?)p.ParticipationId) ?? 0;
            participation = new ActivityParticipation
            {
                ParticipationId = maxId + 1,
                ActivityId = activity.ActivityId,
                UserId = currentUserId.Value,
                RegisterStatus = RegisterStatusOnsite,
                RegisteredAt = now,
                SignStatus = "registered",
                Remark = "现场签到自动生成参与记录"
            };
            _db.ActivityParticipations.Add(participation);
        }

        if (isCheckin)
        {
            if (participation.CheckinAt is not null)
            {
                return BadRequest(new { message = "该用户已经签到，不能重复签到。" });
            }

            participation.CheckinAt = now;
            participation.SignStatus = "checked_in";
        }
        else
        {
            if (participation.CheckinAt is null)
            {
                return BadRequest(new { message = "该用户尚未签到，不能签退。" });
            }

            if (participation.CheckoutAt is not null)
            {
                return BadRequest(new { message = "该用户已经签退，不能重复签退。" });
            }

            participation.CheckoutAt = now;
            participation.SignStatus = "checked_out";
        }

        await _db.SaveChangesAsync();
        return Ok(new ActivityParticipationDto(
            participation.ParticipationId,
            participation.ActivityId,
            participation.UserId,
            participantUser.UserName,
            participantUser.StudentNo,
            participation.RegisterStatus,
            participation.RegisteredAt,
            participation.CheckinAt,
            participation.CheckoutAt,
            participation.SignStatus,
            participation.Remark
        ));
    }

    private async Task<Activity?> LockActivityForRegistration(int activityId)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT ACTIVITY_ID FROM ACTIVITIES WHERE ACTIVITY_ID = :activityId FOR UPDATE";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "activityId";
        parameter.Value = activityId;
        command.Parameters.Add(parameter);

        var lockedActivityId = await command.ExecuteScalarAsync();
        if (lockedActivityId is null || lockedActivityId is DBNull)
        {
            return null;
        }

        return await _db.Activities
            .Include(a => a.Club)
            .FirstOrDefaultAsync(a => a.ActivityId == activityId);
    }


    private async Task<IActionResult?> EnsurePermissionAsync(
        int userId,
        string permission,
        int? clubId,
        string forbiddenMessage)
    {
        var result = await _authService.CheckPermissionAsync(userId, permission, clubId);
        if (!result.Succeeded)
        {
            return StatusCode(result.StatusCode, new { message = result.ErrorMessage ?? "权限校验失败。" });
        }

        if (result.Value?.Allowed != true)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = forbiddenMessage });
        }

        return null;
    }

    private async Task<bool> IsPermissionAllowedAsync(int userId, string permission, int? clubId)
    {
        var result = await _authService.CheckPermissionAsync(userId, permission, clubId);
        return result.Succeeded && result.Value?.Allowed == true;
    }

    private Task<int> CountActiveParticipants(int activityId)
    {
        return _db.ActivityParticipations.CountAsync(p =>
            p.ActivityId == activityId &&
            (p.RegisterStatus == RegisterStatusPending ||
             p.RegisterStatus == RegisterStatusAccepted ||
             p.RegisterStatus == RegisterStatusOnsite));
    }

    private async Task<int> NextParticipationId()
    {
        // The course schema has no participation sequence, so callers run this inside a transaction and retry conflicts.
        var maxId = await _db.ActivityParticipations
            .Select(p => (int?)p.ParticipationId)
            .MaxAsync();
        return (maxId ?? 0) + 1;
    }

    private static ActivityDto ToDto(Activity activity, int currentParticipants, bool isRegistered = false)
    {
        return new ActivityDto(
            activity.ActivityId,
            activity.Title,
            activity.ActivityType,
            activity.Description,
            activity.Club?.ClubName ?? "",
            activity.ClubId,
            activity.CreatorUserId,
            activity.StartAt,
            activity.EndAt,
            activity.Location,
            activity.ActivityStatus,
            activity.Capacity,
            activity.RegistrationDeadline,
            activity.ReviewerUserId,
            activity.ReviewComment,
            activity.BudgetAmount,
            activity.BudgetPurpose,
            activity.BudgetDetail,
            activity.BudgetStatus,
            activity.BudgetReviewerId,
            activity.BudgetComment,
            activity.PublishedAt,
            activity.CheckinStartAt,
            activity.CheckinEndAt,
            activity.CheckoutStartAt,
            activity.CheckoutEndAt,
            currentParticipants,
            isRegistered
        );
    }

    private static ObjectResult Error(int statusCode, string code, string message)
    {
        return new ObjectResult(new ApiError
        {
            Code = code,
            Message = message
        })
        {
            StatusCode = statusCode
        };
    }
}

public record ActivityDto(
    int Id,
    string Title,
    string? ActivityType,
    string? Description,
    string ClubName,
    int ClubId,
    int? CreatorUserId,
    DateTime? StartTime,
    DateTime? EndTime,
    string? Location,
    string? Status,
    int? MaxParticipants,
    DateTime? RegistrationDeadline,
    int? ReviewerUserId,
    string? ReviewComment,
    decimal? BudgetAmount,
    string? BudgetPurpose,
    string? BudgetDetail,
    string? BudgetStatus,
    int? BudgetReviewerId,
    string? BudgetComment,
    DateTime? PublishedAt,
    DateTime? CheckinStartAt,
    DateTime? CheckinEndAt,
    DateTime? CheckoutStartAt,
    DateTime? CheckoutEndAt,
    int CurrentParticipants,
    bool IsRegistered
);

public class CreateActivityRequest
{
    [Required]
    public int ClubId { get; set; }

    [Required, StringLength(255, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(255)]
    public string? ActivityType { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(255)]
    public string? Location { get; set; }

    [Required]
    public DateTime StartTime { get; set; }

    [Required]
    public DateTime EndTime { get; set; }

    public int? MaxParticipants { get; set; }

    public DateTime? RegistrationDeadline { get; set; }
}

public class ReviewActivityRequest
{
    [Required]
    public bool? Approved { get; set; }

    public string? Comment { get; set; }
}

public class UpdateCheckinSettingsRequest
{
    [Required, StringLength(50, MinimumLength = 1)]
    public string CheckinCode { get; set; } = string.Empty;

    [Required]
    public DateTime CheckinStartAt { get; set; }

    [Required]
    public DateTime CheckinEndAt { get; set; }

    [Required, StringLength(50, MinimumLength = 1)]
    public string CheckoutCode { get; set; } = string.Empty;

    [Required]
    public DateTime CheckoutStartAt { get; set; }

    [Required]
    public DateTime CheckoutEndAt { get; set; }
}

public class ActivitySignRequest
{
    [Required, StringLength(50, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;
}

public record ActivityParticipationDto(
    int Id,
    int ActivityId,
    int UserId,
    string UserName,
    string? StudentNo,
    string? RegisterStatus,
    DateTime? RegisteredAt,
    DateTime? CheckinAt,
    DateTime? CheckoutAt,
    string? SignStatus,
    string? Remark
);
