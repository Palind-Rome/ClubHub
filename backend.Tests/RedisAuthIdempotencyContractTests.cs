using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Idempotency;
using ClubHub.Api.Infrastructure.Redis;
using ClubHub.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Tests;

public sealed class RedisAuthIdempotencyContractTests
{
    private static readonly string[] ProtectedOperations =
    [
        "createClubApplication",
        "resubmitClubApplication",
        "reviewClubApplication",
        "createClubAwardApplication",
        "submitClubAwardApplication",
        "reviewClubAwardApplication",
        "createRecruitmentApplication",
        "reviewRecruitment",
        "reviewRecruitmentApplication",
        "registerActivity",
        "reviewActivity",
        "createBudgetApplication",
        "reviewBudgetApplication",
        "createVenueReservation",
        "reviewVenueReservation",
        "enrollLearningItem",
        "reviewLearningItem",
        "createProject",
        "reviewProject",
        "submitProjectTaskDeliverable",
        "reviewProjectTaskDeliverable",
        "borrowMaterial",
        "returnMaterialBorrow",
        "damageMaterialBorrow"
    ];

    [Fact]
    public void OpenApiAndControllerMetadataCoverEveryProtectedOperation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contract = File.ReadAllText(Path.Combine(repositoryRoot, "api", "openapi.yaml"));
        var controllerOperations = typeof(Program).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Select(method => method.GetCustomAttribute<IdempotentOperationAttribute>()?.OperationId)
            .Where(operation => operation is not null)
            .Cast<string>()
            .OrderBy(operation => operation)
            .ToArray();

