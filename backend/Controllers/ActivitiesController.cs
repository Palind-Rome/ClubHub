using Microsoft.AspNetCore.Mvc;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActivitiesController : ControllerBase
{
    private static readonly List<ActivityDto> Activities =
    [
        new(1, "2025 秋季 Hackathon", "计算机协会", new DateTime(2025, 10, 15, 9, 0, 0), new DateTime(2025, 10, 15, 18, 0, 0), "大学生活动中心 301", "published", 60, 38),
        new(2, "校园摄影大赛作品展", "摄影社", new DateTime(2025, 11, 1, 10, 0, 0), new DateTime(2025, 11, 3, 17, 0, 0), "图书馆一楼展厅", "published", null, 12),
        new(3, "羽毛球新生杯", "羽毛球协会", new DateTime(2025, 10, 20, 14, 0, 0), new DateTime(2025, 10, 20, 17, 0, 0), "体育馆羽毛球场", "draft", 32, 0),
    ];

    [HttpGet]
    public IActionResult GetAll() => Ok(Activities);

    [HttpGet("{activityId:int}")]
    public IActionResult GetById(int activityId)
    {
        var activity = Activities.FirstOrDefault(a => a.Id == activityId);
        return activity is null ? NotFound() : Ok(activity);
    }
}

public record ActivityDto(
    int Id,
    string Title,
    string ClubName,
    DateTime StartTime,
    DateTime EndTime,
    string? Location,
    string Status,
    int? MaxParticipants,
    int CurrentParticipants
);
