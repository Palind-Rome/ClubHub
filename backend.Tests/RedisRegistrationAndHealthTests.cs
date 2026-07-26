using System.Net;
using System.Text.Json;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Tests;

public sealed class RedisRegistrationTests
{
    [Fact]
    public void AddClubHubRedisRegistersOneSharedConnectionAndCacheService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Redis:Enabled"] = "false",
                    ["Redis:EnvironmentPrefix"] = "test"
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddClubHubRedis(configuration);

        var connectionRegistration = Assert.Single(
            services,
            service => service.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Equal(ServiceLifetime.Singleton, connectionRegistration.Lifetime);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IRedisCacheService>();
        var second = provider.GetRequiredService<IRedisCacheService>();
        Assert.Same(first, second);
    }

    [Fact]
    public void BuildConnectionOptionsAppliesFailFastTimeoutAndReconnectPolicy()
    {
        var options = new RedisOptions
        {
            Enabled = true,
            ConnectionString = "localhost:6379",
            Username = "clubhub",
            Password = "test-only-password",
            EnvironmentPrefix = "test",
            Database = 2,
            ConnectTimeoutMilliseconds = 1_500,
            OperationTimeoutMilliseconds = 750,
            ConnectRetry = 4,
            ReconnectBaseDelayMilliseconds = 2_000
        };

        var connection = RedisServiceCollectionExtensions.BuildConnectionOptions(options);

        Assert.False(connection.AbortOnConnectFail);
        Assert.False(connection.AllowAdmin);
        Assert.Equal(1_500, connection.ConnectTimeout);
        Assert.Equal(750, connection.SyncTimeout);
        Assert.Equal(750, connection.AsyncTimeout);
        Assert.Equal(4, connection.ConnectRetry);
        Assert.Equal(2, connection.DefaultDatabase);
        Assert.Equal("clubhub", connection.User);
        Assert.Equal("test-only-password", connection.Password);
        Assert.Same(BacklogPolicy.FailFast, connection.BacklogPolicy);
        Assert.IsType<ExponentialRetry>(connection.ReconnectRetryPolicy);
    }

    [Fact]
    public void OptionsValidatorRequiresConnectionAndPasswordOnlyWhenRedisIsEnabled()
    {
        var validator = new RedisOptionsValidator();

        var disabled = validator.Validate(
            null,
            new RedisOptions { Enabled = false, EnvironmentPrefix = "test" });
        var enabled = validator.Validate(
            null,
            new RedisOptions { Enabled = true, EnvironmentPrefix = "test" });

        Assert.True(disabled.Succeeded);
        Assert.True(enabled.Failed);
        Assert.Contains(
            enabled.Failures,
            failure => failure.Contains("ConnectionString", StringComparison.Ordinal));
        Assert.Contains(
            enabled.Failures,
            failure => failure.Contains("Password", StringComparison.Ordinal));
    }
}

public sealed class RedisHealthCheckTests
{
    [Fact]
    public async Task DisabledRedisIsHealthyWithoutOpeningConnection()
    {
        using var metrics = new TestMetrics();
        var database = new HealthCheckRedisDatabase
        {
            ExceptionToThrow = new TimeoutException("must not be called")
        };
        var check = CreateHealthCheck(database, enabled: false, metrics.Value);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(0, database.PingCalls);
    }

    [Fact]
    public async Task EnabledButUnavailableRedisMakesReadinessUnhealthy()
    {
        using var metrics = new TestMetrics();
        var database = new HealthCheckRedisDatabase
        {
            ExceptionToThrow = new TimeoutException("simulated")
        };
        var check = CreateHealthCheck(database, enabled: true, metrics.Value);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1, database.PingCalls);
    }

    [Fact]
    public async Task HealthCheckHonorsCancellation()
    {
        using var metrics = new TestMetrics();
        var database = new HealthCheckRedisDatabase { BlockPing = true };
        var check = CreateHealthCheck(database, enabled: true, metrics.Value);
        var context = CreateContext(check);
        using var cancellation = new CancellationTokenSource();

        var healthCheck = check.CheckHealthAsync(context, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => healthCheck);
    }

    private static RedisHealthCheck CreateHealthCheck(
        IRedisDatabase database,
        bool enabled,
        RedisMetrics metrics) =>
        new(
            database,
            Options.Create(new RedisOptions { Enabled = enabled }),
            metrics,
            NullLogger<RedisHealthCheck>.Instance);

    private static HealthCheckContext CreateContext(RedisHealthCheck check) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                "redis",
                check,
                HealthStatus.Unhealthy,
                ["ready"])
        };

    private sealed class TestMetrics : IDisposable
    {
        private readonly ServiceProvider _provider;

        public TestMetrics()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            _provider = services.BuildServiceProvider();
            Value = new RedisMetrics(
                _provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        }

        public RedisMetrics Value { get; }

        public void Dispose() => _provider.Dispose();
    }

    private sealed class HealthCheckRedisDatabase : IRedisDatabase
    {
        public Exception? ExceptionToThrow { get; init; }

        public int PingCalls { get; private set; }

        public bool BlockPing { get; init; }

        public async Task<TimeSpan> PingAsync(
            CancellationToken cancellationToken = default)
        {
            PingCalls++;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (BlockPing)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return TimeSpan.FromMilliseconds(1);
        }

        public Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> StringSetAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> StringSetIfNotExistsAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> KeyDeleteIfValueMatchesAsync(
            RedisKey key,
            RedisValue expectedValue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

public sealed class RedisHealthEndpointTests
{
    [Fact]
    public async Task LiveAndReadyEndpointsSeparateProcessAndDependencyChecks()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        using var client = factory.CreateClient();

        using var liveResponse = await client.GetAsync("/health/live");
        using var readyResponse = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);

        using var liveDocument = JsonDocument.Parse(await liveResponse.Content.ReadAsStringAsync());
        using var readyDocument = JsonDocument.Parse(await readyResponse.Content.ReadAsStringAsync());
        var liveChecks = liveDocument.RootElement.GetProperty("checks");
        var readyChecks = readyDocument.RootElement.GetProperty("checks");

        Assert.True(liveChecks.TryGetProperty("self", out _));
        Assert.False(liveChecks.TryGetProperty("redis", out _));
        Assert.True(readyChecks.TryGetProperty("self", out _));
        Assert.Equal(
            "healthy",
            readyChecks.GetProperty("redis").GetProperty("status").GetString());
    }

    [Fact]
    public async Task RedisOutageFailsReadinessButNotLiveness()
    {
        await using var baseFactory = new ClubHubWebApplicationFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Redis:Enabled"] = "true",
                        ["Redis:ConnectionString"] = "localhost:6379",
                        ["Redis:Password"] = "test-only-password",
                        ["Redis:EnvironmentPrefix"] = "test"
                    }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRedisDatabase>();
                services.AddSingleton<IRedisDatabase, UnavailableRedisDatabase>();
            });
        });
        using var client = factory.CreateClient();

        using var liveResponse = await client.GetAsync("/health/live");
        using var readyResponse = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
    }

    private sealed class UnavailableRedisDatabase : IRedisDatabase
    {
        public Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default) =>
            throw new TimeoutException("simulated");

        public Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> StringSetAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> StringSetIfNotExistsAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> KeyDeleteIfValueMatchesAsync(
            RedisKey key,
            RedisValue expectedValue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
