using System.Net;
using System.Net.Http.Headers;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Redis;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace ClubHub.Api.Tests;

public sealed class LearningPreviewEndpointTests
{
    private enum RateLimiterMode
    {
        Allowed,
        Rejected,
        Unavailable
    }

    [Fact]
    public async Task PreviewSession_WhenRateLimitIsUnavailable_Returns503WithoutPreparingFile()
    {
        var storage = new PreviewObjectStorage();
        await using var factory = CreateFactory(
            new TestRateLimiter(RateLimiterMode.Unavailable),
            storage,
            previewRedisUnavailable: false);
        var (client, itemId) = await SeedAndAuthenticateAsync(factory);

        using var response = await client.PostAsync(
            $"/api/learning/items/{itemId}/preview-session",
            null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, storage.MetadataReads);
    }

    [Fact]
    public async Task PreviewSession_WhenRateLimitRejects_Returns429AndRetryAfter()
    {
        var storage = new PreviewObjectStorage();
        await using var factory = CreateFactory(
            new TestRateLimiter(RateLimiterMode.Rejected),
            storage,
            previewRedisUnavailable: false);
        var (client, itemId) = await SeedAndAuthenticateAsync(factory);

        using var response = await client.PostAsync(
            $"/api/learning/items/{itemId}/preview-session",
            null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("37", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString() ??
            response.Headers.GetValues("Retry-After").Single());
        Assert.Equal(0, storage.MetadataReads);
    }

    [Fact]
    public async Task PreviewSession_WhenRedisStoreIsUnavailable_Returns503WithoutLocalFallback()
    {
        var storage = new PreviewObjectStorage();
        await using var factory = CreateFactory(
            new TestRateLimiter(RateLimiterMode.Allowed),
            storage,
            previewRedisUnavailable: true);
        var (client, itemId) = await SeedAndAuthenticateAsync(factory);

        using var response = await client.PostAsync(
            $"/api/learning/items/{itemId}/preview-session",
            null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, storage.MetadataReads);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IDistributedRateLimiter limiter,
        ILearningObjectStorage storage,
        bool previewRedisUnavailable)
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"LearningPreviewEndpoint-{Guid.NewGuid():N}";
        var baseFactory = new ClubHubWebApplicationFactory();
        return baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Redis:Enabled"] = previewRedisUnavailable.ToString(),
                    ["Redis:ConnectionString"] = "127.0.0.1:1",
                    ["Redis:Password"] = "preview-endpoint-test-password",
                    ["Redis:EnvironmentPrefix"] = "preview-endpoint-test",
                    ["Redis:Features:PreviewSessions"] = previewRedisUnavailable.ToString()
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ClubHubDbContext>();
                services.RemoveAll<DbContextOptions<ClubHubDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ClubHubDbContext>>();
                services.RemoveAll<IDatabaseProvider>();
                services.AddDbContext<ClubHubDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName, databaseRoot));
                services.RemoveAll<IDistributedRateLimiter>();
                services.AddSingleton(limiter);
                services.RemoveAll<ILearningObjectStorage>();
                services.AddSingleton(storage);
                if (previewRedisUnavailable)
                {
                    services.RemoveAll<IRedisDatabase>();
                    services.AddSingleton<IRedisDatabase, UnavailableRedisDatabase>();
                }
            });
        });
    }

    private static async Task<(HttpClient Client, int ItemId)> SeedAndAuthenticateAsync(
        WebApplicationFactory<Program> factory)
    {
        const int userId = 156101;
        const int clubId = 156102;
        const int roleId = 156103;
        const int itemId = 156104;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            var now = DateTime.UtcNow;
            db.AddRange(
                new User
                {
                    UserId = userId,
                    Username = "preview-endpoint-test",
                    PasswordHash = "not-used",
                    RealName = "预览端点测试",
                    AccountStatus = "normal",
                    CreatedAt = now
                },
                new Club
                {
                    ClubId = clubId,
                    ClubName = "预览端点测试社团",
                    ClubStatus = "normal",
                    CreatedAt = now
                },
                new ClubHub.Api.Data.Entities.Role
                {
                    RoleId = roleId,
                    RoleCode = "STUDENT",
                    RoleName = "普通学生",
                    RoleScope = "system",
                    CreatedAt = now
                },
                new UserRole
                {
                    UserRoleId = 156105,
                    UserId = userId,
                    RoleId = roleId,
                    AssignedAt = now
                },
                new LearningItem
                {
                    ItemId = itemId,
                    ClubId = clubId,
                    UploaderUserId = userId,
                    Title = "Redis 预览端点测试",
                    ItemType = "document",
                    FileUrl = PreviewObjectStorage.Reference,
                    Visibility = "public",
                    ItemStatus = "published",
                    CreatedAt = now
                });
            await db.SaveChangesAsync();
        }

        using var tokenScope = factory.Services.CreateScope();
        var token = tokenScope.ServiceProvider
            .GetRequiredService<AuthTokenService>()
            .CreateToken(new User { UserId = userId, Username = "preview-endpoint-test" });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return (client, itemId);
    }

    private sealed class TestRateLimiter(RateLimiterMode mode) : IDistributedRateLimiter
    {
        public bool Enabled => true;

        public Task<RateLimitDecision> AcquireAsync(
            string policy,
            string subject,
            int limit,
            TimeSpan window,
            CancellationToken cancellationToken = default) =>
            mode switch
            {
                RateLimiterMode.Allowed => Task.FromResult(new RateLimitDecision(true, limit - 1, 0)),
                RateLimiterMode.Rejected => Task.FromResult(new RateLimitDecision(false, 0, 37)),
                _ => Task.FromException<RateLimitDecision>(
                    new RateLimitUnavailableException(
                        "Expected test failure.",
                        new RedisException("Expected test failure.")))
            };

        public Task ResetAsync(
            string policy,
            string subject,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class PreviewObjectStorage : ILearningObjectStorage
    {
        public const string Reference = "clubs/156102/learning/156104/test.pdf";
        public int MetadataReads { get; private set; }

        public bool IsStorageReference(string? value) => value == Reference;

        public Task<StoredObjectMetadata> GetMetadataAsync(
            string storageReference,
            CancellationToken cancellationToken)
        {
            MetadataReads++;
            return Task.FromResult(new StoredObjectMetadata(
                8,
                "application/pdf",
                null,
                "etag",
                DateTimeOffset.UtcNow));
        }

        public Task<StoredObjectDownload> OpenReadAsync(
            string storageReference,
            StoredObjectRange? range,
            CancellationToken cancellationToken) =>
            Task.FromResult(new StoredObjectDownload(new MemoryStream("%PDF-1.7"u8.ToArray())));

        public Task<string> UploadAsync(
            int clubId,
            int itemId,
            string extension,
            Stream content,
            long contentLength,
            string? contentType,
            string originalFileName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task SaveAsync(
            string storageReference,
            Stream content,
            long contentLength,
            string contentType,
            string contentDisposition,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class UnavailableRedisDatabase : IRedisDatabase
    {
        private static Exception Failure() => new RedisException("Expected test failure.");

        public Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            Task.FromException<RedisValue>(Failure());

        public Task<bool> StringSetAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            Task.FromException<bool>(Failure());

        public Task<bool> StringSetIfNotExistsAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            Task.FromException<bool>(Failure());

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            Task.FromException<bool>(Failure());

        public Task<bool> KeyDeleteIfValueMatchesAsync(
            RedisKey key,
            RedisValue expectedValue,
            CancellationToken cancellationToken = default) =>
            Task.FromException<bool>(Failure());

        public Task<RedisResult> ScriptEvaluateAsync(
            string script,
            RedisKey[] keys,
            RedisValue[] values,
            CancellationToken cancellationToken = default) =>
            Task.FromException<RedisResult>(Failure());

        public Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<TimeSpan>(Failure());
    }
}
