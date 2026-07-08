using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActivitiesController : ControllerBase
{
    private readonly ClubHubDbContext _db;

    public ActivitiesController(ClubHubDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
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
                a.PublishedAt,
                a.CheckinStartAt,
                a.CheckinEndAt,
                a.CheckoutStartAt,
                a.CheckoutEndAt,
                a.Participations.Count
            ))
            .ToListAsync();

        return Ok(activities);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateActivityRequest req)
    {
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

        if (req.CreatorUserId is not null && !await UserExists(req.CreatorUserId.Value))
        {
            return BadRequest(new { message = "创建人用户不存在，请留空或填写有效用户 ID。" });
        }

        var maxId = await _db.Activities.MaxAsync(a => (int?)a.ActivityId) ?? 0;
        var now = DateTime.Now;
        var activity = new Activity
        {
            ActivityId = maxId + 1,
            ClubId = req.ClubId,
            CreatorUserId = req.CreatorUserId,
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
    public async Task<IActionResult> GetById(int activityId)
    {
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
                a.PublishedAt,
                a.CheckinStartAt,
                a.CheckinEndAt,
                a.CheckoutStartAt,
                a.CheckoutEndAt,
                a.Participations.Count
            ))
            .FirstOrDefaultAsync();

        return activity is null ? NotFound() : Ok(activity);
    }

    [HttpPost("{activityId:int}/review")]
    public async Task<IActionResult> Review(int activityId, [FromBody] ReviewActivityRequest req)
    {
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

        if (req.ReviewerUserId is not null && !await UserExists(req.ReviewerUserId.Value))
        {
            return BadRequest(new { message = "审核人用户不存在，请留空或填写有效用户 ID。" });
        }

        activity.ReviewerUserId = req.ReviewerUserId;
        activity.ReviewComment = req.Comment;
        activity.ActivityStatus = req.Approved.Value ? "published" : "rejected";
        activity.PublishedAt = req.Approved.Value ? DateTime.Now : null;

        await _db.SaveChangesAsync();
        var currentParticipants = await _db.ActivityParticipations.CountAsync(p => p.ActivityId == activityId);
        return Ok(ToDto(activity, currentParticipants));
    }

    [HttpPut("{activityId:int}/checkin-settings")]
    public async Task<IActionResult> UpdateCheckinSettings(int activityId, [FromBody] UpdateCheckinSettingsRequest req)
    {
        var activity = await _db.Activities
            .Include(a => a.Club)
            .FirstOrDefaultAsync(a => a.ActivityId == activityId);

        if (activity is null) return NotFound();
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
        var currentParticipants = await _db.ActivityParticipations.CountAsync(p => p.ActivityId == activityId);
        return Ok(ToDto(activity, currentParticipants));
    }

    [HttpGet("{activityId:int}/participations")]
    public async Task<IActionResult> GetParticipations(int activityId)
    {
        var activityExists = await _db.Activities.AnyAsync(a => a.ActivityId == activityId);
        if (!activityExists) return NotFound();

        var participations = await _db.ActivityParticipations
            .Where(p => p.ActivityId == activityId)
            .OrderBy(p => p.ParticipationId)
            .Select(p => new ActivityParticipationDto(
                p.ParticipationId,
                p.ActivityId,
                p.UserId,
                p.RegisterStatus,
                p.RegisteredAt,
                p.CheckinAt,
                p.CheckoutAt,
                p.SignStatus,
                p.Remark
            ))
            .ToListAsync();

        return Ok(participations);
    }

    [HttpPost("{activityId:int}/checkin")]
    public async Task<IActionResult> Checkin(int activityId, [FromBody] ActivitySignRequest req)
    {
        return await Sign(activityId, req, isCheckin: true);
    }

    [HttpPost("{activityId:int}/checkout")]
    public async Task<IActionResult> Checkout(int activityId, [FromBody] ActivitySignRequest req)
    {
        return await Sign(activityId, req, isCheckin: false);
    }

    private async Task<IActionResult> Sign(int activityId, ActivitySignRequest req, bool isCheckin)
    {
        var activity = await _db.Activities
            .Include(a => a.Participations)
            .FirstOrDefaultAsync(a => a.ActivityId == activityId);

        if (activity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Code))
        {
            return BadRequest(new { message = isCheckin ? "签到码不能为空。" : "签退码不能为空。" });
        }

        if (activity.ActivityStatus != "published")
        {
            return BadRequest(new { message = "只有已发布的活动可以签到或签退。" });
        }

        if (!await UserExists(req.UserId))
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

        var participation = activity.Participations.FirstOrDefault(p => p.UserId == req.UserId);
        if (participation is null)
        {
            var maxId = await _db.ActivityParticipations.MaxAsync(p => (int?)p.ParticipationId) ?? 0;
            participation = new ActivityParticipation
            {
                ParticipationId = maxId + 1,
                ActivityId = activity.ActivityId,
                UserId = req.UserId,
                RegisterStatus = "onsite",
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
            participation.RegisterStatus,
            participation.RegisteredAt,
            participation.CheckinAt,
            participation.CheckoutAt,
            participation.SignStatus,
            participation.Remark
        ));
    }

    private static ActivityDto ToDto(Activity activity, int currentParticipants)
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
            activity.PublishedAt,
            activity.CheckinStartAt,
            activity.CheckinEndAt,
            activity.CheckoutStartAt,
            activity.CheckoutEndAt,
            currentParticipants
        );
    }

    private async Task<bool> UserExists(int userId)
    {
        return await _db.Users.AnyAsync(u => u.UserId == userId);
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
    DateTime? PublishedAt,
    DateTime? CheckinStartAt,
    DateTime? CheckinEndAt,
    DateTime? CheckoutStartAt,
    DateTime? CheckoutEndAt,
    int CurrentParticipants
);

public class CreateActivityRequest
{
    [Required]
    public int ClubId { get; set; }

    public int? CreatorUserId { get; set; }

    [Required, StringLength(100, MinimumLength = 1)]
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

    public int? ReviewerUserId { get; set; }

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
    [Required]
    public int UserId { get; set; }

    [Required, StringLength(50, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;
}

public record ActivityParticipationDto(
    int Id,
    int ActivityId,
    int UserId,
    string? RegisterStatus,
    DateTime? RegisteredAt,
    DateTime? CheckinAt,
    DateTime? CheckoutAt,
    string? SignStatus,
    string? Remark
);
