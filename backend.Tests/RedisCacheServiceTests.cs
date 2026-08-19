using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Tests;

public sealed class RedisCacheServiceTests
{
    private static readonly RedisKey TestKey = "clubhub:test:club:detail:v1:42";

    [Fact]
    public async Task GetOrCreateReadsSourceOnMissThenUsesCachedValue()
    {
        using var context = new CacheServiceTestContext();
        var sourceCalls = 0;

        var first = await context.Service.GetOrCreateAsync(
            TestKey,
            _ =>
            {
                sourceCalls++;
                return Task.FromResult<string?>("oracle-value");
            });
        var second = await context.Service.GetOrCreateAsync<string>(
            TestKey,
            _ =>
            {
                sourceCalls++;
                return Task.FromResult<string?>("unexpected");
            });

        Assert.Equal("oracle-value", first);
        Assert.Equal("oracle-value", second);
        Assert.Equal(1, sourceCalls);
        Assert.InRange(
            context.Database.LastExpiration,
            TimeSpan.FromSeconds(270),
            TimeSpan.FromSeconds(330));
    }

    [Fact]
    public async Task GetOrCreateCachesNullWithShorterTtl()
    {
        using var context = new CacheServiceTestContext();
        var sourceCalls = 0;

        var first = await context.Service.GetOrCreateAsync<string>(
            TestKey,
            _ =>
            {
                sourceCalls++;
                return Task.FromResult<string?>(null);
            });
        var second = await context.Service.GetOrCreateAsync<string>(
            TestKey,
            _ =>
            {
                sourceCalls++;
                return Task.FromResult<string?>("unexpected");
            });

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, sourceCalls);
        Assert.InRange(
            context.Database.LastExpiration,
            TimeSpan.FromSeconds(27),
            TimeSpan.FromSeconds(33));
    }

    [Fact]
    public async Task GetOrCreateFallsBackToSourceWhenRedisIsUnavailable()
    {
        using var context = new CacheServiceTestContext();
        context.Database.ExceptionToThrow = new TimeoutException("simulated");

        var result = await context.Service.GetOrCreateAsync(
            TestKey,
            _ => Task.FromResult<string?>("oracle-value"));

        Assert.Equal("oracle-value", result);
        Assert.Equal(0, context.Database.WriteCalls);
    }

    [Fact]
    public async Task InvalidPayloadIsDeletedAndReportedAsMissEquivalent()
    {
        using var context = new CacheServiceTestContext();
        context.Database.SetRaw(TestKey, "not-json");

        var result = await context.Service.GetAsync<string>(TestKey);

        Assert.Equal(RedisCacheReadStatus.InvalidPayload, result.Status);
        Assert.Equal(1, context.Database.DeleteCalls);
    }

    [Fact]
    public async Task DisabledCacheDoesNotAccessRedis()
    {
        using var context = new CacheServiceTestContext(enabled: false);

        var result = await context.Service.GetAsync<string>(TestKey);

        Assert.Equal(RedisCacheReadStatus.Disabled, result.Status);
        Assert.Equal(0, context.Database.ReadCalls);
    }

    [Fact]
    public async Task SetRejectsPayloadLargerThanConfiguredLimit()
    {
        using var context = new CacheServiceTestContext(maxPayloadBytes: 64);

        var result = await context.Service.SetAsync(TestKey, new string('x', 128));

        Assert.Equal(RedisCacheWriteStatus.PayloadTooLarge, result);
        Assert.Equal(0, context.Database.WriteCalls);
    }

    [Fact]
    public async Task SerializationFailureDoesNotHideSourceResult()
    {
        using var context = new CacheServiceTestContext();
        var cyclic = new CyclicValue();
        cyclic.Self = cyclic;

        var writeStatus = await context.Service.SetAsync(TestKey, cyclic);
        var sourceResult = await context.Service.GetOrCreateAsync(
            TestKey,
            _ => Task.FromResult<CyclicValue?>(cyclic));

        Assert.Equal(RedisCacheWriteStatus.Unavailable, writeStatus);
        Assert.Same(cyclic, sourceResult);
        Assert.Equal(0, context.Database.WriteCalls);
    }

    [Fact]
    public async Task ConcurrentMissesForSameKeyShareOneSourceCall()
    {
        using var context = new CacheServiceTestContext();
        var sourceStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceCalls = 0;

        async Task<string?> Source(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref sourceCalls);
            sourceStarted.TrySetResult();
            await releaseSource.Task.WaitAsync(cancellationToken);
            return "oracle-value";
        }

        var first = context.Service.GetOrCreateAsync(TestKey, Source);
        await sourceStarted.Task;
        var second = context.Service.GetOrCreateAsync(TestKey, Source);
        releaseSource.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal("oracle-value", result));
        Assert.Equal(1, sourceCalls);
        Assert.Equal(1, context.Database.WriteCalls);
    }

    [Fact]
    public async Task SeparateInstancesUseOneDistributedRebuildLease()
    {
        var database = new FakeRedisDatabase();
        using var firstContext = new CacheServiceTestContext(database: database);
        using var secondContext = new CacheServiceTestContext(database: database);
        var sourceStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceCalls = 0;

        async Task<string?> Source(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref sourceCalls);
            sourceStarted.TrySetResult();
            await releaseSource.Task.WaitAsync(cancellationToken);
            return "oracle-value";
        }

        var first = firstContext.Service.GetOrCreateAsync(TestKey, Source);
        await sourceStarted.Task;
        var second = secondContext.Service.GetOrCreateAsync(TestKey, Source);
        var waitDeadline = DateTime.UtcNow.AddSeconds(5);
        while (database.LeaseContentionCount == 0 && DateTime.UtcNow < waitDeadline)
        {
            await Task.Delay(10);
        }

        Assert.True(database.LeaseContentionCount >= 1);
        releaseSource.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.All(results, value => Assert.Equal("oracle-value", value));
        Assert.Equal(1, sourceCalls);
        Assert.True(database.LeaseContentionCount >= 1);
    }

    [Fact]
    public async Task SourceFailureReleasesLeaseForNextRebuild()
    {
        var database = new FakeRedisDatabase();
        using var firstContext = new CacheServiceTestContext(database: database);
        using var secondContext = new CacheServiceTestContext(database: database);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            firstContext.Service.GetOrCreateAsync<string>(
                TestKey,
                _ => throw new InvalidOperationException("simulated source failure")));

        var recovered = await secondContext.Service.GetOrCreateAsync(
            TestKey,
            _ => Task.FromResult<string?>("recovered"));

        Assert.Equal("recovered", recovered);
        Assert.Equal(2, database.LeaseAcquisitionCount);
    }

    [Fact]
    public async Task SourceAndLeaseMetricsArePublished()
    {
        using var context = new CacheServiceTestContext();
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

        var value = await context.Service.GetOrCreateAsync(
            TestKey,
            _ => Task.FromResult<string?>("oracle-value"));

        Assert.Equal("oracle-value", value);
        Assert.Contains("clubhub.redis.cache.source.loads", measurements);
        Assert.Contains("clubhub.redis.cache.source.duration", measurements);
        Assert.Contains("clubhub.redis.cache.rebuild.leases", measurements);
    }

    [Fact]
    public async Task ContendedLeaseWaitTimeoutFallsBackWithoutHanging()
    {
        using var context = new CacheServiceTestContext();
        var keyBuilder = new RedisKeyBuilder(context.Configuration);
        var leaseKey = keyBuilder.Build(
            "cache",
            "rebuild",
            keyBuilder.HashSensitive(TestKey.ToString()));
        context.Database.SetRaw(leaseKey, "another-owner");
        var policy = new RedisCachePolicy(
            "club-detail",
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(10),
            RebuildWaitTimeout: TimeSpan.FromMilliseconds(100));
        var sourceCalls = 0;

        var result = await context.Service.GetOrCreateAsync(
            TestKey,
            _ =>
            {
                sourceCalls++;
                return Task.FromResult<string?>("oracle-value");
            },
            policy);

        Assert.Equal("oracle-value", result);
        Assert.Equal(1, sourceCalls);
        Assert.True(context.Database.LeaseContentionCount >= 1);
    }

    [Fact]
    public async Task LeaseReleaseFailureDoesNotHideSourceResult()
    {
        using var context = new CacheServiceTestContext();
        context.Database.LeaseReleaseException = new TimeoutException("simulated");

        var result = await context.Service.GetOrCreateAsync(
            TestKey,
            _ => Task.FromResult<string?>("oracle-value"));

        Assert.Equal("oracle-value", result);
    }

    [Fact]
    public async Task CacheReadHonorsCancellation()
    {
        using var context = new CacheServiceTestContext();
        context.Database.BlockReads = true;
        using var cancellation = new CancellationTokenSource();

        var read = context.Service.GetAsync<string>(TestKey, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    private sealed class CyclicValue
    {
        public CyclicValue? Self { get; set; }
    }

    private sealed class CacheServiceTestContext : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        public CacheServiceTestContext(
            bool enabled = true,
            int maxPayloadBytes = 256 * 1024,
            FakeRedisDatabase? database = null)
        {
            Database = database ?? new FakeRedisDatabase();
            Configuration = Options.Create(
                new RedisOptions
                {
                    Enabled = enabled,
                    DefaultTtlSeconds = 300,
                    NullValueTtlSeconds = 30,
                    MaxPayloadBytes = maxPayloadBytes,
                    TtlJitterRatio = 0.1,
                    Features = new RedisFeatureOptions { Cache = enabled }
                });

            var services = new ServiceCollection();
            services.AddMetrics();
            _serviceProvider = services.BuildServiceProvider();
            var metrics = new RedisMetrics(
                _serviceProvider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());

            Service = new RedisCacheService(
                Database,
                new RedisCacheSerializer(),
                new RedisTtlPolicy(Configuration),
                new RedisKeyBuilder(Configuration),
                Configuration,
                metrics,
                NullLogger<RedisCacheService>.Instance);
        }

        public FakeRedisDatabase Database { get; }

        public IOptions<RedisOptions> Configuration { get; }

        public RedisCacheService Service { get; }

        public void Dispose() => _serviceProvider.Dispose();
    }

    private sealed class FakeRedisDatabase : IRedisDatabase
    {
        private readonly ConcurrentDictionary<string, RedisValue> _values = [];
        private readonly ConcurrentDictionary<string, DateTimeOffset> _expirations = [];
        private int _deleteCalls;
        private int _leaseAcquisitionCount;
        private int _leaseContentionCount;
        private int _readCalls;
        private int _writeCalls;

        public Exception? ExceptionToThrow { get; set; }

        public int ReadCalls => Volatile.Read(ref _readCalls);

        public int DeleteCalls => Volatile.Read(ref _deleteCalls);

        public int WriteCalls => Volatile.Read(ref _writeCalls);

        public TimeSpan LastExpiration { get; private set; }

        public bool BlockReads { get; set; }

        public Exception? LeaseReleaseException { get; set; }

        public int LeaseAcquisitionCount => Volatile.Read(ref _leaseAcquisitionCount);

        public int LeaseContentionCount => Volatile.Read(ref _leaseContentionCount);

        public async Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCalls);
            ThrowIfConfigured();
            if (BlockReads)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            RemoveIfExpired(key.ToString());
            return _values.TryGetValue(key.ToString(), out var value)
                ? value
                : RedisValue.Null;
        }

        public Task<bool> StringSetAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _writeCalls);
            ThrowIfConfigured();
            var stringKey = key.ToString();
            _values[stringKey] = value;
            _expirations[stringKey] = DateTimeOffset.UtcNow.Add(expiration);
            LastExpiration = expiration;
            return Task.FromResult(true);
        }

        public Task<bool> StringSetIfNotExistsAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfConfigured();
            var stringKey = key.ToString();
            RemoveIfExpired(stringKey);
            if (!_values.TryAdd(stringKey, value))
            {
                Interlocked.Increment(ref _leaseContentionCount);
                return Task.FromResult(false);
            }

            _expirations[stringKey] = DateTimeOffset.UtcNow.Add(expiration);
            Interlocked.Increment(ref _leaseAcquisitionCount);
            return Task.FromResult(true);
        }

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _deleteCalls);
            ThrowIfConfigured();
            var stringKey = key.ToString();
            _expirations.TryRemove(stringKey, out _);
            return Task.FromResult(_values.TryRemove(stringKey, out _));
        }

        public Task<bool> KeyDeleteIfValueMatchesAsync(
            RedisKey key,
            RedisValue expectedValue,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfConfigured();
            if (LeaseReleaseException is not null)
            {
                throw LeaseReleaseException;
            }

            var stringKey = key.ToString();
            if (!_values.TryGetValue(stringKey, out var value) || value != expectedValue)
            {
                return Task.FromResult(false);
            }

            _expirations.TryRemove(stringKey, out _);
            return Task.FromResult(_values.TryRemove(stringKey, out _));
        }

        public Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfConfigured();
            return Task.FromResult(TimeSpan.FromMilliseconds(1));
        }

        public void SetRaw(RedisKey key, RedisValue value) =>
            _values[key.ToString()] = value;

        private void RemoveIfExpired(string key)
        {
            if (_expirations.TryGetValue(key, out var expiresAt) &&
                expiresAt <= DateTimeOffset.UtcNow)
            {
                _expirations.TryRemove(key, out _);
                _values.TryRemove(key, out _);
            }
        }

        private void ThrowIfConfigured()
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
        }
    }
}
