using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Infrastructure.Redis;

public enum RedisCacheReadStatus
{
    Hit,
    Miss,
    Disabled,
    Unavailable,
    InvalidPayload
}

public enum RedisCacheWriteStatus
{
    Succeeded,
    Disabled,
    Unavailable,
    PayloadTooLarge
}

public readonly record struct RedisCacheReadResult<T>(
    RedisCacheReadStatus Status,
    T? Value,
    bool IsNull);

public interface IRedisCacheService
{
    Task<RedisCacheReadResult<T>> GetAsync<T>(
        RedisKey key,
        CancellationToken cancellationToken = default);

    Task<RedisCacheWriteStatus> SetAsync<T>(
        RedisKey key,
        T? value,
        RedisCachePolicy? policy = null,
        CancellationToken cancellationToken = default);

    Task<RedisCacheWriteStatus> RemoveAsync(
        RedisKey key,
        CancellationToken cancellationToken = default);

    Task<T?> GetOrCreateAsync<T>(
        RedisKey key,
        Func<CancellationToken, Task<T?>> source,
        RedisCachePolicy? policy = null,
        CancellationToken cancellationToken = default);
}

public sealed class RedisCacheService : IRedisCacheService
{
    private readonly IRedisDatabase _database;
    private readonly IRedisCacheSerializer _serializer;
    private readonly IRedisTtlPolicy _ttlPolicy;
    private readonly IRedisKeyBuilder _keyBuilder;
    private readonly IDistributedLockService _distributedLocks;
    private readonly RedisOptions _options;
    private readonly RedisMetrics _metrics;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly object _missLocksSync = new();
    private readonly Dictionary<string, MissLockEntry> _missLocks = new(StringComparer.Ordinal);

    public RedisCacheService(
        IRedisDatabase database,
        IRedisCacheSerializer serializer,
        IRedisTtlPolicy ttlPolicy,
        IRedisKeyBuilder keyBuilder,
        IDistributedLockService distributedLocks,
        IOptions<RedisOptions> options,
        RedisMetrics metrics,
        ILogger<RedisCacheService> logger)
    {
        _database = database;
        _serializer = serializer;
        _ttlPolicy = ttlPolicy;
        _keyBuilder = keyBuilder;
        _distributedLocks = distributedLocks;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<RedisCacheReadResult<T>> GetAsync<T>(
        RedisKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCacheEnabled)
        {
            return new(RedisCacheReadStatus.Disabled, default, false);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var cached = await _database.StringGetAsync(key, cancellationToken);
            if (!cached.HasValue)
            {
                _metrics.RecordCacheRead("miss", stopwatch.Elapsed.TotalMilliseconds);
                return new(RedisCacheReadStatus.Miss, default, false);
            }

            var payload = (byte[])cached!;
            if (payload.Length > _options.MaxPayloadBytes)
            {
                _metrics.RecordCacheRead("invalid-payload", stopwatch.Elapsed.TotalMilliseconds);
                _logger.LogWarning(
                    "Redis cache payload exceeded the configured {MaxPayloadBytes} byte limit.",
                    _options.MaxPayloadBytes);
                await TryDeleteInvalidPayloadAsync(key, cancellationToken);
                return new(RedisCacheReadStatus.InvalidPayload, default, false);
            }

            var parsed = _serializer.Deserialize<T>(payload);
            if (parsed.Status == RedisPayloadReadStatus.Success)
            {
                _metrics.RecordCacheRead("hit", stopwatch.Elapsed.TotalMilliseconds);
                return new(RedisCacheReadStatus.Hit, parsed.Value, parsed.IsNull);
            }

            _metrics.RecordCacheRead("invalid-payload", stopwatch.Elapsed.TotalMilliseconds);
            _logger.LogWarning(
                "Redis cache payload could not be read because its status was {PayloadStatus}.",
                parsed.Status);
            await TryDeleteInvalidPayloadAsync(key, cancellationToken);
            return new(RedisCacheReadStatus.InvalidPayload, default, false);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            _metrics.RecordCacheRead("unavailable", stopwatch.Elapsed.TotalMilliseconds);
            _metrics.RecordFailure("cache-read");
            _logger.LogWarning(
                exception,
                "Redis cache read failed; the caller may use its source of truth.");
            return new(RedisCacheReadStatus.Unavailable, default, false);
        }
    }

