using System.Net;
using System.Text.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Rest;
using ClubHub.Api.Services;
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

        using var response = await client.GetAsync(
            "/api/v1/venues?status=maintenance&page=2&pageSize=1");
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

    [Fact]
    public async Task PermissionCatalogIsNotSilentlyTruncated()
    {
        using var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var expectedCount = scope.ServiceProvider
            .GetRequiredService<AuthService>()
            .GetPermissionCatalog()
            .Count;

        using var response = await client.GetAsync("/api/v1/auth/permissions");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        response.EnsureSuccessStatusCode();
        Assert.True(expectedCount > ApiPaginationResultFilter.DefaultPageSize);
        Assert.Equal(expectedCount, body.RootElement.GetArrayLength());
        Assert.False(response.Headers.Contains("X-Page"));
    }

    [Fact]
    public async Task CollectionWithoutPaginationQueryReturnsRecordsBeyondDefaultPageSize()
    {
        const int recordCount = ApiPaginationResultFilter.DefaultPageSize + 10;
        await SeedVenuesAsync(recordCount);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/venues");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var seededVenueIds = body.RootElement
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .Where(id => id is > 14_300 and <= 14_300 + recordCount)
            .ToArray();

        response.EnsureSuccessStatusCode();
        Assert.Equal(recordCount, seededVenueIds.Length);
        Assert.False(response.Headers.Contains("X-Page"));
    }

    private Task SeedVenuesAsync() => SeedVenuesAsync(3, "maintenance");

    private async Task SeedVenuesAsync(int count, string status = "available")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var existingVenues = db.Venues
            .Where(venue => venue.VenueId > 14_300 && venue.VenueId <= 14_300 + count)
            .ToList();
        foreach (var venue in existingVenues)
        {
            venue.VenueStatus = status;
        }

        var existingIds = existingVenues.Select(venue => venue.VenueId).ToHashSet();
        db.Venues.AddRange(Enumerable.Range(1, count)
            .Where(index => !existingIds.Contains(14_300 + index))
            .Select(index => new Venue
            {
                VenueId = 14_300 + index,
                VenueName = $"分页场地 {index}",
                VenueStatus = status,
                CreatedAt = DateTime.UtcNow
            }));
        await db.SaveChangesAsync();
    }
}
