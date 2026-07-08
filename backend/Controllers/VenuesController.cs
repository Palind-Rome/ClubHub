using System.Collections.Concurrent;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiError = Org.OpenAPITools.Models.ApiError;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/venues")]
public class VenuesController : ControllerBase
{
    private const string VenueCreatePermission = "venue:create";
    private const string VenueUpdatePermission = "venue:update";
    private const string VenueDisablePermission = "venue:disable";
    private const string ReservationStatusPending = "pending";
    private const string ReservationStatusApproved = "approved";
    private const string ReservationStatusCancelled = "cancelled";
    private static readonly TimeZoneInfo BeijingTimeZone = ResolveBeijingTimeZone();
    private static readonly ConcurrentDictionary<int, DateTime> MaintenanceUntilByVenueId = new();

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "available",
        "disabled",
        "maintenance"
    };

    private readonly ClubHubDbContext _db;
    private readonly AuthService _authService;

    public VenuesController(ClubHubDbContext db, AuthService authService)
    {
        _db = db;
        _authService = authService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        if (!IsValidStatus(status, AllowedStatuses, out var normalizedStatus))
        {
            return BadRequest(Error("venue_status_invalid", "场地状态参数不合法。"));
        }

        var query = _db.Venues.AsNoTracking();
        if (normalizedStatus is not null)
        {
            query = query.Where(v => v.VenueStatus == normalizedStatus);
        }

        var venues = await query
            .OrderBy(v => v.VenueId)
            .ToListAsync();

        return Ok(venues.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVenueRequest req)
    {
        var permission = await RequirePermissionAsync(req.OperatorUserId, VenueCreatePermission, "当前用户没有创建场地权限。");
        if (permission is not null) return permission;

        if (!ValidateVenueInput(req.Name, req.Capacity, out var normalizedName, out var validationError))
        {
            return validationError!;
        }

        if (!IsValidStatus(req.Status, AllowedStatuses, out var normalizedStatus))
        {
            return BadRequest(Error("venue_status_invalid", "场地状态参数不合法。"));
        }

        var managerValidation = await ValidateManagerAsync(req.ManagerUserId);
        if (managerValidation is not null) return managerValidation;

        var nextId = (await _db.Venues.MaxAsync(v => (int?)v.VenueId) ?? 0) + 1;
        var venue = new Venue
        {
            VenueId = nextId,
            VenueName = normalizedName,
            Building = NullIfBlank(req.Building),
            RoomNo = NullIfBlank(req.RoomNo),
            Capacity = req.Capacity,
            VenueStatus = normalizedStatus ?? "available",
            ManagerUserId = req.ManagerUserId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Venues.Add(venue);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { venueId = venue.VenueId }, ToDto(venue));
    }

    [HttpGet("{venueId:int}")]
    public async Task<IActionResult> GetById(int venueId)
    {
        var venue = await _db.Venues
            .AsNoTracking()
            .Where(v => v.VenueId == venueId)
            .FirstOrDefaultAsync();

        return venue is null
            ? NotFound(Error("venue_not_found", "场地不存在。"))
            : Ok(ToDto(venue));
    }

    [HttpPut("{venueId:int}")]
    public async Task<IActionResult> Update(int venueId, [FromBody] UpdateVenueRequest req)
    {
        var permission = await RequirePermissionAsync(req.OperatorUserId, VenueUpdatePermission, "当前用户没有维护场地权限。");
        if (permission is not null) return permission;

        if (!ValidateVenueInput(req.Name, req.Capacity, out var normalizedName, out var validationError))
        {
            return validationError!;
        }

        var managerValidation = await ValidateManagerAsync(req.ManagerUserId);
        if (managerValidation is not null) return managerValidation;

        var venue = await _db.Venues.FindAsync(venueId);
        if (venue is null) return NotFound(Error("venue_not_found", "场地不存在。"));

        venue.VenueName = normalizedName;
        venue.Building = NullIfBlank(req.Building);
        venue.RoomNo = NullIfBlank(req.RoomNo);
        venue.Capacity = req.Capacity;
        venue.ManagerUserId = req.ManagerUserId;

        await _db.SaveChangesAsync();
        return Ok(ToDto(venue));
    }

    [HttpPatch("{venueId:int}/status")]
    public async Task<IActionResult> UpdateStatus(int venueId, [FromBody] UpdateVenueStatusRequest req)
    {
        var permission = await RequirePermissionAsync(req.OperatorUserId, VenueDisablePermission, "当前用户没有停用或恢复场地权限。");
        if (permission is not null) return permission;

        if (!IsValidStatus(req.Status, AllowedStatuses, out var normalizedStatus) || normalizedStatus is null)
        {
            return BadRequest(Error("venue_status_invalid", "场地状态参数不合法。"));
        }

        var venue = await _db.Venues.FindAsync(venueId);
        if (venue is null) return NotFound(Error("venue_not_found", "场地不存在。"));

        var conflicts = await StatusConflictQuery(venueId, normalizedStatus, req.MaintenanceUntil)
            .ToListAsync();
        if (conflicts.Count > 0 && req.CancelConflictingReservations != true)
        {
            return Conflict(Error("venue_status_conflict_reservations", "该场地后续存在与状态变更冲突的预约。"));
        }

        if (conflicts.Count > 0)
        {
            foreach (var reservation in conflicts)
            {
                reservation.ReservationStatus = ReservationStatusCancelled;
                reservation.ReviewComment = NullIfBlank(reservation.ReviewComment) ?? "场地状态变更，预约自动取消。";
            }
        }

        venue.VenueStatus = normalizedStatus;
        UpdateMaintenanceUntil(venueId, normalizedStatus, req.MaintenanceUntil);
        await _db.SaveChangesAsync();

        return Ok(ToDto(venue));
    }

    [HttpDelete("{venueId:int}")]
    public async Task<IActionResult> Delete(int venueId, [FromBody] DeleteVenueRequest req)
    {
        var permission = await RequirePermissionAsync(req.OperatorUserId, VenueDisablePermission, "当前用户没有删除场地权限。");
        if (permission is not null) return permission;

        var venue = await _db.Venues.FindAsync(venueId);
        if (venue is null) return NotFound(Error("venue_not_found", "场地不存在。"));

        var hasReservations = await _db.VenueReservations.AnyAsync(r => r.VenueId == venueId);
        if (hasReservations)
        {
            return Conflict(Error("venue_has_reservations", "该场地已有预约记录，不能删除。"));
        }

        _db.Venues.Remove(venue);
        MaintenanceUntilByVenueId.TryRemove(venueId, out _);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static bool IsValidStatus(string? status, HashSet<string> allowed, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        return normalized is null || allowed.Contains(normalized);
    }

    private static string NormalizeVenueStatus(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? "available" : status.Trim().ToLowerInvariant();
        return AllowedStatuses.Contains(normalized) ? normalized : "available";
    }

    private IQueryable<ClubHub.Api.Data.Entities.VenueReservation> StatusConflictQuery(
        int venueId,
        string status,
        DateTime? maintenanceUntil)
    {
        var now = DateTime.UtcNow;
        var query = _db.VenueReservations.Where(r =>
            r.VenueId == venueId &&
            (r.ReservationStatus == ReservationStatusPending || r.ReservationStatus == ReservationStatusApproved) &&
            r.EndAt.HasValue &&
            r.EndAt.Value > now);

        if (status == "maintenance" && maintenanceUntil is not null)
        {
            var maintenanceUntilUtc = RequestTimeToUtc(maintenanceUntil.Value);
            query = query.Where(r => r.StartAt.HasValue && r.StartAt.Value < maintenanceUntilUtc);
        }

        if (status != "disabled" && status != "maintenance")
        {
            query = query.Where(r => false);
        }

        return query;
    }

    private static void UpdateMaintenanceUntil(int venueId, string status, DateTime? maintenanceUntil)
    {
        if (status != "maintenance" || maintenanceUntil is null)
        {
            MaintenanceUntilByVenueId.TryRemove(venueId, out _);
            return;
        }

        MaintenanceUntilByVenueId[venueId] = RequestTimeToUtc(maintenanceUntil.Value);
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

    private async Task<IActionResult?> RequirePermissionAsync(int operatorUserId, string permissionCode, string forbiddenMessage)
    {
        var permission = await _authService.CheckPermissionAsync(operatorUserId, permissionCode, null);
        if (!permission.Succeeded)
        {
            return StatusCode(permission.StatusCode, Error("venue_permission_check_failed", permission.ErrorMessage ?? "场地权限校验失败。"));
        }

        if (permission.Value?.Allowed != true)
        {
            return StatusCode(403, Error("venue_permission_forbidden", permission.Value?.Message ?? forbiddenMessage));
        }

        return null;
    }

    private async Task<IActionResult?> ValidateManagerAsync(int? managerUserId)
    {
        if (managerUserId is null) return null;

        return await _db.Users.AnyAsync(user => user.UserId == managerUserId)
            ? null
            : NotFound(Error("venue_manager_not_found", "场地负责人不存在。"));
    }

    private static bool ValidateVenueInput(
        string? name,
        int capacity,
        out string normalizedName,
        out IActionResult? error)
    {
        normalizedName = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
        error = null;

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            error = new BadRequestObjectResult(Error("venue_name_required", "场地名称不能为空。"));
            return false;
        }

        if (normalizedName.Length > 255)
        {
            error = new BadRequestObjectResult(Error("venue_name_too_long", "场地名称不能超过 255 个字符。"));
            return false;
        }

        if (capacity <= 0)
        {
            error = new BadRequestObjectResult(Error("venue_capacity_invalid", "场地容量必须大于 0。"));
            return false;
        }

        return true;
    }

    private static VenueDto ToDto(Venue venue)
    {
        var status = NormalizeVenueStatus(venue.VenueStatus);
        return new VenueDto(
            venue.VenueId,
            venue.VenueName ?? "",
            venue.Building,
            venue.RoomNo,
            venue.Capacity ?? 0,
            status,
            venue.ManagerUserId,
            venue.CreatedAt ?? DateTime.MinValue,
            status == "maintenance" && MaintenanceUntilByVenueId.TryGetValue(venue.VenueId, out var maintenanceUntil)
                ? maintenanceUntil
                : null);
    }

    private static string? NullIfBlank(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static ApiError Error(string code, string message, string? detail = null) => new()
    {
        Code = code,
        Message = message,
        Detail = detail
    };
}

public record VenueDto(
    int Id,
    string Name,
    string? Building,
    string? RoomNo,
    int Capacity,
    string Status,
    int? ManagerUserId,
    DateTime CreatedAt,
    DateTime? MaintenanceUntil);

public record CreateVenueRequest(
    int OperatorUserId,
    string Name,
    string? Building,
    string? RoomNo,
    int Capacity,
    string? Status,
    int? ManagerUserId);

public record UpdateVenueRequest(
    int OperatorUserId,
    string Name,
    string? Building,
    string? RoomNo,
    int Capacity,
    int? ManagerUserId);

public record UpdateVenueStatusRequest(
    int OperatorUserId,
    string Status,
    DateTime? MaintenanceUntil,
    bool? CancelConflictingReservations);

public record DeleteVenueRequest(
    int OperatorUserId);
