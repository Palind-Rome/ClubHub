using Microsoft.Extensions.Options;

namespace ClubHub.Api.Infrastructure.Redis;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public bool Enabled { get; init; }

    public string? ConnectionString { get; init; }

    public string Username { get; init; } = "default";

    public string? Password { get; init; }

    public string EnvironmentPrefix { get; init; } = "development";

    public int Database { get; init; }

    public int DefaultTtlSeconds { get; init; } = 300;

    public int NullValueTtlSeconds { get; init; } = 30;

    public int MaxPayloadBytes { get; init; } = 256 * 1024;

    public double TtlJitterRatio { get; init; } = 0.1;

    public int ConnectTimeoutMilliseconds { get; init; } = 5_000;

    public int OperationTimeoutMilliseconds { get; init; } = 3_000;

    public int ConnectRetry { get; init; } = 3;

    public int ReconnectBaseDelayMilliseconds { get; init; } = 5_000;

    public RedisFeatureOptions Features { get; init; } = new();
}

public sealed class RedisFeatureOptions
{
    public bool Cache { get; init; }

    public bool AuthSessions { get; init; }

    public bool PermissionCache { get; init; }

    public bool PreviewSessions { get; init; }

    public bool RateLimiting { get; init; }

    public bool Idempotency { get; init; }

    public bool DistributedLocks { get; init; }

    public bool RealtimeReadModels { get; init; }
}

internal sealed class RedisOptionsValidator : IValidateOptions<RedisOptions>
{
    public ValidateOptionsResult Validate(string? name, RedisOptions options)
    {
        var failures = new List<string>();

        if (options.Enabled && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("Redis:ConnectionString is required when Redis is enabled.");
        }

        if (options.Enabled && string.IsNullOrWhiteSpace(options.Password))
        {
            failures.Add("Redis:Password is required when Redis is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            failures.Add("Redis:Username must not be empty.");
        }

        if (!RedisKeyBuilder.IsValidNamespaceSegment(options.EnvironmentPrefix))
        {
            failures.Add(
                "Redis:EnvironmentPrefix must contain only lowercase letters, digits, and hyphens.");
        }

        if (options.Database < -1)
        {
            failures.Add("Redis:Database must be -1 or greater.");
        }

        if (options.DefaultTtlSeconds <= 0)
        {
            failures.Add("Redis:DefaultTtlSeconds must be greater than zero.");
        }

        if (options.NullValueTtlSeconds <= 0)
        {
            failures.Add("Redis:NullValueTtlSeconds must be greater than zero.");
        }

        if (options.MaxPayloadBytes is <= 0 or > 256 * 1024)
        {
            failures.Add("Redis:MaxPayloadBytes must be between 1 and 262144.");
        }

        if (options.TtlJitterRatio is < 0 or > 0.5)
        {
            failures.Add("Redis:TtlJitterRatio must be between 0 and 0.5.");
        }

        if (options.ConnectTimeoutMilliseconds <= 0 ||
            options.OperationTimeoutMilliseconds <= 0 ||
            options.ReconnectBaseDelayMilliseconds <= 0)
        {
            failures.Add("Redis timeout values must be greater than zero.");
        }

        if (options.ConnectRetry < 0)
        {
            failures.Add("Redis:ConnectRetry must be zero or greater.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