    public async Task<RedisCacheWriteStatus> SetAsync<T>(
        RedisKey key,
        T? value,
        RedisCachePolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        policy?.Validate();
        if (!IsCacheEnabled)
        {
            return RedisCacheWriteStatus.Disabled;
        }

        var stopwatch = Stopwatch.StartNew();
        byte[] payload;
        try
        {
            payload = _serializer.Serialize(value);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            _metrics.RecordCacheWrite(
                "cache-write",
                "serialization-failed",
                stopwatch.Elapsed.TotalMilliseconds);
            _metrics.RecordFailure("cache-write-serialization");
            _logger.LogWarning(
                exception,
                "Redis cache write skipped because the value could not be serialized.");
            return RedisCacheWriteStatus.Unavailable;
        }

        if (payload.Length > _options.MaxPayloadBytes)
        {
            _metrics.RecordCacheWrite(
                "cache-write",
                "payload-too-large",
                stopwatch.Elapsed.TotalMilliseconds);
            _logger.LogWarning(
                "Redis cache write skipped because the payload exceeded the configured "
                + "{MaxPayloadBytes} byte limit.",
                _options.MaxPayloadBytes);
            return RedisCacheWriteStatus.PayloadTooLarge;
        }

        try
        {
            var isNullValue = value is null;
            var expiration = _ttlPolicy.GetExpiration(
                isNullValue ? policy?.NullTtl : policy?.Ttl,
                isNullValue,
                isNullValue
                    ? policy?.NullTtlJitterRatio
                    : policy?.TtlJitterRatio);
            var succeeded = await _database.StringSetAsync(
                key,
                payload,
                expiration,
                cancellationToken);
            var outcome = succeeded ? "succeeded" : "rejected";
            _metrics.RecordCacheWrite("cache-write", outcome, stopwatch.Elapsed.TotalMilliseconds);
            return succeeded
                ? RedisCacheWriteStatus.Succeeded
                : RedisCacheWriteStatus.Unavailable;
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            _metrics.RecordCacheWrite(
                "cache-write",
                "unavailable",
                stopwatch.Elapsed.TotalMilliseconds);
            _metrics.RecordFailure("cache-write");
            _logger.LogWarning(
                exception,
                "Redis cache write failed; the source-of-truth result remains authoritative.");
            return RedisCacheWriteStatus.Unavailable;
        }
    }

