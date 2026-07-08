using ClubHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Org.OpenAPITools.Models;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService) => _authService = authService;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        return ToActionResult(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        return ToActionResult(result);
    }

    [HttpGet("session")]
    public async Task<IActionResult> GetSession([FromQuery] int userId)
    {
        var result = await _authService.GetSessionAsync(userId);
        return ToActionResult(result);
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _authService.GetRoleDefinitionsAsync();
        return Ok(roles);
    }

    [HttpGet("permissions")]
    public IActionResult GetPermissions()
    {
        return Ok(_authService.GetPermissionCatalog());
    }

    [HttpGet("permissions/check")]
    public async Task<IActionResult> CheckPermission(
        [FromQuery] int userId,
        [FromQuery] string permission,
        [FromQuery] int? clubId)
    {
        var result = await _authService.CheckPermissionAsync(userId, permission, clubId);
        return ToActionResult(result);
    }

    [HttpPost("roles/assign")]
    public async Task<IActionResult> AssignRole([FromBody] AssignRoleRequest request)
    {
        var result = await _authService.AssignRoleAsync(request);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult<T>(AuthServiceResult<T> result)
    {
        if (result.Succeeded)
        {
            return StatusCode(result.StatusCode, result.Value);
        }

        return StatusCode(
            result.StatusCode,
            new ApiError { Message = result.ErrorMessage ?? "请求处理失败。" });
    }
}
