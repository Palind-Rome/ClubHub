using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Rest;
using ClubHub.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class VenuesAuthorizationTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private static int _sequence;
    private readonly ClubHubWebApplicationFactory _factory;

    public VenuesAuthorizationTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task VersionedDeleteWithoutBodyRequiresBearerIdentity()
    {
        using var client = _factory.CreateClient();

        using var response = await client.DeleteAsync("/api/v1/venues/1");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MaintenanceDeadlineIsPersistedAndClearedWithStatus()
    {
        var (client, userId, venueId) = await SeedVenueAdminAsync();
        var requestedDeadline = DateTime.UtcNow.AddDays(3).AddSeconds(30);
        var deadline = new DateTime(
            requestedDeadline.Year,
            requestedDeadline.Month,
            requestedDeadline.Day,
            requestedDeadline.Hour,
            requestedDeadline.Minute,
            requestedDeadline.Second,
            DateTimeKind.Utc);

        using var maintenanceResponse = await client.PatchAsJsonAsync(
            $"/api/v1/venues/{venueId}/status",
            new
            {
                operatorUserId = userId,
                status = "maintenance",
                maintenanceUntil = requestedDeadline,
                cancelConflictingReservations = false
            });
        var maintenanceBody = await ReadJsonAsync(maintenanceResponse);

        Assert.Equal(HttpStatusCode.OK, maintenanceResponse.StatusCode);
        Assert.Equal("maintenance", maintenanceBody.GetProperty("status").GetString());
        Assert.Equal(deadline, maintenanceBody.GetProperty("maintenanceUntil").GetDateTime());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            var venue = await db.Venues.FindAsync(venueId);
            Assert.Equal(deadline, venue!.MaintenanceUntil);
        }

        using var availableResponse = await client.PatchAsJsonAsync(
            $"/api/v1/venues/{venueId}/status",
            new
            {
                operatorUserId = userId,
                status = "available",
                maintenanceUntil = (DateTime?)null,
                cancelConflictingReservations = false
            });
        var availableBody = await ReadJsonAsync(availableResponse);

        Assert.Equal(HttpStatusCode.OK, availableResponse.StatusCode);
        Assert.Equal("available", availableBody.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, availableBody.GetProperty("maintenanceUntil").ValueKind);

        await using var finalScope = _factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var finalVenue = await finalDb.Venues.FindAsync(venueId);
        Assert.Null(finalVenue!.MaintenanceUntil);
    }

    [Fact]
    public async Task PastMaintenanceDeadlineIsRejectedBeforeStateChange()
    {
        var (client, userId, venueId) = await SeedVenueAdminAsync();

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/venues/{venueId}/status",
            new
            {
                operatorUserId = userId,
                status = "maintenance",
                maintenanceUntil = DateTime.UtcNow.AddMinutes(-1),
                cancelConflictingReservations = false
            });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationError, body.GetProperty("code").GetString());
        Assert.Equal("维护结束时间必须晚于当前时间。", body.GetProperty("message").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var venue = await db.Venues.FindAsync(venueId);
        Assert.Equal("available", venue!.VenueStatus);
        Assert.Null(venue.MaintenanceUntil);
    }

    [Fact]
    public async Task UnzonedMaintenanceDeadlineIsRejectedBeforeStateChange()
    {
        var (client, userId, venueId) = await SeedVenueAdminAsync();
        var unzonedDeadline = DateTime.SpecifyKind(
            DateTime.UtcNow.AddDays(3),
            DateTimeKind.Unspecified);

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/venues/{venueId}/status",
            new
            {
                operatorUserId = userId,
                status = "maintenance",
                maintenanceUntil = unzonedDeadline,
                cancelConflictingReservations = false
            });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorCodes.ValidationError, body.GetProperty("code").GetString());
        Assert.Equal("维护结束时间必须包含时区信息。", body.GetProperty("message").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var venue = await db.Venues.FindAsync(venueId);
        Assert.Equal("available", venue!.VenueStatus);
        Assert.Null(venue.MaintenanceUntil);
    }

    private async Task<(HttpClient Client, int UserId, int VenueId)> SeedVenueAdminAsync()
    {
        var baseId = 780_000 + Interlocked.Increment(ref _sequence) * 10;
        var now = DateTime.UtcNow;
        var userId = baseId;
        var roleId = baseId + 1;
        var venueId = baseId + 2;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        db.Users.Add(new User
        {
            UserId = userId,
            Username = $"venue-admin-{baseId}",
            PasswordHash = "unused",
            RealName = "场地测试管理员",
            AccountStatus = "normal",
            CreatedAt = now
        });
        db.Roles.Add(new Role
        {
            RoleId = roleId,
            RoleCode = "VENUE_ADMIN",
            RoleName = "场地管理员",
            RoleScope = "system",
            CreatedAt = now
        });
        db.UserRoles.Add(new UserRole
        {
            UserRoleId = baseId + 3,
            UserId = userId,
            RoleId = roleId,
            ClubId = null,
            AssignedAt = now
        });
        db.Venues.Add(new Venue
        {
            VenueId = venueId,
            VenueName = "持久化测试场地",
            Capacity = 60,
            VenueStatus = "available",
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateToken(
            new User { UserId = userId, Username = $"venue-admin-{baseId}" });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId, venueId);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
