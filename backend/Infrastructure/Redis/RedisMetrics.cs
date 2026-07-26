using System.Diagnostics.Metrics;

namespace ClubHub.Api.Infrastructure.Redis;

public sealed class RedisMetrics
{
    public const string MeterName = "ClubHub.Api.Redis";

    private readonly Counter<long> _cacheReads;
    private readonly Counter<long> _cacheWrites;
    private readonly Counter<long> _operationFailures;
    private readonly Histogram<double> _operationDuration;
    private readonly Counter<long> _sourceLoads;
    private readonly Histogram<double> _sourceDuration;
    private readonly Counter<long> _rebuildLeases;

    public RedisMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _cacheReads = meter.CreateCounter<long>(
            "clubhub.redis.cache.reads",
            description: "Redis cache read attempts.");
        _cacheWrites = meter.CreateCounter<long>(
            "clubhub.redis.cache.writes",
            description: "Redis cache write attempts.");
        _operationFailures = meter.CreateCounter<long>(
            "clubhub.redis.operation.failures",
            description: "Redis operations that failed or timed out.");
        _operationDuration = meter.CreateHistogram<double>(
            "clubhub.redis.operation.duration",
            unit: "ms",
            description: "Redis operation duration in milliseconds.");
        _sourceLoads = meter.CreateCounter<long>(
            "clubhub.redis.cache.source.loads",
            description: "Source-of-truth loads performed for cache entries.");
        _sourceDuration = meter.CreateHistogram<double>(
            "clubhub.redis.cache.source.duration",
            unit: "ms",
            description: "Source-of-truth cache load duration in milliseconds.");
        _rebuildLeases = meter.CreateCounter<long>(
            "clubhub.redis.cache.rebuild.leases",
            description: "Cache rebuild lease outcomes.");
    }

    public void RecordCacheRead(string outcome, double elapsedMilliseconds)
    {
        _cacheReads.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _operationDuration.Record(
            elapsedMilliseconds,
            new KeyValuePair<string, object?>("operation", "cache-read"));
    }

    public void RecordCacheWrite(string operation, string outcome, double elapsedMilliseconds)
    {
        _cacheWrites.Add(
            1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("outcome", outcome));
        _operationDuration.Record(
            elapsedMilliseconds,
            new KeyValuePair<string, object?>("operation", operation));
    }

    public void RecordFailure(string operation) =>
        _operationFailures.Add(
            1,
            new KeyValuePair<string, object?>("operation", operation));

    public void RecordSourceLoad(
        string cacheName,
        string outcome,
        double elapsedMilliseconds)
    {
        _sourceLoads.Add(
            1,
            new KeyValuePair<string, object?>("cache", cacheName),
            new KeyValuePair<string, object?>("outcome", outcome));
        _sourceDuration.Record(
            elapsedMilliseconds,
            new KeyValuePair<string, object?>("cache", cacheName));
    }

    public void RecordRebuildLease(string cacheName, string outcome) =>
        _rebuildLeases.Add(
            1,
            new KeyValuePair<string, object?>("cache", cacheName),
            new KeyValuePair<string, object?>("outcome", outcome));
}
