using System.Security.Claims;
using System.Text.Encodings.Web;
using ClubHub.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Org.OpenAPITools.Models;

namespace ClubHub.Api.Services;

public sealed class AuthTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ClubHubBearer";

    private readonly AuthTokenService _authTokenService;
    private readonly IAuthSessionService _authSessions;
    private readonly IPermissionSnapshotCache _permissionSnapshots;
    private readonly ClubHubDbContext _db;

    public AuthTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AuthTokenService authTokenService,
        IAuthSessionService authSessions,
        IPermissionSnapshotCache permissionSnapshots,
        ClubHubDbContext db)
        : base(options, logger, encoder)
    {
        _authTokenService = authTokenService;
        _authSessions = authSessions;
        _permissionSnapshots = permissionSnapshots;
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return TryAuthenticatePreviewCookie();
        }

        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization[bearerPrefix.Length..].Trim();
        if (!_authTokenService.TryValidateToken(token, out var principal) &&
            (_authSessions.Enabled ||
             !_authTokenService.TryValidateLegacyToken(token, out principal)))
        {
            return AuthenticateResult.Fail("Invalid ClubHub token.");
        }

        var validation = await _authSessions.ValidateAndRefreshAsync(
            token,
            principal,
            Context.RequestAborted);
        if (validation == AuthSessionValidation.Unavailable)
        {
            Context.Items[AuthSessionUnavailableItemKey] = true;
            return AuthenticateResult.Fail("Authentication session store unavailable.");
        }

        if (validation != AuthSessionValidation.Valid)
        {
            return AuthenticateResult.Fail("Authentication session has been revoked.");
        }

        if (_authSessions.Enabled)
        {
            var account = await _permissionSnapshots.GetAccountStatusAsync(
                principal.UserId,
                async () =>
                {
                    var status = await _db.Users
                        .AsNoTracking()
                        .Where(user => user.UserId == principal.UserId)
                        .Select(user => new { user.AccountStatus })
                        .SingleOrDefaultAsync();
                    return new AccountStatusSnapshot(
                        status is not null,
                        status?.AccountStatus);
                },
                Context.RequestAborted);
            if (!account.Exists || !IsActiveAccountStatus(account.Status))
            {
                return AuthenticateResult.Fail("Authentication account is disabled or missing.");
            }
        }

        return CreateSuccessResult(principal);
    }

    private static bool IsActiveAccountStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Trim().ToLowerInvariant() is "active" or "normal" or "enabled" or "在任" or "正常";

    private AuthenticateResult TryAuthenticatePreviewCookie()
    {
        if (!HttpMethods.IsGet(Request.Method) ||
            Request.Path.Value?.EndsWith("/preview", StringComparison.OrdinalIgnoreCase) != true ||
            !int.TryParse(Request.RouteValues["itemId"]?.ToString(), out var itemId) ||
            itemId <= 0 ||
            !Request.Cookies.TryGetValue(AuthTokenService.PreviewCookieName, out var token) ||
            !_authTokenService.TryValidatePreviewToken(token, itemId, out var principal))
        {
            return AuthenticateResult.NoResult();
        }

        return CreateSuccessResult(principal);
    }

    private static AuthenticateResult CreateSuccessResult(AuthTokenPrincipal principal)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.UserId.ToString())
        };

        if (!string.IsNullOrWhiteSpace(principal.Username))
        {
            claims.Add(new Claim(ClaimTypes.Name, principal.Username));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var unavailable = Context.Items.ContainsKey(AuthSessionUnavailableItemKey);
        Response.StatusCode = unavailable
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new ApiError
        {
            Code = unavailable ? "auth_session_unavailable" : "authentication_required",
            Message = unavailable
                ? "会话服务暂不可用，请稍后重试。"
                : "登录状态已失效，请重新登录。"
        });
    }

    private const string AuthSessionUnavailableItemKey = "clubhub.auth-session-unavailable";
}
