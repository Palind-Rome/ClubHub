using System.Diagnostics;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Redis;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubHub.Api.Tests;

public class LearningPreviewTests
{
    [Theory]
    [InlineData(".pdf", "%PDF-1.7", LearningPreviewKind.Pdf, "application/pdf")]
    [InlineData(".gif", "GIF89a", LearningPreviewKind.Image, "image/gif")]
    public void Detect_recognizes_safe_native_formats(
        string extension,
        string signature,
        LearningPreviewKind expectedKind,
        string expectedContentType)
    {
        var format = LearningPreviewFormatDetector.Detect(
            extension,
            System.Text.Encoding.ASCII.GetBytes(signature));

        Assert.Equal(expectedKind, format.Kind);
        Assert.Equal(expectedContentType, format.ContentType);
        Assert.False(format.RequiresOfficeConversion);
    }

    [Fact]
    public void Detect_requires_office_conversion_for_docx_zip_signature()
    {
        var format = LearningPreviewFormatDetector.Detect(
            ".docx",
            new byte[] { 0x50, 0x4b, 0x03, 0x04 });

        Assert.Equal(LearningPreviewKind.Pdf, format.Kind);
        Assert.True(format.RequiresOfficeConversion);
    }

    [Fact]
    public void Detect_rejects_extension_and_content_mismatch()
    {
        var exception = Assert.Throws<LearningPreviewException>(() =>
            LearningPreviewFormatDetector.Detect(".pdf", "not a pdf"u8));

        Assert.Equal(LearningPreviewFailure.Unsupported, exception.Failure);
        Assert.Contains("扩展名不一致", exception.Message);
    }

    [Fact]
    public void BusyFailureRemainsTheLastEnumMember()
    {
        var failures = Enum.GetValues<LearningPreviewFailure>();

        Assert.Equal(LearningPreviewFailure.Busy, failures[^1]);
        Assert.NotEqual(LearningPreviewFailure.Busy, default);
    }

    [Fact]
    public async Task Converter_rejects_invalid_openxml_before_starting_office_process()
    {
        var converter = new OfficePreviewConverter(Options.Create(new LearningPreviewOptions
        {
            OfficeExecutablePath = "missing-soffice"
        }));
        await using var source = new MemoryStream(new byte[] { 0x50, 0x4b, 0x03, 0x04, 0x00 });

        var exception = await Assert.ThrowsAsync<LearningPreviewException>(() =>
            converter.ConvertAsync(source, source.Length, ".docx", CancellationToken.None));

        Assert.Equal(LearningPreviewFailure.Unsupported, exception.Failure);
        Assert.Contains("有效的 Word", exception.Message);
    }

    [Theory]
    [InlineData("bytes=0-99", 1000, 0, 99)]
    [InlineData("bytes=900-", 1000, 900, 999)]
    [InlineData("bytes=-100", 1000, 900, 999)]
    [InlineData("bytes=900-2000", 1000, 900, 999)]
    public void ParseRange_supports_single_http_byte_ranges(
        string value,
        long length,
        long expectedStart,
        long expectedEnd)
    {
        var range = LearningPreviewService.ParseRange(value, length);

        Assert.NotNull(range);
        Assert.Equal(expectedStart, range.Start);
        Assert.Equal(expectedEnd, range.End);
    }

    [Theory]
    [InlineData("bytes=1000-")]
    [InlineData("bytes=100-50")]
    [InlineData("bytes=0-10,20-30")]
    public void ParseRange_rejects_invalid_or_multiple_ranges(string value)
    {
        var exception = Assert.Throws<LearningPreviewException>(() =>
            LearningPreviewService.ParseRange(value, 1000));

        Assert.Equal(LearningPreviewFailure.InvalidRange, exception.Failure);
    }

