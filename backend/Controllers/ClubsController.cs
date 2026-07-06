using ClubHub.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClubsController : ControllerBase
{
    private readonly ClubHubDbContext _db;

    public ClubsController(ClubHubDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var clubs = await _db.Clubs
            .OrderBy(c => c.ClubId)
            .Select(c => new ClubDto(
                c.ClubId,
                c.ClubName,
                c.Description,
                c.Category,
                c.ClubStatus,
                c.FoundedAt,
                c.CreatedAt
            ))
            .ToListAsync();

        return Ok(clubs);
    }

    [HttpGet("{clubId:int}")]
    public async Task<IActionResult> GetById(int clubId)
    {
        var club = await _db.Clubs
            .Where(c => c.ClubId == clubId)
            .Select(c => new ClubDto(
                c.ClubId,
                c.ClubName,
                c.Description,
                c.Category,
                c.ClubStatus,
                c.FoundedAt,
                c.CreatedAt
            ))
            .FirstOrDefaultAsync();

        return club is null ? NotFound() : Ok(club);
    }
}

public record ClubDto(
    int Id,
    string Name,
    string? Description,
    string? Category,
    string? Status,
    DateTime? FoundedAt,
    DateTime CreatedAt
);
