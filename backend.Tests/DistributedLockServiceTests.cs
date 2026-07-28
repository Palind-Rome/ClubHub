using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ClubHub.Api.Tests;

public sealed class DistributedLockServiceTests
{
    [Fact]
    public async Task ContendingCallerTimesOutWithoutTakingTheLock()
    {
        var database = new InMemoryRedisDatabase();
        using var services = CreateServices(database);
        var locks = services.GetRequiredService<IDistributedLockService>();
        var key = (RedisKey)"clubhub:test:lock:v1:resource";
        var policy = Policy(wait: TimeSpan.FromMilliseconds(40));

        await using var first = await locks.TryAcquireAsync(key, policy);
        var second = await locks.TryAcquireAsync(key, policy);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task CancellationStopsBoundedContentionWait()
    {
        var database = new InMemoryRedisDatabase();
        using var services = CreateServices(database);
        var locks = services.GetRequiredService<IDistributedLockService>();
        var key = (RedisKey)"clubhub:test:lock:v1:cancel";
        var policy = Policy(wait: TimeSpan.FromSeconds(5));
        await using var first = await locks.TryAcquireAsync(key, policy);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            locks.TryAcquireAsync(key, policy, cancellation.Token));
    }

    [Fact]
    public async Task MultiResourceAcquisitionUsesFixedOrderAndReleasesPartialAttempt()
    {
        var database = new InMemoryRedisDatabase();
        database.SetRaw("clubhub:test:lock:v1:b", "another-owner", TimeSpan.FromMinutes(1));
        using var services = CreateServices(database);
        var locks = services.GetRequiredService<IDistributedLockService>();
        var policy = Policy(wait: TimeSpan.Zero);

        var result = await locks.TryAcquireAsync(
            [
                "clubhub:test:lock:v1:c",
                "clubhub:test:lock:v1:b",
                "clubhub:test:lock:v1:a",
                "clubhub:test:lock:v1:a"
            ],
            policy);

        Assert.Null(result);
        Assert.Equal(
            ["clubhub:test:lock:v1:a", "clubhub:test:lock:v1:b"],
            database.AcquisitionAttempts);
        Assert.False(database.Contains("clubhub:test:lock:v1:a"));
        Assert.False(database.Contains("clubhub:test:lock:v1:c"));
    }

    [Fact]
    public async Task ExpiredOwnerCannotDeleteTheReplacementOwner()
    {
        var database = new InMemoryRedisDatabase();
        using var services = CreateServices(database);
        var locks = services.GetRequiredService<IDistributedLockService>();
        var key = (RedisKey)"clubhub:test:lock:v1:takeover";
        var expiring = Policy(
            wait: TimeSpan.Zero,
            lease: TimeSpan.FromMilliseconds(700));

        var oldHandle = await locks.TryAcquireAsync(key, expiring);
        Assert.NotNull(oldHandle);
        await Task.Delay(1100);

        var replacement = await locks.TryAcquireAsync(
            key,
            Policy(wait: TimeSpan.Zero, lease: TimeSpan.FromSeconds(2)));
        Assert.NotNull(replacement);

        await oldHandle.DisposeAsync();
        var third = await locks.TryAcquireAsync(key, Policy(wait: TimeSpan.Zero));
        Assert.Null(third);

        await replacement.DisposeAsync();
        await using var afterRelease = await locks.TryAcquireAsync(key, Policy(wait: TimeSpan.Zero));
        Assert.NotNull(afterRelease);
    }

    [Fact]
    public async Task RenewalKeepsTheLeasePastItsOriginalTtl()
    {
        var database = new InMemoryRedisDatabase();
        using var services = CreateServices(database);
        var locks = services.GetRequiredService<IDistributedLockService>();
        var key = (RedisKey)"clubhub:test:lock:v1:renew";
        var renewing = Policy(
            wait: TimeSpan.Zero,
            lease: TimeSpan.FromMilliseconds(600),
            renewal: TimeSpan.FromMilliseconds(100));

        await using var handle = await locks.TryAcquireAsync(key, renewing);
        Assert.NotNull(handle);
        await Task.Delay(1200);

        var contender = await locks.TryAcquireAsync(key, Policy(wait: TimeSpan.Zero));

        Assert.Null(contender);
        Assert.True(handle.IsLeaseValid);
    }

