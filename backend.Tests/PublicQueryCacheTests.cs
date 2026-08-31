using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
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

public sealed class PublicQueryCacheTests
{
    [Fact]
    public async Task ActivityCacheSharesPublicSnapshotButKeepsUserStateLive()
    {
        var redis = new InMemoryRedisDatabase();
        await using var factory = CreateFactory(redis);
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        using var firstResponse = await client.GetAsync("/api/activities/10?currentUserId=101");
        var first = await ReadJsonAsync(firstResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("Redis workshop", first.GetProperty("title").GetString());
        Assert.Equal(1, first.GetProperty("currentParticipants").GetInt32());
        Assert.True(first.GetProperty("isRegistered").GetBoolean());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            var activity = await db.Activities.FindAsync(10);
            activity!.Title = "Updated workshop";
            db.ActivityParticipations.Add(new ActivityParticipation
            {
                ParticipationId = 2,
                ActivityId = 10,
                UserId = 202,
                RegisterStatus = "accepted"
            });
            await db.SaveChangesAsync();
        }

        using var secondResponse = await client.GetAsync("/api/activities/10?currentUserId=202");
        var second = await ReadJsonAsync(secondResponse);

        Assert.Equal("Redis workshop", second.GetProperty("title").GetString());
        Assert.Equal(2, second.GetProperty("currentParticipants").GetInt32());
        Assert.True(second.GetProperty("isRegistered").GetBoolean());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<PublicQueryCacheService>();
            await cache.InvalidateActivityAsync(10);
        }

        using var refreshedResponse = await client.GetAsync("/api/activities/10?currentUserId=303");
        var refreshed = await ReadJsonAsync(refreshedResponse);