    [Theory]
    [InlineData(true, true, false, false, false, true)]
    [InlineData(true, false, true, false, false, true)]
    [InlineData(true, false, false, true, false, true)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, true, true, true, true, false)]
    public void AccessPolicy_requires_visibility_and_non_public_management(
        bool visible,
        bool published,
        bool manage,
        bool review,
        bool delete,
        bool expected)
    {
        Assert.Equal(
            expected,
            LearningPreviewAccessPolicy.CanPreview(visible, published, manage, review, delete));
    }

    [Fact]
    public void HttpPolicy_sets_inline_and_anti_sniffing_headers()
    {
        var context = new DefaultHttpContext();

        LearningPreviewHttpPolicy.Apply(context.Response, "application/pdf", "培训资料.pdf");

        Assert.StartsWith("inline;", context.Response.Headers.ContentDisposition.ToString());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("bytes", context.Response.Headers.AcceptRanges);
        Assert.Contains("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Resource-Policy"]);
    }

    [Fact]
    public void Preview_token_is_item_scoped_and_cannot_be_used_as_login_token()
    {
        var service = CreateTokenService();
        var token = service.CreatePreviewToken(21, 144);

        Assert.Equal(TimeSpan.FromMinutes(30), service.PreviewSessionLifetime);
        Assert.True(service.TryValidatePreviewToken(token, 144, out var principal));
        Assert.Equal(21, principal.UserId);
        Assert.False(service.TryValidatePreviewToken(token, 145, out _));
        Assert.False(service.TryValidateToken(token, out _));
    }

    [Fact]
    public void Login_token_cannot_be_used_as_preview_token()
    {
        var service = CreateTokenService();
        var token = service.CreateToken(new User { UserId = 21, Username = "student" });

        Assert.False(service.TryValidatePreviewToken(token, 144, out _));
    }

    [Fact]
    public void Preview_session_store_reuses_metadata_only_for_matching_token_user_and_item()
    {
        using var store = new LearningPreviewSessionStore();
        var preview = new PreparedLearningPreview(
            LearningPreviewKind.Pdf,
            "application/pdf",
            1024,
            "clubs/7/learning/144/preview.pdf",
            null,
            false);

        store.Store("signed-preview-token", 21, 144, preview, TimeSpan.FromMinutes(30));

        Assert.True(store.TryGet("signed-preview-token", 21, 144, out var stored));
        Assert.Same(preview, stored);
        Assert.False(store.TryGet("different-token", 21, 144, out _));
        Assert.False(store.TryGet("signed-preview-token", 22, 144, out _));
        Assert.False(store.TryGet("signed-preview-token", 21, 145, out _));
    }

    [Fact]
    public async Task Conversion_limiter_never_exceeds_configured_concurrency()
    {
        using var limiter = new OfficeConversionLimiter(2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = new object();
        var active = 0;
        var maximumActive = 0;
        var started = 0;

        var operations = Enumerable.Range(0, 6).Select(_ => limiter.RunAsync(
            async cancellationToken =>
            {
                lock (sync)
                {
                    active += 1;
                    maximumActive = Math.Max(maximumActive, active);
                    started += 1;
                    if (started == limiter.MaxConcurrency) firstWaveStarted.TrySetResult();
                }

                await release.Task.WaitAsync(cancellationToken);
                lock (sync)
                {
                    active -= 1;
                }
                return true;
            },
            CancellationToken.None)).ToArray();

        await firstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (sync)
        {
            Assert.Equal(limiter.MaxConcurrency, active);
        }

        release.TrySetResult();
        await Task.WhenAll(operations);
        Assert.Equal(limiter.MaxConcurrency, maximumActive);
    }

    [Fact]
    public async Task OfficeOperationTimeoutIsDistinctFromConversionFailure()
    {
        var storage = new InMemoryPreviewStorage();
        var previewOptions = Options.Create(new LearningPreviewOptions
        {
            EnableOfficeConversion = true,
            ConversionTimeoutSeconds = -29,
            MaxConcurrentConversions = 1
        });
        var redisOptions = Options.Create(new RedisOptions
        {
            Enabled = false,
            EnvironmentPrefix = "test"
        });
        using var limiter = new OfficeConversionLimiter(previewOptions);
        using var service = new LearningPreviewService(
            storage,
            new TestHostEnvironment(),
            new TimeoutPreviewConverter(),
            limiter,
            new UnexpectedDistributedLockService(),
            new RedisKeyBuilder(redisOptions),
            previewOptions,
            redisOptions);

        var exception = await Assert.ThrowsAsync<LearningPreviewException>(() =>
            service.PrepareAsync(
                144,
                7,
                InMemoryPreviewStorage.SourceReference,
                CancellationToken.None));

        Assert.Equal(LearningPreviewFailure.Timeout, exception.Failure);
    }

    [Fact]
    public async Task RemovePreviewDeletesEveryStoredVersionAndLegacyObject()
    {
        var storage = new InMemoryPreviewStorage();
        storage.Add("clubs/7/learning/144/preview/old-version.pdf", "%PDF-old"u8.ToArray());
        storage.Add("clubs/7/learning/144/preview/new-version.pdf", "%PDF-new"u8.ToArray());
        storage.Add("clubs/7/learning/144/preview/converted.pdf", "%PDF-legacy"u8.ToArray());
        storage.Add("clubs/7/learning/145/preview/other.pdf", "%PDF-other"u8.ToArray());
        var previewOptions = Options.Create(new LearningPreviewOptions());
        var redisOptions = Options.Create(new RedisOptions
        {
            Enabled = false,
            EnvironmentPrefix = "test"
        });
        using var limiter = new OfficeConversionLimiter(previewOptions);
        using var service = new LearningPreviewService(
            storage,
            new TestHostEnvironment(),
            new UnexpectedPreviewConverter(),
            limiter,
            new UnexpectedDistributedLockService(),
            new RedisKeyBuilder(redisOptions),
            previewOptions,
            redisOptions);

        await service.RemovePreviewAsync(
            144,
            7,
            InMemoryPreviewStorage.SourceReference,
            CancellationToken.None);

        Assert.DoesNotContain(
            storage.Keys,
            key => key.StartsWith(
                "clubs/7/learning/144/preview/",
                StringComparison.Ordinal));
        Assert.Contains(InMemoryPreviewStorage.SourceReference, storage.Keys);
        Assert.Contains("clubs/7/learning/145/preview/other.pdf", storage.Keys);
    }

    [Fact]
    public async Task PublishLocalPreviewKeepsExistingDestinationAndPropagatesOtherIoFailures()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"clubhub-preview-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, "preview.tmp");
        var previewPath = Path.Combine(directory, "preview.pdf");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, "new"u8.ToArray());
            await File.WriteAllBytesAsync(previewPath, "existing"u8.ToArray());

            LearningPreviewService.PublishLocalPreview(temporaryPath, previewPath);

            Assert.Equal("existing"u8.ToArray(), await File.ReadAllBytesAsync(previewPath));
            Assert.True(File.Exists(temporaryPath));
            File.Delete(temporaryPath);
            Assert.ThrowsAny<IOException>(() =>
                LearningPreviewService.PublishLocalPreview(temporaryPath, previewPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AuthTokenService CreateTokenService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:TokenSigningKey"] = "test-signing-key-with-sufficient-entropy",
                ["LearningPreview:SessionLifetimeMinutes"] = "30"
            })
            .Build();
        return new AuthTokenService(configuration, new TestHostEnvironment());
    }

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ClubHub.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class InMemoryPreviewStorage : ILearningObjectStorage
    {
        public const string SourceReference = "clubs/7/learning/144/source.docx";
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal)
        {
            [SourceReference] = [0x50, 0x4b, 0x03, 0x04]
        };

        public IReadOnlyCollection<string> Keys => _objects.Keys;

        public void Add(string storageReference, byte[] content) =>
            _objects[storageReference] = content;

        public bool IsStorageReference(string? value) =>
            value?.StartsWith("clubs/", StringComparison.Ordinal) == true;

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

        public Task<StoredObjectMetadata> GetMetadataAsync(
            string storageReference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = _objects[storageReference];
            return Task.FromResult(new StoredObjectMetadata(
                content.LongLength,
                storageReference.EndsWith(".pdf", StringComparison.Ordinal)
                    ? "application/pdf"
                    : "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                null,
                "etag",
                DateTimeOffset.UnixEpoch));
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
            var content = _objects[storageReference];
            return Task.FromResult(
                new StoredObjectDownload(new MemoryStream(content, writable: false)));
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

        public Task RemoveAsync(
            string storageReference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _objects.Remove(storageReference);
            return Task.CompletedTask;
        }

        public Task RemoveManyAsync(
            IReadOnlyCollection<string> storageReferences,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var storageReference in storageReferences)
            {
                _objects.Remove(storageReference);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TimeoutPreviewConverter : IOfficePreviewConverter
    {
        public async Task<OfficePreviewArtifact> ConvertAsync(
            Stream source,
            long contentLength,
            string extension,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
    }

    private sealed class UnexpectedPreviewConverter : IOfficePreviewConverter
    {
        public Task<OfficePreviewArtifact> ConvertAsync(
            Stream source,
            long contentLength,
            string extension,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The converter should not be called.");
    }

    private sealed class UnexpectedDistributedLockService : IDistributedLockService
    {
        public Task<IDistributedLockHandle?> TryAcquireAsync(
            RedisKey resource,
            DistributedLockPolicy policy,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Distributed locking should be disabled.");

        public Task<IDistributedLockHandle?> TryAcquireAsync(
            IReadOnlyCollection<RedisKey> resources,
            DistributedLockPolicy policy,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Distributed locking should be disabled.");
    }
}
