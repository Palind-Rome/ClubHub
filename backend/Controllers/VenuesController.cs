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
            .Select(v => new VenueDto(
                v.VenueId,
                v.VenueName ?? "",
                v.Building,
                v.RoomNo,
                v.Capacity ?? 0,
                NormalizeVenueStatus(v.VenueStatus),
                v.ManagerUserId,
                v.CreatedAt ?? DateTime.MinValue))
            .ToListAsync();

        return Ok(venues);
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
            .Select(v => new VenueDto(
                v.VenueId,
                v.VenueName ?? "",
                v.Building,
                v.RoomNo,
                v.Capacity ?? 0,
                NormalizeVenueStatus(v.VenueStatus),
                v.ManagerUserId,
                v.CreatedAt ?? DateTime.MinValue))
            .FirstOrDefaultAsync();

        return venue is null
            ? NotFound(Error("venue_not_found", "场地不存在。"))
            : Ok(venue);
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

        venue.VenueStatus = normalizedStatus;
        await _db.SaveChangesAsync();

        return Ok(ToDto(venue));
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
        return new VenueDto(
            venue.VenueId,
            venue.VenueName ?? "",
            venue.Building,
            venue.RoomNo,
            venue.Capacity ?? 0,
            NormalizeVenueStatus(venue.VenueStatus),
            venue.ManagerUserId,
            venue.CreatedAt ?? DateTime.MinValue);
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
    DateTime CreatedAt);

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
    string Status);
