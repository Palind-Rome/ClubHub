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

        public CacheServiceTestContext(bool enabled = true, int maxPayloadBytes = 256 * 1024)
        {
            Database = new FakeRedisDatabase();
            var options = Options.Create(
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
                new RedisTtlPolicy(options),
                options,
                metrics,
                NullLogger<RedisCacheService>.Instance);
        }

        public FakeRedisDatabase Database { get; }

        public RedisCacheService Service { get; }

        public void Dispose() => _serviceProvider.Dispose();
    }

    private sealed class FakeRedisDatabase : IRedisDatabase
    {
        private readonly Dictionary<string, RedisValue> _values = [];

        public Exception? ExceptionToThrow { get; set; }

        public int ReadCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public int WriteCalls { get; private set; }

        public TimeSpan LastExpiration { get; private set; }

        public bool BlockReads { get; set; }

        public async Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            ThrowIfConfigured();
            if (BlockReads)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

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
            WriteCalls++;
            ThrowIfConfigured();
            _values[key.ToString()] = value;
            LastExpiration = expiration;
            return Task.FromResult(true);
        }

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCalls++;
            ThrowIfConfigured();
            return Task.FromResult(_values.Remove(key.ToString()));
        }

        public Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfConfigured();
            return Task.FromResult(TimeSpan.FromMilliseconds(1));
        }

        public void SetRaw(RedisKey key, RedisValue value) =>
            _values[key.ToString()] = value;

        private void ThrowIfConfigured()
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
        }
    }
}
