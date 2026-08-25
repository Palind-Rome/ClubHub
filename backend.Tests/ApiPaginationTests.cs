using System.Net;
using System.Text.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Rest;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class ApiPaginationTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;

    public ApiPaginationTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task CollectionEndpointReturnsRequestedPageAndMetadata()
    {
        await SeedVenuesAsync();
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/venues?page=2&pageSize=1");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        response.EnsureSuccessStatusCode();
        Assert.Equal("2", response.Headers.GetValues("X-Page").Single());
        Assert.Equal("1", response.Headers.GetValues("X-Page-Size").Single());
        Assert.Equal("3", response.Headers.GetValues("X-Total-Count").Single());
        Assert.Contains("rel=\"prev\"", response.Headers.GetValues("Link").Single());
        Assert.Contains("rel=\"next\"", response.Headers.GetValues("Link").Single());
        Assert.Equal("分页场地 2", body.RootElement[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task InvalidPaginationReturnsStructuredValidationError()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/v1/venues?page=1&pageSize={ApiPaginationResultFilter.MaximumPageSize + 1}");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationError, body.RootElement.GetProperty("code").GetString());
    }

    private async Task SeedVenuesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        if (db.Venues.Any(venue => venue.VenueName!.StartsWith("分页场地 ")))
        {
            return;
        }

        db.Venues.AddRange(Enumerable.Range(1, 3).Select(index => new Venue
        {
            VenueId = 14_300 + index,
            VenueName = $"分页场地 {index}",
            VenueStatus = "available",
            CreatedAt = DateTime.UtcNow
        }));
        await db.SaveChangesAsync();
    }
}
