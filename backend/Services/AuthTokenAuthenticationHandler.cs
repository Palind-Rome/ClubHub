using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Org.OpenAPITools.Models;

namespace ClubHub.Api.Services;

public sealed class AuthTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ClubHubBearer";

    private readonly AuthTokenService _authTokenService;

    public AuthTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AuthTokenService authTokenService)
        : base(options, logger, encoder)
    {
        _authTokenService = authTokenService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return Task.FromResult(TryAuthenticatePreviewCookie());
        }

        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = authorization[bearerPrefix.Length..].Trim();
        if (!_authTokenService.TryValidateToken(token, out var principal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid ClubHub token."));
        }

        return Task.FromResult(CreateSuccessResult(principal));
    }

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
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new ApiError
        {
            Code = "authentication_required",
            Message = "登录状态已失效，请重新登录。"
        });
    }
}
