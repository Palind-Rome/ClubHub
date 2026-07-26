using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Infrastructure.Redis;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IRedisDatabase _database;
    private readonly RedisOptions _options;
    private readonly RedisMetrics _metrics;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(
        IRedisDatabase database,
        IOptions<RedisOptions> options,
        RedisMetrics metrics,
        ILogger<RedisHealthCheck> logger)
    {
        _database = database;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return HealthCheckResult.Healthy(
                "Redis is disabled.",
                new Dictionary<string, object> { ["enabled"] = false });
        }

        try
        {
            var latency = await _database.PingAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return HealthCheckResult.Healthy(
                "Redis is available.",
                new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["latencyMs"] = latency.TotalMilliseconds
                });
        }
        catch (Exception exception) when (
            exception is RedisException or TimeoutException or ObjectDisposedException)
        {
            _metrics.RecordFailure("health-check");
            _logger.LogWarning(exception, "Redis health check failed.");
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "Redis is unavailable.",
                exception,
                new Dictionary<string, object> { ["enabled"] = true });
        }
    }
}