    [Fact]
    public async Task OwnerChangeSignalsLeaseLoss()
    {
        var database = new InMemoryRedisDatabase();
        using var services = CreateServices(database);
        var locks = services.GetRequiredService<IDistributedLockService>();
        var key = (RedisKey)"clubhub:test:lock:v1:lost";
        var policy = Policy(
            wait: TimeSpan.Zero,
            lease: TimeSpan.FromMilliseconds(200),
            renewal: TimeSpan.FromMilliseconds(30));

        await using var handle = await locks.TryAcquireAsync(key, policy);
        Assert.NotNull(handle);
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = handle.LeaseLost.Register(lost.SetResult);
        database.SetRaw(key, "replacement-owner", TimeSpan.FromSeconds(2));
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(handle.IsLeaseValid);
        Assert.Throws<DistributedLockLeaseLostException>(handle.ThrowIfLeaseLost);
    }

    [Fact]
    public async Task RedisFailureIsReportedAsUnavailable()
    {
        var database = new InMemoryRedisDatabase { AcquireException = new RedisException("offline") };
        using var services = CreateServices(database);
        var locks = services.GetRequiredService<IDistributedLockService>();

        await Assert.ThrowsAsync<DistributedLockUnavailableException>(() =>
            locks.TryAcquireAsync(
                "clubhub:test:lock:v1:offline",
                Policy(wait: TimeSpan.Zero)));
    }