        Assert.Equal("Updated workshop", refreshed.GetProperty("title").GetString());
        Assert.False(refreshed.GetProperty("isRegistered").GetBoolean());
    }

    [Fact]
    public async Task VenueCacheInvalidatesAndCachesMissingIds()
    {
        var redis = new InMemoryRedisDatabase();
        await using var factory = CreateFactory(redis);
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        using var firstResponse = await client.GetAsync("/api/venues/20");
        var first = await ReadJsonAsync(firstResponse);
        Assert.Equal("Lecture hall", first.GetProperty("name").GetString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            var venue = await db.Venues.FindAsync(20);
            venue!.VenueName = "Updated hall";
            await db.SaveChangesAsync();
        }

        using var cachedResponse = await client.GetAsync("/api/venues/20");
        var cached = await ReadJsonAsync(cachedResponse);
        Assert.Equal("Lecture hall", cached.GetProperty("name").GetString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<PublicQueryCacheService>();
            await cache.InvalidateVenueAsync(20);
        }

        using var refreshedResponse = await client.GetAsync("/api/venues/20");
        var refreshed = await ReadJsonAsync(refreshedResponse);
        Assert.Equal("Updated hall", refreshed.GetProperty("name").GetString());

        var writesBeforeMissing = redis.DataWriteCount;
        using var firstMissing = await client.GetAsync("/api/venues/999");
        using var secondMissing = await client.GetAsync("/api/venues/999");
        Assert.Equal(HttpStatusCode.NotFound, firstMissing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, secondMissing.StatusCode);
        Assert.Equal(writesBeforeMissing + 1, redis.DataWriteCount);
    }

    [Fact]
    public async Task VenueCacheRebuildsPersistedMaintenanceDeadline()
    {
        var redis = new InMemoryRedisDatabase();
        await using var factory = CreateFactory(redis);
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        var firstDeadline = new DateTime(2026, 9, 15, 4, 0, 0, DateTimeKind.Utc);
        var secondDeadline = firstDeadline.AddDays(2);

        using var firstResponse = await client.GetAsync("/api/venues/21");
        var first = await ReadJsonAsync(firstResponse);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("maintenance", first.GetProperty("status").GetString());
        Assert.Equal(firstDeadline, first.GetProperty("maintenanceUntil").GetDateTime());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            var venue = await db.Venues.FindAsync(21);
            venue!.MaintenanceUntil = secondDeadline;
            await db.SaveChangesAsync();
        }

        using var cachedResponse = await client.GetAsync("/api/venues/21");
        var cached = await ReadJsonAsync(cachedResponse);
        Assert.Equal(firstDeadline, cached.GetProperty("maintenanceUntil").GetDateTime());

        redis.Unavailable = true;
        using var fallbackResponse = await client.GetAsync("/api/venues/21");
        var fallback = await ReadJsonAsync(fallbackResponse);
        Assert.Equal(secondDeadline, fallback.GetProperty("maintenanceUntil").GetDateTime());
        redis.Unavailable = false;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<PublicQueryCacheService>();
            await cache.InvalidateVenueAsync(21);
        }

        using var refreshedResponse = await client.GetAsync("/api/venues/21");
        var refreshed = await ReadJsonAsync(refreshedResponse);
        Assert.Equal(secondDeadline, refreshed.GetProperty("maintenanceUntil").GetDateTime());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            var venue = await db.Venues.FindAsync(21);
            venue!.VenueStatus = "available";
            venue.MaintenanceUntil = null;
            await db.SaveChangesAsync();
            var cache = scope.ServiceProvider.GetRequiredService<PublicQueryCacheService>();
            await cache.InvalidateVenueAsync(21);
        }

        using var clearedResponse = await client.GetAsync("/api/venues/21");
        var cleared = await ReadJsonAsync(clearedResponse);
        Assert.Equal("available", cleared.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("maintenanceUntil").ValueKind);
    }

    [Fact]
    public async Task RedisOutageFallsBackAndRecoveryNaturallyRebuilds()
    {
        var redis = new InMemoryRedisDatabase { Unavailable = true };
        await using var factory = CreateFactory(redis);
        await SeedAsync(factory.Services);
        using var client = factory.CreateClient();

        using var degradedResponse = await client.GetAsync("/api/venues/20");
        var degraded = await ReadJsonAsync(degradedResponse);
        Assert.Equal(HttpStatusCode.OK, degradedResponse.StatusCode);
        Assert.Equal("Lecture hall", degraded.GetProperty("name").GetString());
        Assert.Equal(0, redis.DataWriteCount);

        redis.Unavailable = false;
        using var recoveredResponse = await client.GetAsync("/api/venues/20");
        Assert.Equal(HttpStatusCode.OK, recoveredResponse.StatusCode);
        Assert.Equal(1, redis.DataWriteCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(InMemoryRedisDatabase redis) =>
        new PublicQueryCacheFactory(redis);

    private sealed class PublicQueryCacheFactory(InMemoryRedisDatabase redis)
        : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"PublicQueryCacheTests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Authentication:TokenSigningKey"] = "ClubHub.Tests.TokenSigningKey",
                        ["ConnectionStrings:Default"] = "Tests must not use the Oracle connection",
                        ["Redis:Enabled"] = "true",
                        ["Redis:ConnectionString"] = "test-redis:6379",
                        ["Redis:Password"] = "test-only-password",
                        ["Redis:EnvironmentPrefix"] = "test",
                        ["Redis:Features:Cache"] = "true"
                    }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ClubHubDbContext>();
                services.RemoveAll<DbContextOptions<ClubHubDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ClubHubDbContext>>();
                services.RemoveAll<IDatabaseProvider>();
                services.AddDbContext<ClubHubDbContext>(
                    options => options.UseInMemoryDatabase(_databaseName));
                services.RemoveAll<IRedisDatabase>();
                services.AddSingleton<IRedisDatabase>(redis);
            });
        }
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        db.Clubs.Add(new Club
        {
            ClubId = 1,
            ClubName = "Database Club",
            CreatedAt = DateTime.UtcNow
        });
        db.Activities.Add(new Activity
        {
            ActivityId = 10,
            ClubId = 1,
            Title = "Redis workshop",
            ActivityStatus = "published",
            Capacity = 50,
            CreatedAt = DateTime.UtcNow
        });
        db.ActivityParticipations.Add(new ActivityParticipation
        {
            ParticipationId = 1,
            ActivityId = 10,
            UserId = 101,
            RegisterStatus = "accepted"
        });
        db.Venues.Add(new Venue
        {
            VenueId = 20,
            VenueName = "Lecture hall",
            Capacity = 100,
            VenueStatus = "available",
            CreatedAt = DateTime.UtcNow
        });
        db.Venues.Add(new Venue
        {
            VenueId = 21,
            VenueName = "Maintenance hall",
            Capacity = 80,
            VenueStatus = "maintenance",
            MaintenanceUntil = new DateTime(2026, 9, 15, 4, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class InMemoryRedisDatabase : IRedisDatabase
    {
        private readonly ConcurrentDictionary<string, RedisValue> _values = [];
        private int _dataWriteCount;

        public bool Unavailable { get; set; }

        public int DataWriteCount => Volatile.Read(ref _dataWriteCount);

        public Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            return Task.FromResult(
                _values.TryGetValue(key.ToString(), out var value)
                    ? value
                    : RedisValue.Null);
        }

        public Task<bool> StringSetAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            _values[key.ToString()] = value;
            Interlocked.Increment(ref _dataWriteCount);
            return Task.FromResult(true);
        }

        public Task<bool> StringSetIfNotExistsAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            return Task.FromResult(_values.TryAdd(key.ToString(), value));
        }

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            return Task.FromResult(_values.TryRemove(key.ToString(), out _));
        }

        public Task<bool> KeyDeleteIfValueMatchesAsync(
            RedisKey key,
            RedisValue expectedValue,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            var stringKey = key.ToString();
            if (!_values.TryGetValue(stringKey, out var value) || value != expectedValue)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(_values.TryRemove(stringKey, out _));
        }

        public Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            return Task.FromResult(TimeSpan.FromMilliseconds(1));
        }

        private void ThrowIfUnavailable()
        {
            if (Unavailable)
            {
                throw new TimeoutException("simulated Redis outage");
            }
        }
    }
}
