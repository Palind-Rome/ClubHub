using ClubHub.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiError = Org.OpenAPITools.Models.ApiError;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/venues")]
public class VenuesController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "available",
        "disabled",
        "maintenance"
    };

    private readonly ClubHubDbContext _db;

    public VenuesController(ClubHubDbContext db) => _db = db;

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
