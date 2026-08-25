using System.Net;
using System.Net.Http.Headers;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class LearningDownloadSafetyTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;

    public LearningDownloadSafetyTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task FileGetDoesNotMutateLearningStateWhenDeliveryFails()
    {
        var (client, itemId, recordId) = await SeedAndAuthenticateAsync();

        using var response = await client.GetAsync($"/api/v1/learning/items/{itemId}/file?download=true");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var record = await db.LearningRecords.AsNoTracking().SingleAsync(item => item.RecordId == recordId);
        Assert.Equal("cancelled", record.EnrollStatus);
        Assert.Null(record.DownloadedAt);
        Assert.False(await db.OperationLogs.AnyAsync(log => log.TargetId == itemId));
    }

    [Fact]
    public async Task DownloadAuditDoesNotCreateOrReactivateLearningRecord()
    {
        var (client, itemId, recordId) = await SeedAndAuthenticateAsync();

        using var response = await client.PostAsync($"/api/v1/learning/items/{itemId}/downloads", null);

        response.EnsureSuccessStatusCode();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var record = await db.LearningRecords.AsNoTracking().SingleAsync(item => item.RecordId == recordId);
        Assert.Equal("cancelled", record.EnrollStatus);
        Assert.Null(record.DownloadedAt);
        Assert.Equal(
            1,
            await db.OperationLogs.CountAsync(log =>
                log.TargetId == itemId && log.OperationType == "download"));
    }

    private async Task<(HttpClient Client, int ItemId, int RecordId)> SeedAndAuthenticateAsync()
    {
        var suffix = Random.Shared.Next(100_000, 900_000);
        var userId = 1_700_000 + suffix;
        var clubId = 2_700_000 + suffix;
        var itemId = 3_700_000 + suffix;
        var recordId = 4_700_000 + suffix;
        const string username = "learning-download-safety";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            var studentRoleId = await db.Roles
                .Where(role => role.RoleCode == "STUDENT")
                .Select(role => role.RoleId)
                .SingleAsync();
            var now = DateTime.UtcNow;
            db.AddRange(
                new User
                {
                    UserId = userId,
                    Username = $"{username}-{suffix}",
                    PasswordHash = "not-used",
                    RealName = "下载安全测试",
                    AccountStatus = "normal",
                    CreatedAt = now
                },
                new Club
                {
                    ClubId = clubId,
                    ClubName = $"下载安全测试社团-{suffix}",
                    ClubStatus = "normal",
                    CreatedAt = now
                },
                new UserRole
                {
                    UserRoleId = 5_700_000 + suffix,
                    UserId = userId,
                    RoleId = studentRoleId,
                    AssignedAt = now
                },
                new LearningItem
                {
                    ItemId = itemId,
                    ClubId = clubId,
                    UploaderUserId = userId,
                    Title = "下载安全测试资源",
                    ItemType = "document",
                    FileUrl = $"/api/learning/items/{itemId}/file",
                    Visibility = "public",
                    DownloadPermission = "allow",
                    ItemStatus = "published",
                    CreatedAt = now
                },
                new LearningRecord
                {
                    RecordId = recordId,
                    ItemId = itemId,
                    UserId = userId,
                    EnrollStatus = "cancelled",
                    EnrolledAt = now.AddDays(-1)
                });
            await db.SaveChangesAsync();
        }

        using var tokenScope = _factory.Services.CreateScope();
        var token = tokenScope.ServiceProvider
            .GetRequiredService<AuthTokenService>()
            .CreateToken(new User { UserId = userId, Username = $"{username}-{suffix}" });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, itemId, recordId);
    }
}
