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
    private readonly Counter<long> _lockAcquisitions;
    private readonly Histogram<double> _lockWaitDuration;
    private readonly Counter<long> _lockRenewals;
    private readonly Counter<long> _lockLeaseLosses;
    private readonly Counter<long> _lockReleases;
    private readonly Histogram<double> _lockHoldDuration;

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
        _lockAcquisitions = meter.CreateCounter<long>(
            "clubhub.redis.lock.acquisitions",
            description: "Distributed lock acquisition outcomes.");
        _lockWaitDuration = meter.CreateHistogram<double>(
            "clubhub.redis.lock.wait.duration",
            unit: "ms",
            description: "Distributed lock acquisition wait duration.");
        _lockRenewals = meter.CreateCounter<long>(
            "clubhub.redis.lock.renewals",
            description: "Distributed lock renewal outcomes.");
        _lockLeaseLosses = meter.CreateCounter<long>(
            "clubhub.redis.lock.lease.losses",
            description: "Distributed lock leases that became invalid.");
        _lockReleases = meter.CreateCounter<long>(
            "clubhub.redis.lock.releases",
            description: "Distributed lock release outcomes.");
        _lockHoldDuration = meter.CreateHistogram<double>(
            "clubhub.redis.lock.hold.duration",
            unit: "ms",
            description: "Distributed lock hold duration.");
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

    public void RecordLockAcquisition(
        string policyName,
        string outcome,
        double elapsedMilliseconds,
        int resourceCount)
    {
        _lockAcquisitions.Add(
            1,
            new KeyValuePair<string, object?>("policy", policyName),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("resource_count", resourceCount));
        _lockWaitDuration.Record(
            elapsedMilliseconds,
            new KeyValuePair<string, object?>("policy", policyName),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordLockRenewal(string policyName, string outcome) =>
        _lockRenewals.Add(
            1,
            new KeyValuePair<string, object?>("policy", policyName),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordLockLeaseLost(string policyName, string reason) =>
        _lockLeaseLosses.Add(
            1,
            new KeyValuePair<string, object?>("policy", policyName),
            new KeyValuePair<string, object?>("reason", reason));

    public void RecordLockRelease(string policyName, string outcome) =>
        _lockReleases.Add(
            1,
            new KeyValuePair<string, object?>("policy", policyName),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordLockHold(string policyName, double elapsedMilliseconds) =>
        _lockHoldDuration.Record(
            elapsedMilliseconds,
            new KeyValuePair<string, object?>("policy", policyName));
}
