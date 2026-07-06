using ClubHub.Api.Data;
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
                a.Club != null ? a.Club.ClubName : "",
                a.StartAt,
                a.EndAt,
                a.Location,
                a.ActivityStatus,
                a.Capacity,
                a.CreatedAt
            ))
            .ToListAsync();

        return Ok(activities);
    }

    [HttpGet("{activityId:int}")]
    public async Task<IActionResult> GetById(int activityId)
    {
        var activity = await _db.Activities
            .Where(a => a.ActivityId == activityId)
            .Select(a => new ActivityDto(
                a.ActivityId,
                a.Title,
                a.Club != null ? a.Club.ClubName : "",
                a.StartAt,
                a.EndAt,
                a.Location,
                a.ActivityStatus,
                a.Capacity,
                a.CreatedAt
            ))
            .FirstOrDefaultAsync();

        return activity is null ? NotFound() : Ok(activity);
    }
}

public record ActivityDto(
    int Id,
    string Title,
    string ClubName,
    DateTime? StartTime,
    DateTime? EndTime,
    string? Location,
    string? Status,
    int? MaxParticipants,
    DateTime CreatedAt
);
