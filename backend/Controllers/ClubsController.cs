using Microsoft.AspNetCore.Mvc;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClubsController : ControllerBase
{
    private static readonly List<ClubDto> Clubs =
    [
        new(1, "计算机协会", "编程竞赛、技术分享、黑客松组织", "学术科技", 42, "张三", new DateTime(2024, 9, 1)),
        new(2, "摄影社", "校园摄影采风、人像摄影教学、作品展览", "文化艺术", 28, "李四", new DateTime(2024, 9, 15)),
        new(3, "羽毛球协会", "每周训练、校内联赛、校际交流赛", "体育竞技", 35, "王五", new DateTime(2024, 3, 10)),
    ];

    [HttpGet]
    public IActionResult GetAll() => Ok(Clubs);

    [HttpGet("{clubId:int}")]
    public IActionResult GetById(int clubId)
    {
        var club = Clubs.FirstOrDefault(c => c.Id == clubId);
        return club is null ? NotFound() : Ok(club);
    }
}

public record ClubDto(
    int Id,
    string Name,
    string? Description,
    string Category,
    int MemberCount,
    string PresidentName,
    DateTime? EstablishedAt
);
