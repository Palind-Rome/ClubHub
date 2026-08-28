using System.Net;
using System.Text.Json;
using ClubHub.Api.Infrastructure.Rest;

namespace ClubHub.Api.Tests;

public sealed class AuthCaptchaEndpointTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthCaptchaEndpointTests(ClubHubWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task CaptchaEndpointReturnsNoStoreChallenge()
    {
        using var response = await _client.GetAsync("/api/v1/auth/captcha");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(body.RootElement.TryGetProperty("captchaToken", out var token));
        Assert.True(body.RootElement.TryGetProperty("image", out var image));
        Assert.True(body.RootElement.TryGetProperty("expiresAt", out var expiresAt));
        Assert.True(token.GetString()?.Length >= 40);
        Assert.StartsWith("data:image/svg+xml;base64,", image.GetString());
        Assert.False(string.IsNullOrWhiteSpace(expiresAt.GetString()));
    }

    [Fact]
    public async Task LoginRejectsInvalidCaptchaBeforeCredentialLookup()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                username = "does-not-matter",
                password = "wrong-password",
                captchaToken = "invalid-captcha-token",
                captchaCode = "23456"
            });
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationError, body.RootElement.GetProperty("code").GetString());
        Assert.Equal("验证码无效或已过期，请刷新后重试。", body.RootElement.GetProperty("message").GetString());
    }
}
