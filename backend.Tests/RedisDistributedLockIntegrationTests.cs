using System.Collections.Concurrent;
using ClubHub.Api.Infrastructure.Redis;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Tests;

public sealed class RedisDistributedLockIntegrationTests
{
    [Fact]
    public async Task ConcurrentInstancesAllowOnlyOneOwnerWhenCiRedisIsAvailable()
    {
        var connectionString = RedisTestConnection();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        using var services = CreateMetrics();
        var database = new DirectRedisDatabase(connection.GetDatabase());
        var firstInstance = CreateLockService(database, services);
        var secondInstance = CreateLockService(database, services);
        var key = (RedisKey)$"clubhub:test:lock:competition:v1:{Guid.NewGuid():N}";
        var policy = new DistributedLockPolicy(
            "integration-lock",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(20));

        var handles = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            (index % 2 == 0 ? firstInstance : secondInstance)
                .TryAcquireAsync(key, policy)));

        Assert.Single(handles, handle => handle is not null);
        foreach (var handle in handles.Where(handle => handle is not null))
        {
            await handle!.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExpiredOwnerCannotDeleteReplacementWhenCiRedisIsAvailable()
    {
        var connectionString = RedisTestConnection();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        using var services = CreateMetrics();
        var lockService = CreateLockService(
            new DirectRedisDatabase(connection.GetDatabase()),
            services);
        var key = (RedisKey)$"clubhub:test:lock:handoff:v1:{Guid.NewGuid():N}";
        var shortPolicy = new DistributedLockPolicy(
            "integration-lock",
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(120),
            TimeSpan.FromMilliseconds(20));
        var replacementPolicy = shortPolicy with { LeaseDuration = TimeSpan.FromSeconds(5) };

        var oldHandle = await lockService.TryAcquireAsync(key, shortPolicy);
        Assert.NotNull(oldHandle);
        await Task.Delay(250);
        var replacement = await lockService.TryAcquireAsync(key, replacementPolicy);
        Assert.NotNull(replacement);

        await oldHandle!.DisposeAsync();
        var third = await lockService.TryAcquireAsync(key, replacementPolicy);
        Assert.Null(third);

        await replacement!.DisposeAsync();
    }

    [Fact]
    public async Task AbandonedLeaseRecoversAfterTtlWhenCiRedisIsAvailable()
    {
        var connectionString = RedisTestConnection();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        using var services = CreateMetrics();
        var lockService = CreateLockService(
            new DirectRedisDatabase(connection.GetDatabase()),
            services);
        var key = (RedisKey)$"clubhub:test:lock:abandoned:v1:{Guid.NewGuid():N}";
        var policy = new DistributedLockPolicy(
            "integration-lock",
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(120),
            TimeSpan.FromMilliseconds(20));

        var abandoned = await lockService.TryAcquireAsync(key, policy);
        Assert.NotNull(abandoned);
        await Task.Delay(250);
        var recovered = await lockService.TryAcquireAsync(key, policy);

        Assert.NotNull(recovered);
        await recovered!.DisposeAsync();
        await abandoned!.DisposeAsync();
    }

    [Fact]
    public async Task DisconnectedRedisFailsClosedWhenCiRedisIsAvailable()
    {
        var connectionString = RedisTestConnection();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        using var services = CreateMetrics();
        var lockService = CreateLockService(
            new DirectRedisDatabase(connection.GetDatabase()),
            services);
        await connection.DisposeAsync();

        await Assert.ThrowsAsync<DistributedLockUnavailableException>(() =>
            lockService.TryAcquireAsync(
                (RedisKey)$"clubhub:test:lock:offline:v1:{Guid.NewGuid():N}",
                new DistributedLockPolicy(
                    "integration-lock",
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromMilliseconds(20))));
    }

    [Fact]
    public async Task OfficePreviewConvertsOneVersionOnceAcrossInstancesWhenCiRedisIsAvailable()
    {
        var connectionString = RedisTestConnection();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        using var metrics = CreateMetrics();
        var environmentPrefix = $"ci-lock-{Guid.NewGuid():N}"[..20];
        var redisOptions = Options.Create(new RedisOptions
        {
            Enabled = true,
            EnvironmentPrefix = environmentPrefix,
            Features = new RedisFeatureOptions { DistributedLocks = true }
        });
        var database = new DirectRedisDatabase(connection.GetDatabase());
        var keys = new RedisKeyBuilder(redisOptions);
        var firstLocks = CreateLockService(database, metrics);
        var secondLocks = CreateLockService(database, metrics);
        var storage = new PreviewObjectStorage();
        var converter = new BlockingPreviewConverter();
        var previewOptions = Options.Create(new LearningPreviewOptions
        {
            EnableOfficeConversion = true,
            ConversionTimeoutSeconds = 5,
            MaxConcurrentConversions = 2
        });
        using var limiter = new OfficeConversionLimiter(2);
        using var first = new LearningPreviewService(
            storage,
            new TestWebHostEnvironment(),
            converter,
            limiter,
            firstLocks,
            keys,
            previewOptions,
            redisOptions);
        using var second = new LearningPreviewService(
            storage,
            new TestWebHostEnvironment(),
            converter,
            limiter,
            secondLocks,
            keys,
            previewOptions,
            redisOptions);

        var firstPreview = first.PrepareAsync(
            157,
            151,
            PreviewObjectStorage.SourceReference,
            CancellationToken.None);
        await converter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondPreview = second.PrepareAsync(
            157,
            151,
            PreviewObjectStorage.SourceReference,
            CancellationToken.None);
        converter.Release.TrySetResult();

        var previews = await Task.WhenAll(firstPreview, secondPreview);

        Assert.Equal(1, converter.ConversionCount);
        Assert.Equal(previews[0].StorageReference, previews[1].StorageReference);
        Assert.Contains("/preview/", previews[0].StorageReference, StringComparison.Ordinal);
    }

    private static ServiceProvider CreateMetrics()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        return services.BuildServiceProvider();
    }

    private static DistributedLockService CreateLockService(
        IRedisDatabase database,
        ServiceProvider services) =>
        new(
            database,
            new RedisMetrics(
                services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            NullLogger<DistributedLockService>.Instance);

    private static string? RedisTestConnection()
    {
        var connection =
            Environment.GetEnvironmentVariable("CLUBHUB_REDIS_TEST_CONNECTION");
        if (string.Equals(
                Environment.GetEnvironmentVariable("CI"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(connection),
                "CI must provide CLUBHUB_REDIS_TEST_CONNECTION for Redis integration tests.");
        }

        return connection;
    }

    private sealed class DirectRedisDatabase(IDatabase database) : IRedisDatabase
    {
        public Task<RedisValue> StringGetAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            database.StringGetAsync(key).WaitAsync(cancellationToken);

        public Task<bool> StringSetAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            database.StringSetAsync(key, value, expiration).WaitAsync(cancellationToken);

        public Task<bool> StringSetIfNotExistsAsync(
            RedisKey key,
            RedisValue value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default) =>
            database.StringSetAsync(key, value, expiration, When.NotExists)
                .WaitAsync(cancellationToken);

        public Task<bool> KeyDeleteAsync(
            RedisKey key,
            CancellationToken cancellationToken = default) =>
            database.KeyDeleteAsync(key).WaitAsync(cancellationToken);

        public async Task<bool> KeyDeleteIfValueMatchesAsync(
            RedisKey key,
            RedisValue expectedValue,
            CancellationToken cancellationToken = default)
        {
            const string script =
                "if redis.call('get', KEYS[1]) == ARGV[1] then " +
                "return redis.call('del', KEYS[1]) else return 0 end";
            var result = await database.ScriptEvaluateAsync(script, [key], [expectedValue])
                .WaitAsync(cancellationToken);
            return (long)result == 1;
        }

        public Task<RedisResult> ScriptEvaluateAsync(
            string script,
            RedisKey[] keys,
            RedisValue[] values,
            CancellationToken cancellationToken = default) =>
            database.ScriptEvaluateAsync(script, keys, values).WaitAsync(cancellationToken);

        public Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default) =>
            database.PingAsync().WaitAsync(cancellationToken);
    }

    private sealed class PreviewObjectStorage : ILearningObjectStorage
    {
        public const string SourceReference =
            "clubs/151/learning/157/source.docx";
        private readonly ConcurrentDictionary<string, byte[]> _objects = new()
        {
            [SourceReference] = [0x50, 0x4b, 0x03, 0x04]
        };

        public bool IsStorageReference(string? value) =>
            value?.StartsWith("clubs/", StringComparison.Ordinal) == true;

        public Task<StoredObjectMetadata> GetMetadataAsync(
            string storageReference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = _objects[storageReference];
            return Task.FromResult(new StoredObjectMetadata(
                bytes.LongLength,
                storageReference.EndsWith(".pdf", StringComparison.Ordinal)
                    ? "application/pdf"
                    : "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                null,
                storageReference == SourceReference ? "source-etag" : "preview-etag",
                new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero)));
        }

        public Task<bool> ExistsAsync(
            string storageReference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_objects.ContainsKey(storageReference));
        }

        public Task<IReadOnlyList<string>> ListByPrefixAsync(
            string storagePrefix,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>(
                _objects.Keys
                    .Where(key => key.StartsWith(storagePrefix, StringComparison.Ordinal))
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToArray());
        }

        public Task<StoredObjectDownload> OpenReadAsync(
            string storageReference,
            StoredObjectRange? range,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = _objects[storageReference];
            if (range is not null)
            {
                bytes = bytes[
                    checked((int)range.Start)..checked((int)(range.End + 1))];
            }
            return Task.FromResult(
                new StoredObjectDownload(new MemoryStream(bytes, writable: false)));
        }

        public async Task SaveAsync(
            string storageReference,
            Stream content,
            long contentLength,
            string contentType,
            string contentDisposition,
            CancellationToken cancellationToken)
        {
            await using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            _objects[storageReference] = copy.ToArray();
        }

        public Task<string> UploadAsync(
            int clubId,
            int itemId,
            string extension,
            Stream content,
            long contentLength,
            string? contentType,
            string originalFileName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            string storageReference,
            CancellationToken cancellationToken)
        {
            _objects.TryRemove(storageReference, out _);
            return Task.CompletedTask;
        }

        public Task RemoveManyAsync(
            IReadOnlyCollection<string> storageReferences,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var storageReference in storageReferences)
            {
                _objects.TryRemove(storageReference, out _);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockingPreviewConverter : IOfficePreviewConverter
    {
        private int _conversionCount;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConversionCount => Volatile.Read(ref _conversionCount);

        public async Task<OfficePreviewArtifact> ConvertAsync(
            Stream source,
            long contentLength,
            string extension,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _conversionCount);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"clubhub-preview-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var pdfPath = Path.Combine(directory, "source.pdf");
            await File.WriteAllBytesAsync(pdfPath, "%PDF-1.7"u8.ToArray(), cancellationToken);
            return new OfficePreviewArtifact(directory, pdfPath);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "ClubHub.Api.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
