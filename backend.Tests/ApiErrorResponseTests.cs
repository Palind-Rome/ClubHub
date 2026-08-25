using System.Net;
using System.Text.Json;
using ClubHub.Api.Infrastructure.Rest;

namespace ClubHub.Api.Tests;

public sealed class ApiErrorResponseTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiErrorResponseTests(ClubHubWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task StringValidationErrorIsConvertedToChineseApiError()
    {
        using var response = await _client.GetAsync("/api/v1/projects?page=0");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationError, body.RootElement.GetProperty("code").GetString());
        Assert.Equal("请求参数不合法。", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("Page must be greater than 0.", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task NotFoundErrorWithoutChineseMessageUsesStandardApiError()
    {
        using var response = await _client.GetAsync("/api/v1/projects/999");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiErrorCodes.NotFound, body.RootElement.GetProperty("code").GetString());
        Assert.Equal("请求的资源不存在。", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("Project does not exist.", body.RootElement.GetProperty("detail").GetString());
    }
}