    [Fact]
    public async Task LockMetricsIncludeAcquisitionRenewalReleaseAndHold()
    {
        var measurements = new ConcurrentBag<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == RedisMetrics.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.SetMeasurementEventCallback<double>(
            (instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.Start();

        var database = new InMemoryRedisDatabase();
        using var services = CreateServices(database);
        var locks = services.GetRequiredService<IDistributedLockService>();
        await using (var handle = await locks.TryAcquireAsync(
            "clubhub:test:lock:v1:metrics",
            Policy(
                wait: TimeSpan.Zero,
                lease: TimeSpan.FromMilliseconds(100),
                renewal: TimeSpan.FromMilliseconds(20))))
        {
            Assert.NotNull(handle);
            await Task.Delay(60);
            var lost = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = handle.LeaseLost.Register(lost.SetResult);
            database.SetRaw(
                "clubhub:test:lock:v1:metrics",
                "replacement-owner",
                TimeSpan.FromSeconds(1));
            await lost.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Contains("clubhub.redis.lock.acquisitions", measurements);
        Assert.Contains("clubhub.redis.lock.wait.duration", measurements);
        Assert.Contains("clubhub.redis.lock.renewals", measurements);
        Assert.Contains("clubhub.redis.lock.lease.losses", measurements);
        Assert.Contains("clubhub.redis.lock.releases", measurements);
        Assert.Contains("clubhub.redis.lock.hold.duration", measurements);
    }

    [Fact]
    public async Task ContentionMetricsDistinguishImmediateTimeoutFromWaitedContention()
    {
        var outcomes = new ConcurrentBag<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == RedisMetrics.MeterName &&
                    instrument.Name == "clubhub.redis.lock.acquisitions")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "outcome" && tag.Value is string outcome)
                    {
                        outcomes.Add(outcome);
                    }
                }
            });
        listener.Start();

        var database = new InMemoryRedisDatabase();
        database.SetRaw(
            "clubhub:test:lock:v1:outcomes",
            "another-owner",
            TimeSpan.FromSeconds(5));
        using var services = CreateServices(database);
        var locks = services.GetRequiredService<IDistributedLockService>();

        Assert.Null(await locks.TryAcquireAsync(
            "clubhub:test:lock:v1:outcomes",
            Policy(wait: TimeSpan.Zero)));
        Assert.Null(await locks.TryAcquireAsync(
            "clubhub:test:lock:v1:outcomes",
            Policy(wait: TimeSpan.FromMilliseconds(40))));

        Assert.Contains("timeout", outcomes);
        Assert.Contains("contended", outcomes);
    }

    private static ServiceProvider CreateServices(IRedisDatabase database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton(database);
        services.AddSingleton<IRedisDatabase>(database);
        services.AddSingleton<RedisMetrics>();
        services.AddSingleton<IDistributedLockService, DistributedLockService>();
        return services.BuildServiceProvider();
    }

    private static DistributedLockPolicy Policy(
        TimeSpan wait,
        TimeSpan? lease = null,
        TimeSpan? renewal = null) =>
        new(
            "test-lock",
            wait,
            lease ?? TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(10),
            renewal);

    private sealed class InMemoryRedisDatabase : IRedisDatabase
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, Entry> _values = new(StringComparer.Ordinal);
        private readonly List<string> _acquisitionAttempts = [];

        public Exception? AcquireException { get; init; }

        public IReadOnlyList<string> AcquisitionAttempts
        {
            get
            {
                lock (_sync)
                {
                    return _acquisitionAttempts.ToArray();
                }
            }
        }

        public bool Contains(RedisKey key)
        {
            lock (_sync)
            {
                RemoveIfExpired(key);
                return _values.ContainsKey(key.ToString());
            }
        }

        public void SetRaw(RedisKey key, RedisValue value, TimeSpan expiration)
        {
            lock (_sync)
            {
                _values[key.ToString()] = new Entry(value, DateTimeOffset.UtcNow.Add(expiration));
            }
        }

        public Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                RemoveIfExpired(key);
                return Task.FromResult(
                    _values.TryGetValue(key.ToString(), out var entry)
                        ? entry.Value
                        : RedisValue.Null);
            }
        }

        public Task<bool> StringSetAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetRaw(key, value, expiration);
            return Task.FromResult(true);
        }

        public Task<bool> StringSetIfNotExistsAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AcquireException is not null)
            {
                throw AcquireException;
            }

            lock (_sync)
            {
                _acquisitionAttempts.Add(key.ToString());
                RemoveIfExpired(key);
                if (_values.ContainsKey(key.ToString()))
                {
                    return Task.FromResult(false);
                }

                _values.Add(
                    key.ToString(),
                    new Entry(value, DateTimeOffset.UtcNow.Add(expiration)));
                return Task.FromResult(true);
            }
        }

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult(_values.Remove(key.ToString()));
            }
        }

        public Task<bool> KeyDeleteIfValueMatchesAsync(
            RedisKey key,
            RedisValue expectedValue,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                RemoveIfExpired(key);
                if (!_values.TryGetValue(key.ToString(), out var entry) ||
                    entry.Value != expectedValue)
                {
                    return Task.FromResult(false);
                }

                return Task.FromResult(_values.Remove(key.ToString()));
            }
        }

        public Task<RedisResult> ScriptEvaluateAsync(
            string script,
            RedisKey[] keys,
            RedisValue[] values,
            CancellationToken cancellationToken = default)
        {
            _ = script;
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                var key = keys.Single();
                RemoveIfExpired(key);
                if (!_values.TryGetValue(key.ToString(), out var entry) ||
                    entry.Value != values[0])
                {
                    return Task.FromResult(RedisResult.Create((RedisValue)0));
                }

                entry.ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds((long)values[1]);
                return Task.FromResult(RedisResult.Create((RedisValue)1));
            }
        }

        public Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(TimeSpan.Zero);
        }

        private void RemoveIfExpired(RedisKey key)
        {
            if (_values.TryGetValue(key.ToString(), out var entry) &&
                entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _values.Remove(key.ToString());
            }
        }

        private sealed class Entry(RedisValue value, DateTimeOffset expiresAt)
        {
            public RedisValue Value { get; } = value;

            public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
        }
    }
}