    public async Task<RedisCacheWriteStatus> RemoveAsync(
        RedisKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCacheEnabled)
        {
            return RedisCacheWriteStatus.Disabled;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _database.KeyDeleteAsync(key, cancellationToken);
            _metrics.RecordCacheWrite(
                "cache-delete",
                "succeeded",
                stopwatch.Elapsed.TotalMilliseconds);
            return RedisCacheWriteStatus.Succeeded;
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            _metrics.RecordCacheWrite(
                "cache-delete",
                "unavailable",
                stopwatch.Elapsed.TotalMilliseconds);
            _metrics.RecordFailure("cache-delete");
            _logger.LogWarning(exception, "Redis cache deletion failed.");
            return RedisCacheWriteStatus.Unavailable;
        }
    }

    public async Task<T?> GetOrCreateAsync<T>(
        RedisKey key,
        Func<CancellationToken, Task<T?>> source,
        RedisCachePolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        policy ??= CreateDefaultPolicy();
        policy.Validate();

        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached.Status == RedisCacheReadStatus.Hit)
        {
            return cached.Value;
        }

        if (cached.Status is RedisCacheReadStatus.Disabled or RedisCacheReadStatus.Unavailable)
        {
            return await LoadSourceAsync(source, policy.Name, cancellationToken);
        }

        var lockKey = $"{typeof(T).AssemblyQualifiedName}:{key}";
        var entry = AddMissLockReference(lockKey);
        var acquired = false;
        try
        {
            await entry.Gate.WaitAsync(cancellationToken);
            acquired = true;

            cached = await GetAsync<T>(key, cancellationToken);
            if (cached.Status == RedisCacheReadStatus.Hit)
            {
                return cached.Value;
            }

            if (cached.Status is RedisCacheReadStatus.Disabled or RedisCacheReadStatus.Unavailable)
            {
                return await LoadSourceAsync(source, policy.Name, cancellationToken);
            }

            var leaseKey = _keyBuilder.Build(
                "cache",
                "rebuild",
                _keyBuilder.HashSensitive(key.ToString()));
            IDistributedLockHandle? rebuildLock = null;
            try
            {
                rebuildLock = await _distributedLocks.TryAcquireAsync(
                    leaseKey.ToString(),
                    new DistributedLockPolicy(
                        "cache-rebuild",
                        TimeSpan.Zero,
                        policy.EffectiveRebuildLeaseTtl,
                        policy.EffectiveRebuildPollInterval),
                    cancellationToken);
                _metrics.RecordRebuildLease(
                    policy.Name,
                    rebuildLock is null ? "contended" : "acquired");
            }
            catch (DistributedLockUnavailableException exception)
            {
                _metrics.RecordRebuildLease(policy.Name, "unavailable");
                _metrics.RecordFailure("cache-rebuild-lease-acquire");
                _logger.LogWarning(
                    exception,
                    "Redis cache rebuild lease acquisition failed for {CacheName}.",
                    policy.Name);
            }

            if (rebuildLock is null)
            {
                var contendedValue = await WaitForRebuildAsync<T>(
                    key,
                    policy,
                    cancellationToken);
                if (contendedValue.HasValue)
                {
                    return contendedValue.Value;
                }

                return await LoadSourceAsync(source, policy.Name, cancellationToken);
            }

            await using (rebuildLock)
            {
                var sourceValue = await LoadSourceAsync(source, policy.Name, cancellationToken);
                await SetAsync(key, sourceValue, policy, cancellationToken);
                return sourceValue;
            }
        }
        finally
        {
            if (acquired)
            {
                entry.Gate.Release();
            }

            ReleaseMissLockReference(lockKey, entry);
        }
    }

    private bool IsCacheEnabled => _options.Enabled && _options.Features.Cache;

    private RedisCachePolicy CreateDefaultPolicy() =>
        new(
            "default",
            TimeSpan.FromSeconds(_options.DefaultTtlSeconds),
            TimeSpan.FromSeconds(_options.NullValueTtlSeconds),
            _options.TtlJitterRatio,
            _options.TtlJitterRatio);

    private async Task<T?> LoadSourceAsync<T>(
        Func<CancellationToken, Task<T?>> source,
        string cacheName,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await source(cancellationToken);
            _metrics.RecordSourceLoad(
                cacheName,
                value is null ? "null" : "success",
                stopwatch.Elapsed.TotalMilliseconds);
            return value;
        }
        catch (OperationCanceledException)
        {
            _metrics.RecordSourceLoad(
                cacheName,
                "canceled",
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            _metrics.RecordSourceLoad(
                cacheName,
                "failure",
                stopwatch.Elapsed.TotalMilliseconds);
            _metrics.RecordFailure("cache-source-load");
            _logger.LogWarning(
                exception,
                "Cache source load failed for {CacheName}.",
                cacheName);
            throw;
        }
    }

    private async Task<(bool HasValue, T? Value)> WaitForRebuildAsync<T>(
        RedisKey key,
        RedisCachePolicy policy,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < policy.EffectiveRebuildWaitTimeout)
        {
            var cached = await GetAsync<T>(key, cancellationToken);
            if (cached.Status == RedisCacheReadStatus.Hit)
            {
                _metrics.RecordRebuildLease(policy.Name, "wait-hit");
                return (true, cached.Value);
            }

            if (cached.Status is RedisCacheReadStatus.Disabled or RedisCacheReadStatus.Unavailable)
            {
                break;
            }

            var remaining = policy.EffectiveRebuildWaitTimeout - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(
                remaining < policy.EffectiveRebuildPollInterval
                    ? remaining
                    : policy.EffectiveRebuildPollInterval,
                cancellationToken);
        }

        _metrics.RecordRebuildLease(policy.Name, "wait-timeout");
        return (false, default);
    }

    private MissLockEntry AddMissLockReference(string key)
    {
        lock (_missLocksSync)
        {
            if (!_missLocks.TryGetValue(key, out var entry))
            {
                entry = new MissLockEntry();
                _missLocks.Add(key, entry);
            }

            entry.ReferenceCount++;
            return entry;
        }
    }

    private void ReleaseMissLockReference(string key, MissLockEntry entry)
    {
        lock (_missLocksSync)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                _missLocks.Remove(key);
                entry.Gate.Dispose();
            }
        }
    }

    private async Task TryDeleteInvalidPayloadAsync(
        RedisKey key,
        CancellationToken cancellationToken)
    {
        try
        {
            await _database.KeyDeleteAsync(key, cancellationToken);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            _metrics.RecordFailure("invalid-payload-delete");
            _logger.LogDebug(
                exception,
                "Redis invalid cache payload could not be deleted.");
        }
    }

    private static bool IsRedisFailure(Exception exception) =>
        exception is RedisException or TimeoutException or ObjectDisposedException;

    private sealed class MissLockEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }
}
