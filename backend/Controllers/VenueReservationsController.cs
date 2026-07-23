using System.Security.Claims;
using ClubHub.Api.Data;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Org.OpenAPITools.Models;
using ApiError = Org.OpenAPITools.Models.ApiError;
using VenueReservationEntity = ClubHub.Api.Data.Entities.VenueReservation;

namespace ClubHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/venue-reservations")]
public class VenueReservationsController : ControllerBase
{
    private const string VenueStatusAvailable = "available";
    private const string ClubStatusActive = "active";
    private const string AccountStatusNormal = "normal";
    private const string AccountStatusEnabled = "enabled";
    private const string AccountStatusActive = "active";
    private const string ReservationStatusPending = "pending";
    private const string ReservationStatusApproved = "approved";
    private const string ReservationStatusRejected = "rejected";
    private const string ReservePermission = "venue:reserve";
    private const string ReviewPermission = "venue:review";
    private static readonly TimeZoneInfo BeijingTimeZone = ResolveBeijingTimeZone();

    private static readonly HashSet<string> AllowedReservationStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ReservationStatusPending,
        ReservationStatusApproved,
        ReservationStatusRejected,
        "cancelled"
    };

    private readonly ClubHubDbContext _db;
    private readonly AuthService _authService;

    public VenueReservationsController(ClubHubDbContext db, AuthService authService)
    {
        _db = db;
        _authService = authService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] int? venueId,
        [FromQuery] int? clubId,
        [FromQuery] int? applicantUserId,
        [FromQuery] int? reviewerUserId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        if (!IsValidStatus(status, AllowedReservationStatuses, out var normalizedStatus))
        {
            return BadRequest(Error("reservation_status_invalid", "预约状态参数不合法。"));
        }

        var query = ReservationQuery().AsNoTracking();
        var access = await GetReservationAccessAsync(currentUserId.Value);
        if (access.Error is not null) return access.Error;
        if (!access.CanReview && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("venue_reservation_view_forbidden", "当前用户没有场地预约查看权限。"));
        }

        if (!access.CanReview)
        {
            query = query.Where(r =>
                r.ApplicantUserId == currentUserId.Value || access.ClubIds.Contains(r.ClubId));
        }

        if (normalizedStatus is not null)
        {
            query = query.Where(r => r.ReservationStatus == normalizedStatus);
        }

        if (venueId is not null)
        {
            query = query.Where(r => r.VenueId == venueId);
        }

        if (clubId is not null)
        {
            query = query.Where(r => r.ClubId == clubId);
        }

        if (applicantUserId is not null)
        {
            query = query.Where(r => r.ApplicantUserId == applicantUserId);
        }

        if (reviewerUserId is not null)
        {
            query = query.Where(r => r.ReviewerUserId == reviewerUserId);
        }

        var reservations = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.ReservationId)
            .ToListAsync();

        return Ok(reservations.Select(ToDto));
    }

    [HttpGet("{reservationId:int}")]
    public async Task<IActionResult> GetById(int reservationId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var access = await GetReservationAccessAsync(currentUserId.Value);
        if (access.Error is not null) return access.Error;
        if (!access.CanReview && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("venue_reservation_view_forbidden", "当前用户没有场地预约查看权限。"));
        }

        var reservation = await ReservationQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

        if (reservation is null) return NotFound(Error("reservation_not_found", "预约不存在。"));
        if (!access.CanReview &&
            reservation.ApplicantUserId != currentUserId.Value &&
            !access.ClubIds.Contains(reservation.ClubId))
        {
            return StatusCode(403, Error("venue_reservation_view_forbidden", "当前用户没有查看该预约的权限。"));
        }

        return Ok(ToDto(reservation));
    }

    [HttpGet("occupied-slots")]
    public async Task<IActionResult> GetOccupiedSlots([FromQuery] int? venueId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var access = await GetReservationAccessAsync(currentUserId.Value);
        if (access.Error is not null) return access.Error;
        if (!access.CanReview && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("venue_reservation_slots_forbidden", "当前用户没有场地预约申请或审批权限。"));
        }

        var now = DateTime.UtcNow;
        var query = _db.VenueReservations
            .AsNoTracking()
            .Include(r => r.Venue)
            .Where(r =>
                r.ReservationStatus == ReservationStatusApproved &&
                r.EndAt.HasValue &&
                r.EndAt.Value > now);

        if (venueId is not null)
        {
            query = query.Where(r => r.VenueId == venueId.Value);
        }

        var reservations = await query
            .OrderBy(r => r.StartAt)
            .ThenBy(r => r.ReservationId)
            .ToListAsync();

        return Ok(reservations.Select(r => new VenueOccupiedSlot
        {
            ReservationId = r.ReservationId,
            VenueId = r.VenueId,
            VenueName = r.Venue?.VenueName ?? "",
            StartTime = StoredTimeToUtc(r.StartAt ?? DateTime.MinValue),
            EndTime = StoredTimeToUtc(r.EndAt ?? DateTime.MinValue)
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVenueReservationRequest req)
    {
        var applicantUserId = User.GetUserId();
        if (applicantUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var permission = await _authService.CheckPermissionAsync(applicantUserId.Value, ReservePermission, req.ClubId);
        if (!permission.Succeeded)
        {
            return StatusCode(permission.StatusCode, Error("venue_reservation_permission_check_failed", permission.ErrorMessage ?? "预约权限校验失败。"));
        }

        if (permission.Value?.Allowed != true)
        {
            return StatusCode(403, Error("venue_reservation_forbidden", "只有该社团的干部或负责人可以提交场地预约。"));
        }

        var startTime = RequestTimeToUtc(req.StartTime);
        var endTime = RequestTimeToUtc(req.EndTime);

        var validation = ValidateReservationTime(startTime, endTime);
        if (validation is not null) return validation;

        if (string.IsNullOrWhiteSpace(req.Purpose))
        {
            return BadRequest(Error("purpose_required", "预约用途不能为空。"));
        }

        var venue = await _db.Venues.FindAsync(req.VenueId);
        if (venue is null) return NotFound(Error("venue_not_found", "场地不存在。"));
        if (!string.Equals(NormalizeStatus(venue.VenueStatus), VenueStatusAvailable, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(Error("venue_unavailable", "场地当前不可预约。"));
        }

        var club = await _db.Clubs.FindAsync(req.ClubId);
        if (club is null) return NotFound(Error("club_not_found", "社团不存在。"));
        if (!IsActiveClub(club.ClubStatus))
        {
            return BadRequest(Error("club_not_active", "只有正常运营中的社团可以提交场地预约。"));
        }

        var applicant = await _db.Users.FindAsync(applicantUserId.Value);
        if (applicant is null) return NotFound(Error("applicant_not_found", "申请人不存在。"));
        if (!IsActiveAccount(applicant.AccountStatus))
        {
            return BadRequest(Error("applicant_account_inactive", "申请人账号不可用，不能提交场地预约。"));
        }

        ClubHub.Api.Data.Entities.Activity? activity = null;
        if (req.ActivityId is not null)
        {
            activity = await _db.Activities.FindAsync(req.ActivityId.Value);
            if (activity is null) return NotFound(Error("activity_not_found", "关联活动不存在。"));
            if (activity.ClubId != req.ClubId)
            {
                return BadRequest(Error("activity_club_mismatch", "关联活动不属于该社团。"));
            }
        }

        if (await HasApprovedConflictAsync(req.VenueId, startTime, endTime))
        {
            return Conflict(Error("venue_reservation_conflict", "该场地在所选时间段已有已通过预约。"));
        }

        var reservation = new VenueReservationEntity
        {
            VenueId = req.VenueId,
            ClubId = req.ClubId,
            ActivityId = req.ActivityId,
            ApplicantUserId = applicantUserId.Value,
            StartAt = startTime,
            EndAt = endTime,
            Purpose = req.Purpose.Trim(),
            ReservationStatus = ReservationStatusPending,
            CreatedAt = DateTime.UtcNow,
            Venue = venue,
            Club = club,
            Activity = activity,
            ApplicantUser = applicant
        };

        _db.VenueReservations.Add(reservation);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { reservationId = reservation.ReservationId }, ToDto(reservation));
    }

    [HttpPost("{reservationId:int}/review")]
    public async Task<IActionResult> Review(int reservationId, [FromBody] ReviewVenueReservationRequest req)
    {
        var reviewerUserId = User.GetUserId();
        if (reviewerUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var permission = await _authService.CheckPermissionAsync(reviewerUserId.Value, ReviewPermission, null);
        if (!permission.Succeeded)
        {
            return StatusCode(permission.StatusCode, Error("venue_review_permission_check_failed", permission.ErrorMessage ?? "审批权限校验失败。"));
        }

        if (permission.Value?.Allowed != true)
        {
            return StatusCode(403, Error("venue_review_forbidden", permission.Value?.Message ?? "当前用户没有场地预约审批权限。"));
        }

        var reservation = await ReservationQuery()
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

        if (reservation is null) return NotFound(Error("reservation_not_found", "预约不存在。"));

        if (!string.Equals(NormalizeStatus(reservation.ReservationStatus), ReservationStatusPending, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(Error("reservation_not_pending", "只有待审批的预约可以审核。"));
        }

        var reviewer = await _db.Users.FindAsync(reviewerUserId.Value);
        if (reviewer is null) return NotFound(Error("reviewer_not_found", "审批人不存在。"));

        if (req.Approved)
        {
            if (reservation.StartAt is null || reservation.EndAt is null)
            {
                return BadRequest(Error("reservation_time_invalid", "预约时间数据不完整，不能审批通过。"));
            }

            var startTime = StoredTimeToUtc(reservation.StartAt.Value);
            var endTime = StoredTimeToUtc(reservation.EndAt.Value);

            if (startTime <= DateTime.UtcNow)
            {
                return BadRequest(Error("reservation_start_time_not_future", "预约开始时间必须晚于当前时间，不能审批通过。"));
            }

            if (await HasApprovedConflictAsync(
                    reservation.VenueId,
                    startTime,
                    endTime,
                    reservation.ReservationId))
            {
                return Conflict(Error("venue_reservation_conflict", "审批通过失败：该场地在所选时间段已有已通过预约。"));
            }
        }

        reservation.ReservationStatus = req.Approved ? ReservationStatusApproved : ReservationStatusRejected;
        reservation.ReviewerUserId = reviewerUserId.Value;
        reservation.ReviewerUser = reviewer;
        reservation.ReviewComment = string.IsNullOrWhiteSpace(req.ReviewComment)
            ? null
            : req.ReviewComment.Trim();

        await _db.SaveChangesAsync();

        return Ok(ToDto(reservation));
    }

    [HttpDelete("{reservationId:int}")]
    public async Task<IActionResult> Delete(int reservationId)
    {
        var operatorUserId = User.GetUserId();
        if (operatorUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var operatorUser = await _db.Users.FindAsync(operatorUserId.Value);
        if (operatorUser is null) return NotFound(Error("operator_not_found", "操作人不存在。"));
        if (!IsActiveAccount(operatorUser.AccountStatus))
        {
            return StatusCode(403, Error("operator_account_inactive", "操作人账号不可用。"));
        }

        var reservation = await ReservationQuery()
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

        if (reservation is null) return NotFound(Error("reservation_not_found", "预约不存在。"));

        if (reservation.ApplicantUserId != operatorUserId.Value)
        {
            var permission = await _authService.CheckPermissionAsync(operatorUserId.Value, ReviewPermission, null);
            if (!permission.Succeeded)
            {
                return StatusCode(permission.StatusCode, Error("venue_reservation_delete_permission_check_failed", permission.ErrorMessage ?? "删除预约权限校验失败。"));
            }

            if (permission.Value?.Allowed != true)
            {
                return StatusCode(403, Error("venue_reservation_delete_forbidden", permission.Value?.Message ?? "当前用户没有删除该预约的权限。"));
            }
        }

        if (IsInProgressApprovedReservation(reservation))
        {
            return Conflict(Error("venue_reservation_in_progress_cannot_delete", "已开始且未结束的已通过预约暂时无法删除。"));
        }

        _db.VenueReservations.Remove(reservation);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private IQueryable<VenueReservationEntity> ReservationQuery()
    {
        return _db.VenueReservations
            .Include(r => r.Venue)
            .Include(r => r.Club)
            .Include(r => r.Activity)
            .Include(r => r.ApplicantUser)
            .Include(r => r.ReviewerUser);
    }

    private async Task<bool> HasApprovedConflictAsync(
        int venueId,
        DateTime startTime,
        DateTime endTime,
        int? excludedReservationId = null)
    {
        return await _db.VenueReservations.AnyAsync(r =>
            r.VenueId == venueId &&
            r.ReservationStatus == ReservationStatusApproved &&
            (excludedReservationId == null || r.ReservationId != excludedReservationId) &&
            r.StartAt.HasValue &&
            r.EndAt.HasValue &&
            r.StartAt.Value < endTime &&
            r.EndAt.Value > startTime);
    }

    private IActionResult? ValidateReservationTime(DateTime startTime, DateTime endTime)
    {
        if (startTime == default || endTime == default)
        {
            return BadRequest(Error("reservation_time_required", "预约开始时间和结束时间不能为空。"));
        }

        if (startTime >= endTime)
        {
            return BadRequest(Error("reservation_time_invalid", "预约开始时间必须早于结束时间。"));
        }

        if (startTime < DateTime.UtcNow)
        {
            return BadRequest(Error("reservation_start_time_in_past", "预约开始时间不能早于当前时间。"));
        }

        if (endTime < DateTime.UtcNow)
        {
            return BadRequest(Error("reservation_end_time_in_past", "预约结束时间不能早于当前时间。"));
        }

        return null;
    }

    private static DateTime RequestTimeToUtc(DateTime value)
    {
        if (value == default) return value;

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(value, BeijingTimeZone)
        };
    }

    private static DateTime StoredTimeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static TimeZoneInfo ResolveBeijingTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
    }

    private static VenueReservationDto ToDto(VenueReservationEntity reservation)
    {
        return new VenueReservationDto(
            reservation.ReservationId,
            reservation.VenueId,
            reservation.Venue?.VenueName ?? "",
            reservation.ClubId,
            reservation.Club?.ClubName ?? "",
            reservation.ActivityId,
            reservation.Activity?.Title,
            reservation.ApplicantUserId,
            DisplayName(reservation.ApplicantUser),
            StoredTimeToUtc(reservation.StartAt ?? DateTime.MinValue),
            StoredTimeToUtc(reservation.EndAt ?? DateTime.MinValue),
            reservation.Purpose ?? "",
            NormalizeReservationStatus(reservation.ReservationStatus),
            reservation.ReviewerUserId,
            DisplayName(reservation.ReviewerUser),
            reservation.ReviewComment,
            StoredTimeToUtc(reservation.CreatedAt ?? DateTime.MinValue));
    }

    private static bool IsValidStatus(string? status, HashSet<string> allowed, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        return normalized is null || allowed.Contains(normalized);
    }

    private static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? "" : status.Trim().ToLowerInvariant();
    }

    private static bool IsActiveClub(string? status)
    {
        var normalized = NormalizeStatus(status);
        return normalized is "" or ClubStatusActive or AccountStatusNormal or AccountStatusEnabled or "运营中" or "正常";
    }

    private static bool IsActiveAccount(string? status)
    {
        var normalized = NormalizeStatus(status);
        return normalized is "" or AccountStatusNormal or AccountStatusEnabled or AccountStatusActive or "正常";
    }

    private static string NormalizeReservationStatus(string? status)
    {
        var normalized = NormalizeStatus(status);
        return AllowedReservationStatuses.Contains(normalized) ? normalized : ReservationStatusPending;
    }

    private static string? DisplayName(ClubHub.Api.Data.Entities.User? user)
    {
        if (user is null) return null;
        return string.IsNullOrWhiteSpace(user.RealName) ? user.Username : user.RealName;
    }

    private static bool IsInProgressApprovedReservation(VenueReservationEntity reservation)
    {
        if (!string.Equals(NormalizeReservationStatus(reservation.ReservationStatus), ReservationStatusApproved, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (reservation.StartAt is null || reservation.EndAt is null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        return StoredTimeToUtc(reservation.StartAt.Value) <= now && StoredTimeToUtc(reservation.EndAt.Value) > now;
    }

    private async Task<(IActionResult? Error, bool CanReview, IReadOnlyList<int> ClubIds)> GetReservationAccessAsync(int userId)
    {
        var reviewPermission = await _authService.CheckPermissionAsync(userId, ReviewPermission, null);
        if (!reviewPermission.Succeeded)
        {
            return (
                StatusCode(reviewPermission.StatusCode, Error(
                    "venue_reservation_permission_check_failed",
                    reviewPermission.ErrorMessage ?? "预约权限校验失败。")),
                false,
                []);
        }

        var clubPermission = await _authService.GetPermissionClubIdsAsync(userId, ReservePermission);
        if (!clubPermission.Succeeded)
        {
            return (
                StatusCode(clubPermission.StatusCode, Error(
                    "venue_reservation_permission_check_failed",
                    clubPermission.ErrorMessage ?? "预约权限校验失败。")),
                false,
                []);
        }

        return (null, reviewPermission.Value?.Allowed == true, clubPermission.Value ?? []);
    }

    private static ApiError Error(string code, string message, string? detail = null) => new()
    {
        Code = code,
        Message = message,
        Detail = detail
    };
}

public record VenueReservationDto(
    int Id,
    int VenueId,
    string VenueName,
    int ClubId,
    string ClubName,
    int? ActivityId,
    string? ActivityTitle,
    int ApplicantUserId,
    string? ApplicantName,
    DateTime StartTime,
    DateTime EndTime,
    string Purpose,
    string Status,
    int? ReviewerUserId,
    string? ReviewerName,
    string? ReviewComment,
    DateTime CreatedAt);
