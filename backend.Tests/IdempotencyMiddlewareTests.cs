using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Idempotency;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Tests;

public sealed class IdempotencyMiddlewareTests
{
    [Fact]
    public async Task ExistingSuccessfulRecordIsReplayedWithoutExecutingBusinessLogic()
    {
        await using var db = CreateDbContext();
        var context = CreateContext("replay-key");
        db.IdempotencyRecords.Add(CreateRecord(
            "replay-key",
            RequestHash(context),
            """{"id":156}"""));
        await db.SaveChangesAsync();
        var executions = 0;
        var middleware = CreateMiddleware(_ =>
        {
            executions++;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            db,
            new TestRedisDatabase(),
            CreateKeys(),
            NullLogger<IdempotencyMiddleware>.Instance);

        Assert.Equal(0, executions);
        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal("true", context.Response.Headers["Idempotency-Replayed"]);
        Assert.Equal("""{"id":156}""", await ResponseBodyAsync(context));
    }

    [Fact]
    public async Task ExistingKeyWithDifferentRequestReturns409()
    {
        await using var db = CreateDbContext();
        var context = CreateContext("conflict-key", """{"value":2}""");
        db.IdempotencyRecords.Add(CreateRecord(
            "conflict-key",
            new string('a', 64),
            """{"id":156}"""));
        await db.SaveChangesAsync();
        var executions = 0;

        await CreateMiddleware(_ =>
            {
                executions++;
                return Task.CompletedTask;
            })
            .InvokeAsync(
                context,
                db,
                new TestRedisDatabase(),
                CreateKeys(),
                NullLogger<IdempotencyMiddleware>.Instance);

        Assert.Equal(0, executions);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentProcessingRequestReturns409WithRetryAfter()
    {
        await using var db = CreateDbContext();
        var context = CreateContext("processing-key");
        var redis = new TestRedisDatabase
        {
            AcquireResult = false,
            CurrentValue =
                $$"""{"Status":"processing","RequestHash":"{{RequestHash(context)}}","Owner":"other","HttpStatus":null,"Body":null,"Headers":null}"""
        };
        var executions = 0;

        await CreateMiddleware(_ =>
            {
                executions++;
                return Task.CompletedTask;
            })
            .InvokeAsync(
                context,
                db,
                redis,
                CreateKeys(),
                NullLogger<IdempotencyMiddleware>.Instance);

        Assert.Equal(0, executions);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("2", context.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task RedisAcquisitionFailureReturns503WithoutExecutingBusinessLogic()
    {
        await using var db = CreateDbContext();
        var context = CreateContext("unavailable-key");
        var executions = 0;

        await CreateMiddleware(_ =>
            {
                executions++;
                return Task.CompletedTask;
            })
            .InvokeAsync(
                context,
                db,
                new TestRedisDatabase { FailAcquisition = true },
                CreateKeys(),
                NullLogger<IdempotencyMiddleware>.Instance);

        Assert.Equal(0, executions);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    private static IdempotencyMiddleware CreateMiddleware(RequestDelegate next) =>
        new(
            next,
            Options.Create(new RedisOptions
            {
                Enabled = true,
                EnvironmentPrefix = "idempotency-test",
                Features = new RedisFeatureOptions { Idempotency = true }
            }));

    private static RedisKeyBuilder CreateKeys() =>
        new(Options.Create(new RedisOptions { EnvironmentPrefix = "idempotency-test" }));

    private static ClubHubDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ClubHubDbContext>()
            .UseInMemoryDatabase($"IdempotencyMiddleware-{Guid.NewGuid():N}")
            .Options);

    private static DefaultHttpContext CreateContext(string key, string body = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/test";
        context.Request.Headers["Idempotency-Key"] = key;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "156")],
            "test"));
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IdempotentOperationAttribute("testOperation")),
            "test"));
        return context;
    }

    private static IdempotencyRecord CreateRecord(
        string requestKey,
        string requestHash,
        string responseBody) =>
        new()
        {
            IdempotencyId = Random.Shared.Next(1, int.MaxValue),
            UserId = 156,
            OperationScope = "testOperation",
            RequestKeyHash = CreateKeys().HashSensitive(requestKey),
            RequestHash = requestHash,
            RecordStatus = "succeeded",
            HttpStatus = StatusCodes.Status201Created,
            ContentType = "application/json",
            ResponseHeaders = """{"Content-Type":"application/json"}""",
            ResponseBody = responseBody,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static string RequestHash(HttpContext context)
    {
        var body = ((MemoryStream)context.Request.Body).ToArray();
        var bodyDigest = Convert.ToHexStringLower(SHA256.HashData(body));
        var canonical = string.Join(
            '\n',
            context.Request.Method,
            "testOperation",
            context.Request.Path.Value,
            context.Request.QueryString.Value,
            bodyDigest);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static async Task<string> ResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    private sealed class TestRedisDatabase : IRedisDatabase
    {
        public bool AcquireResult { get; init; } = true;
        public bool FailAcquisition { get; init; }
        public RedisValue CurrentValue { get; init; } = RedisValue.Null;

        public Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentValue);

        public Task<bool> StringSetAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> StringSetIfNotExistsAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            FailAcquisition
                ? Task.FromException<bool>(new RedisException("Expected test failure."))
                : Task.FromResult(AcquireResult);

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> KeyDeleteIfValueMatchesAsync(
            RedisKey key,
            RedisValue expectedValue,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<RedisResult> ScriptEvaluateAsync(
            string script,
            RedisKey[] keys,
            RedisValue[] values,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RedisResult.Create(1L));

        public Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(TimeSpan.Zero);
    }
}
