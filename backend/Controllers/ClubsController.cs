using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
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
                c.ClubId, c.ClubName, c.Description, c.Category, c.ClubStatus, c.FoundedAt, c.CreatedAt))
            .ToListAsync();
        return Ok(clubs);
    }

    [HttpGet("{clubId:int}")]
    public async Task<IActionResult> GetById(int clubId)
    {
        var club = await _db.Clubs
            .Where(c => c.ClubId == clubId)
            .Select(c => new ClubDto(
                c.ClubId, c.ClubName, c.Description, c.Category, c.ClubStatus, c.FoundedAt, c.CreatedAt))
            .FirstOrDefaultAsync();
        return club is null ? NotFound() : Ok(club);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClubRequest req)
    {
        // 获取下一个 ID
        var maxId = await _db.Clubs.MaxAsync(c => (int?)c.ClubId) ?? 0;
        var club = new Club
        {
            ClubId = maxId + 1,
            ClubName = req.Name,
            Category = req.Category,
            Description = req.Description,
            ClubStatus = "active",
            FoundedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.Clubs.Add(club);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { clubId = club.ClubId }, new ClubDto(
            club.ClubId, club.ClubName, club.Description, club.Category, club.ClubStatus, club.FoundedAt, club.CreatedAt));
    }

    [HttpPut("{clubId:int}")]
    public async Task<IActionResult> Update(int clubId, [FromBody] UpdateClubRequest req)
    {
        var club = await _db.Clubs.FindAsync(clubId);
        if (club is null) return NotFound();

        if (req.Name is not null) club.ClubName = req.Name;
        if (req.Category is not null) club.Category = req.Category;
        if (req.Description is not null) club.Description = req.Description;

        await _db.SaveChangesAsync();
        return Ok(new ClubDto(
            club.ClubId, club.ClubName, club.Description, club.Category, club.ClubStatus, club.FoundedAt, club.CreatedAt));
    }

    [HttpDelete("{clubId:int}")]
    public async Task<IActionResult> Delete(int clubId)
    {
        var club = await _db.Clubs.FindAsync(clubId);
        if (club is null) return NotFound();

        _db.Clubs.Remove(club);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record ClubDto(int Id, string Name, string? Description, string? Category, string? Status, DateTime? FoundedAt, DateTime CreatedAt);

public class CreateClubRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
}

public class UpdateClubRequest
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
}
