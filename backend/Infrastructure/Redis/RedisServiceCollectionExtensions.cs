using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Infrastructure.Redis;

public static class RedisServiceCollectionExtensions
{
    public static IServiceCollection AddClubHubRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RedisOptions>, RedisOptionsValidator>());

        services.AddMetrics();
        services.TryAddSingleton<IRedisKeyBuilder, RedisKeyBuilder>();
        services.TryAddSingleton<IRedisCacheSerializer, RedisCacheSerializer>();
        services.TryAddSingleton<IRedisTtlPolicy, RedisTtlPolicy>();
        services.TryAddSingleton<IRedisDatabase, StackExchangeRedisDatabase>();
        services.TryAddSingleton<IRedisCacheService, RedisCacheService>();
        services.TryAddSingleton<RedisMetrics>();
        services.TryAddSingleton<IConnectionMultiplexer>(CreateConnection);

        services
            .AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy("ClubHub API is running."),
                tags: ["live", "ready"])
            .AddCheck<RedisHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);

        return services;
    }

    internal static ConfigurationOptions BuildConnectionOptions(
        RedisOptions options,
        ILoggerFactory? loggerFactory = null)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new OptionsValidationException(
                RedisOptions.SectionName,
                typeof(RedisOptions),
                ["Redis:ConnectionString is required before a Redis connection can be created."]);
        }

        var connectionOptions = ConfigurationOptions.Parse(options.ConnectionString);
        connectionOptions.User = options.Username;
        connectionOptions.Password = options.Password;
        connectionOptions.AbortOnConnectFail = false;
        connectionOptions.AllowAdmin = false;
        connectionOptions.ConnectRetry = options.ConnectRetry;
        connectionOptions.ConnectTimeout = options.ConnectTimeoutMilliseconds;
        connectionOptions.SyncTimeout = options.OperationTimeoutMilliseconds;
        connectionOptions.AsyncTimeout = options.OperationTimeoutMilliseconds;
        if (options.Database >= 0)
        {
            connectionOptions.DefaultDatabase = options.Database;
        }
        connectionOptions.ClientName = "clubhub-api";
        connectionOptions.BacklogPolicy = BacklogPolicy.FailFast;
        connectionOptions.ReconnectRetryPolicy =
            new ExponentialRetry(options.ReconnectBaseDelayMilliseconds);
        connectionOptions.LoggerFactory = loggerFactory;
        return connectionOptions;
    }

    private static IConnectionMultiplexer CreateConnection(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<RedisOptions>>().Value;
        var loggerFactory = services.GetService<ILoggerFactory>();
        return ConnectionMultiplexer.Connect(BuildConnectionOptions(options, loggerFactory));
    }
}