        Assert.Equal(ProtectedOperations.OrderBy(operation => operation), controllerOperations);
        foreach (var operation in ProtectedOperations)
        {
            Assert.Contains(
                $"operationId: {operation}\n      x-idempotency-required: true",
                contract.ReplaceLineEndings("\n"),
                StringComparison.Ordinal);
            Assert.Matches(
                $@"operationId: {Regex.Escape(operation)}\n" +
                @"      x-idempotency-required: true\n" +
                @"      security:\n" +
                @"        - bearerAuth: \[\]\n" +
                @"      parameters:\n" +
                @"        - \$ref: ""#/components/parameters/IdempotencyKey""",
                contract.ReplaceLineEndings("\n"));
        }
        Assert.Contains("name: Idempotency-Key", contract, StringComparison.Ordinal);
        Assert.Contains("minLength: 8", contract, StringComparison.Ordinal);
        Assert.Contains("maxLength: 128", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationTokensHaveUniqueSessionIdsAndAbsoluteExpiry()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:TokenSigningKey"] = "ClubHub.Tests.RedisSessionSigningKey",
                ["Authentication:Sessions:AbsoluteLifetimeHours"] = "12"
            })
            .Build();
        var service = new AuthTokenService(configuration, new TestEnvironment());
        var user = new User { UserId = 42, Username = "session-test" };

        var first = service.CreateToken(user);
        var second = service.CreateToken(user);

        Assert.NotEqual(first, second);
        Assert.True(service.TryValidateToken(first, out var firstPrincipal));
        Assert.True(service.TryValidateToken(second, out var secondPrincipal));
        Assert.NotEqual(firstPrincipal.SessionId, secondPrincipal.SessionId);
        Assert.Equal(42, firstPrincipal.UserId);
        Assert.InRange(
            firstPrincipal.ExpiresAt - firstPrincipal.IssuedAt,
            TimeSpan.FromHours(11.99),
            TimeSpan.FromHours(12.01));
    }

    [Fact]
    public void LegacySignedTokensRemainReadableUntilRedisSessionsAreEnabled()
    {
        const string signingKey = "ClubHub.Tests.RedisSessionSigningKey";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:TokenSigningKey"] = signingKey
            })
            .Build();
        var service = new AuthTokenService(configuration, new TestEnvironment());
        var payload = JsonSerializer.Serialize(new
        {
            UserId = 42,
            Username = "legacy-user",
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });
        var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadPart)));

        Assert.False(service.TryValidateToken($"{payloadPart}.{signature}", out _));
        Assert.True(
            service.TryValidateLegacyToken($"{payloadPart}.{signature}", out var principal));
        Assert.Equal("legacy", principal.SessionId);
        Assert.Equal(42, principal.UserId);
    }

    [Fact]
    public async Task FixedWindowLuaIsAtomicAcrossConcurrentClientsWhenCiRedisIsAvailable()
    {
        var connectionString = RedisTestConnection();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var db = connection.GetDatabase();
        var key = $"clubhub:test:rate-limit:v1:{Guid.NewGuid():N}";
        const string script = """
            local current = redis.call('incr', KEYS[1])
            if current == 1 then redis.call('expire', KEYS[1], ARGV[1]) end
            return current
            """;

        var counts = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => db.ScriptEvaluateAsync(script, [key], [60])));

        Assert.Equal(Enumerable.Range(1, 20), counts.Select(result => (int)(long)result).Order());
        Assert.InRange(await db.KeyTimeToLiveAsync(key) ?? TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        await db.KeyDeleteAsync(key);
    }

    [Fact]
    public async Task RedisSessionsAreSharedAndEvictTheOldestSessionWhenCiRedisIsAvailable()
    {
        var connectionString = RedisTestConnection();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var redisOptions = Options.Create(new RedisOptions
        {
            Enabled = true,
            EnvironmentPrefix = "ci-session",
            Features = new RedisFeatureOptions { AuthSessions = true }
        });
        var sessionOptions = Options.Create(new AuthSessionOptions
        {
            MaxSessionsPerUser = 10,
            SlidingLifetimeMinutes = 30,
            AbsoluteLifetimeHours = 12
        });
        var database = new DirectRedisDatabase(connection.GetDatabase());
        var keys = new RedisKeyBuilder(redisOptions);
        var firstInstance = new AuthSessionService(
            database,
            keys,
            redisOptions,
            sessionOptions,
            NullLogger<AuthSessionService>.Instance);
        var secondInstance = new AuthSessionService(
            database,
            keys,
            redisOptions,
            sessionOptions,
            NullLogger<AuthSessionService>.Instance);
        var tokens = new List<(string Token, AuthTokenPrincipal Principal)>();
        var issuedAt = DateTimeOffset.UtcNow;
        for (var index = 0; index < 11; index++)
        {
            var principal = new AuthTokenPrincipal(
                156,
                "redis-session",
                Guid.NewGuid().ToString("N"),
                issuedAt.AddMilliseconds(index),
                issuedAt.AddHours(12));
            var token = $"session-{Guid.NewGuid():N}";
            tokens.Add((token, principal));
            await firstInstance.CreateAsync(token, principal);
        }

        Assert.Equal(
            AuthSessionValidation.Missing,
            await secondInstance.ValidateAndRefreshAsync(tokens[0].Token, tokens[0].Principal));
        Assert.Equal(
            AuthSessionValidation.Valid,
            await secondInstance.ValidateAndRefreshAsync(tokens[^1].Token, tokens[^1].Principal));

        await secondInstance.RevokeAllAsync(156);
        Assert.Equal(
            AuthSessionValidation.Missing,
            await firstInstance.ValidateAndRefreshAsync(tokens[^1].Token, tokens[^1].Principal));
    }

    [Fact]
    public async Task FixedWindowLimiterAllowsExactlyTheConfiguredConcurrencyWhenCiRedisIsAvailable()
    {
        var connectionString = RedisTestConnection();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var redisOptions = Options.Create(new RedisOptions
        {
            Enabled = true,
            EnvironmentPrefix = "ci-limit",
            Features = new RedisFeatureOptions { RateLimiting = true }
        });
        var limiter = new DistributedRateLimiter(
            new DirectRedisDatabase(connection.GetDatabase()),
            new RedisKeyBuilder(redisOptions),
            redisOptions);
        var subject = Guid.NewGuid().ToString("N");
        var decisions = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => limiter.AcquireAsync(
                    "concurrency",
                    subject,
                    5,
                    TimeSpan.FromMinutes(1))));

        Assert.Equal(5, decisions.Count(decision => decision.Allowed));
        Assert.All(
            decisions.Where(decision => !decision.Allowed),
            decision => Assert.InRange(decision.RetryAfterSeconds, 1, 60));
        Assert.True((await limiter.AcquireAsync(
            "concurrency",
            $"{subject}-isolated",
            5,
            TimeSpan.FromMinutes(1))).Allowed);
    }

    [Fact]
    public async Task StoringPreviewSessionDoesNotEvictAnotherUnexpiredSessionWhenCiRedisIsAvailable()
    {
        var connectionString = RedisTestConnection();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var environmentPrefix = $"ci-preview-{Guid.NewGuid():N}"[..20];
        var redisOptions = Options.Create(new RedisOptions
        {
            Enabled = true,
            EnvironmentPrefix = environmentPrefix,
            Features = new RedisFeatureOptions { PreviewSessions = true }
        });
        var database = new DirectRedisDatabase(connection.GetDatabase());
        var keys = new RedisKeyBuilder(redisOptions);
        using var firstInstance = new LearningPreviewSessionStore(
            database,
            keys,
            redisOptions,
            new TestEnvironment());
        using var secondInstance = new LearningPreviewSessionStore(
            database,
            keys,
            redisOptions,
            new TestEnvironment());
        var firstToken = $"preview-{Guid.NewGuid():N}";
        var secondToken = $"preview-{Guid.NewGuid():N}";
        var preview = new PreparedLearningPreview(
            LearningPreviewKind.Pdf,
            "application/pdf",
            128,
            "learning/preview/test.pdf",
            null,
            true);

        Assert.True(await firstInstance.StoreAsync(
            firstToken,
            156,
            1,
            preview,
            TimeSpan.FromMinutes(5)));
        Assert.True(await secondInstance.StoreAsync(
            secondToken,
            157,
            2,
            preview,
            TimeSpan.FromMinutes(5)));
        Assert.NotNull(await secondInstance.GetAsync(firstToken, 156, 1));

        await connection.GetDatabase().KeyDeleteAsync(
        [
            keys.Build("learning", "preview", keys.HashSensitive(firstToken)),
            keys.Build("learning", "preview", keys.HashSensitive(secondToken)),
            keys.Build("learning", "preview-index", "global")
        ]);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ClubHub.sln")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static string? RedisTestConnection()
    {
        var connection =
            Environment.GetEnvironmentVariable("CLUBHUB_REDIS_TEST_CONNECTION");
        if (string.Equals(
                Environment.GetEnvironmentVariable("CI"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(connection),
                "CI must provide CLUBHUB_REDIS_TEST_CONNECTION for Redis integration tests.");
        }
        return connection;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ClubHub.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DirectRedisDatabase(IDatabase database) : IRedisDatabase
    {
        public Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            database.StringGetAsync(key).WaitAsync(cancellationToken);

        public Task<bool> StringSetAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            database.StringSetAsync(key, value, expiration).WaitAsync(cancellationToken);

        public Task<bool> StringSetIfNotExistsAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            database.StringSetAsync(key, value, expiration, When.NotExists)
                .WaitAsync(cancellationToken);

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            database.KeyDeleteAsync(key).WaitAsync(cancellationToken);

        public async Task<bool> KeyDeleteIfValueMatchesAsync(
            RedisKey key,
            RedisValue expectedValue,
            CancellationToken cancellationToken = default)
        {
            const string script =
                "if redis.call('get', KEYS[1]) == ARGV[1] then " +
                "return redis.call('del', KEYS[1]) else return 0 end";
            var result = await database.ScriptEvaluateAsync(script, [key], [expectedValue])
                .WaitAsync(cancellationToken);
            return (long)result == 1;
        }

        public Task<RedisResult> ScriptEvaluateAsync(
            string script,
            RedisKey[] keys,
            RedisValue[] values,
            CancellationToken cancellationToken = default) =>
            database.ScriptEvaluateAsync(script, keys, values).WaitAsync(cancellationToken);

        public Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default) =>
            database.PingAsync().WaitAsync(cancellationToken);
    }
}
