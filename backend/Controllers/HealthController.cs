using Microsoft.AspNetCore.Mvc;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new HealthStatus("healthy", DateTime.UtcNow));
    }
}

/// <summary>
/// 健康检查响应。与 api/openapi.yaml 中的 HealthStatus schema 对应。
/// </summary>
public record HealthStatus(string Status, DateTime Timestamp);
