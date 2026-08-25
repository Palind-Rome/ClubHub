using System.Security.Claims;
using ClubHub.Api.Infrastructure.Rest;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Org.OpenAPITools.Models;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IDistributedRateLimiter _rateLimiter;

    public AuthController(AuthService authService, IDistributedRateLimiter rateLimiter)
    {
        _authService = authService;
        _rateLimiter = rateLimiter;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var rateLimit = await AcquireRateLimitAsync(
            "register-ip",
            ClientIp(),
            5,
            TimeSpan.FromHours(1));
        if (rateLimit is not null) return rateLimit;

        var result = await _authService.RegisterAsync(request);
        return ToActionResult(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var accountSubject = (request.Username ?? string.Empty).Trim().ToLowerInvariant();
        var accountLimit = await AcquireRateLimitAsync(
            "login-account",
            accountSubject,
            5,
            TimeSpan.FromMinutes(15));
        if (accountLimit is not null) return accountLimit;
        var ipLimit = await AcquireRateLimitAsync(
            "login-ip",
            ClientIp(),
            20,
            TimeSpan.FromMinutes(15));
        if (ipLimit is not null) return ipLimit;

        var result = await _authService.LoginAsync(request);
        if (result.Succeeded)
        {
            try
            {
                await _rateLimiter.ResetAsync(
                    "login-account",
                    accountSubject,
                    HttpContext.RequestAborted);
                await _rateLimiter.ResetAsync(
                    "login-ip",
                    ClientIp(),
                    HttpContext.RequestAborted);
            }
            catch (RateLimitUnavailableException)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new ApiError { Message = "限流服务暂不可用，请稍后重试。" });
            }
        }
        return ToActionResult(result);
    }

    [Authorize]
    [HttpGet("session")]
    public async Task<IActionResult> GetSession()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(rawUserId, out var userId))
        {
            return Unauthorized(new ApiError { Message = "登录状态已失效，请重新登录。" });
        }

        if (!TryGetBearerToken(out var token))
        {
            return Unauthorized(new ApiError { Message = "登录状态已失效，请重新登录。" });
        }

        var result = await _authService.GetSessionAsync(userId, token);
        return ToActionResult(result);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromServices] AuthTokenService tokenService,
        [FromServices] IAuthSessionService authSessions)
    {
        if (!TryGetBearerToken(out var token) ||
            (!tokenService.TryValidateToken(token, out var principal) &&
             (authSessions.Enabled ||
              !tokenService.TryValidateLegacyToken(token, out principal))))
        {
            return Unauthorized(new ApiError { Message = "登录状态已失效，请重新登录。" });
        }

        try
        {
            await authSessions.RevokeAsync(token, principal, HttpContext.RequestAborted);
            return NoContent();
        }
        catch (Exception ex) when (authSessions.Enabled &&
                                   ex is StackExchange.Redis.RedisException or TimeoutException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiError { Message = "会话服务暂不可用，无法确认注销结果。" });
        }
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _authService.GetRoleDefinitionsAsync();
        return Ok(roles);
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions(
        [FromQuery] string? permission,
        [FromQuery] int? clubId)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return Ok(_authService.GetPermissionCatalog());
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new ApiError { Message = "登录状态已失效，请重新登录。" });
        }

        var result = await _authService.CheckPermissionAsync(userId, permission, clubId);
        return ToActionResult(result);
    }

    [Authorize]
    [HttpGet("permissions/check")]
    public async Task<IActionResult> CheckPermission(
        [FromQuery] string permission,
        [FromQuery] int? clubId)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new ApiError { Message = "登录状态已失效，请重新登录。" });
        }

        var result = await _authService.CheckPermissionAsync(userId, permission, clubId);
        return ToActionResult(result);
    }

    [Authorize]
    [HttpPost("roles/assign")]
    [HttpPost("~/api/v1/users/{userId:int}/roles")]
    public async Task<IActionResult> AssignRole(
        [FromBody] AssignRoleRequest request,
        [FromRoute] int? userId = null)
    {
        if (!TryGetCurrentUserId(out var operatorUserId))
        {
            return Unauthorized(new ApiError { Message = "登录状态已失效，请重新登录。" });
        }

        if (userId is not null && request.TargetUserId > 0 && request.TargetUserId != userId.Value)
        {
            return BadRequest(new ApiError
            {
                Code = ApiErrorCodes.ValidationError,
                Message = "路径用户 ID 与请求体目标用户 ID 不一致。"
            });
        }

        if (userId is not null)
        {
            request.TargetUserId = userId.Value;
        }

        var result = await _authService.AssignRoleAsync(request, operatorUserId);
        return ToActionResult(result);
    }

    private bool TryGetCurrentUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;

    private bool TryGetBearerToken(out string token)
    {
        const string prefix = "Bearer ";
        var value = Request.Headers.Authorization.ToString();
        token = value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..].Trim()
            : string.Empty;
        return token.Length > 0;
    }

    private async Task<IActionResult?> AcquireRateLimitAsync(
        string policy,
        string subject,
        int limit,
        TimeSpan window)
    {
        try
        {
            var decision = await _rateLimiter.AcquireAsync(
                policy,
                subject,
                limit,
                window,
                HttpContext.RequestAborted);
            if (decision.Allowed) return null;

            Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString();
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new ApiError { Message = "请求过于频繁，请稍后重试。" });
        }
        catch (RateLimitUnavailableException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiError { Message = "限流服务暂不可用，请稍后重试。" });
        }
    }

    private string ClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

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
