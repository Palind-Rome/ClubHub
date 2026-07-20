using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace ClubHub.Api.Services;

public sealed class LearningPreviewService : IDisposable
{
    private const string LocalFileUrlPrefix = "/api/learning/items/";
    private const int SignatureBytes = 512;
    private const int ConversionLockStripeCount = 64;

    private readonly ILearningObjectStorage _objectStorage;
    private readonly IWebHostEnvironment _environment;
    private readonly OfficePreviewConverter _converter;
    private readonly OfficeConversionLimiter _conversionLimiter;
    private readonly bool _officeConversionEnabled;
    private readonly SemaphoreSlim[] _conversionLocks = Enumerable.Range(0, ConversionLockStripeCount)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public LearningPreviewService(
        ILearningObjectStorage objectStorage,
        IWebHostEnvironment environment,
        OfficePreviewConverter converter,
        OfficeConversionLimiter conversionLimiter,
        IOptions<LearningPreviewOptions> options)
    {
        _objectStorage = objectStorage;
        _environment = environment;
        _converter = converter;
        _conversionLimiter = conversionLimiter;
        _officeConversionEnabled = environment.IsDevelopment() && options.Value.EnableOfficeConversion;
    }

    public async Task<PreparedLearningPreview> PrepareAsync(
        int itemId,
        int clubId,
        string? fileUrl,
        CancellationToken cancellationToken)
    {
        var source = await ResolveSourceAsync(itemId, clubId, fileUrl, cancellationToken);
        if (!source.Format.RequiresOfficeConversion)
        {
            return new PreparedLearningPreview(
                source.Format.Kind,
                source.Format.ContentType,
                source.Length,
                source.StorageReference,
                source.PhysicalPath,
                false);
        }

        if (!_officeConversionEnabled)
        {
            throw new LearningPreviewException(
                LearningPreviewFailure.Unsupported,
                "Office 在线转换在独立隔离服务完成前暂不可用，请在获得权限后下载查看。");
        }

        return source.StorageReference is not null
            ? await PrepareStoredOfficePreviewAsync(source, itemId, clubId, cancellationToken)
            : await PrepareLocalOfficePreviewAsync(source, itemId, clubId, cancellationToken);
    }

    public async Task<LearningPreviewStream> OpenAsync(
        PreparedLearningPreview preview,
        string? rangeHeader,
        CancellationToken cancellationToken)
    {
        if (preview.PhysicalPath is not null)
        {
            return new LearningPreviewStream(null, preview.PhysicalPath, null, preview.Length);
        }

        if (preview.StorageReference is null)
        {
            throw new LearningPreviewException(
                LearningPreviewFailure.NotFound,
                "预览文件不存在。");
        }

        var range = ParseRange(rangeHeader, preview.Length);
        var storedObject = await _objectStorage.OpenReadAsync(
            preview.StorageReference,
            range is null ? null : new StoredObjectRange(range.Start, range.End),
            cancellationToken);
        return new LearningPreviewStream(
            storedObject.Content,
            null,
            range,
            range?.Length ?? preview.Length);
    }

    public async Task RemovePreviewAsync(
        int itemId,
        int clubId,
        string? fileUrl,
        CancellationToken cancellationToken)
    {
        if (_objectStorage.IsStorageReference(fileUrl))
        {
            var previewReference = BuildPreviewReference(clubId, itemId);
            if (await _objectStorage.ExistsAsync(previewReference, cancellationToken))
            {
                await _objectStorage.RemoveAsync(previewReference, cancellationToken);
            }
        }

        var localPath = GetLocalPreviewPath(clubId, itemId);
        if (File.Exists(localPath)) File.Delete(localPath);
    }

    internal static PreviewByteRange? ParseRange(string? rangeHeader, long contentLength)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader)) return null;
        if (contentLength <= 0 || !RangeHeaderValue.TryParse(rangeHeader, out var parsed) ||
            !string.Equals(parsed.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
            parsed.Ranges.Count != 1)
        {
            throw InvalidRange(contentLength);
        }

        var requested = parsed.Ranges.Single();
        long start;
        long end;
        if (requested.From.HasValue)
        {
            start = requested.From.Value;
            if (start < 0 || start >= contentLength) throw InvalidRange(contentLength);
            end = requested.To.HasValue
                ? Math.Min(requested.To.Value, contentLength - 1)
                : contentLength - 1;
            if (end < start) throw InvalidRange(contentLength);
        }
        else if (requested.To.HasValue && requested.To.Value > 0)
        {
            var suffixLength = Math.Min(requested.To.Value, contentLength);
            start = contentLength - suffixLength;
            end = contentLength - 1;
        }
        else
        {
            throw InvalidRange(contentLength);
        }

        return new PreviewByteRange(start, end, contentLength);
    }

    private async Task<PreviewSource> ResolveSourceAsync(
        int itemId,
        int clubId,
        string? fileUrl,
        CancellationToken cancellationToken)
    {
        if (_objectStorage.IsStorageReference(fileUrl))
        {
            var metadata = await _objectStorage.GetMetadataAsync(fileUrl!, cancellationToken);
            if (metadata.ContentLength is null or <= 0)
            {
                throw new LearningPreviewException(
                    LearningPreviewFailure.NotFound,
                    "资源文件为空或不存在。");
            }

            var signature = await ReadStoredSignatureAsync(
                fileUrl!,
                metadata.ContentLength.Value,
                cancellationToken);
            var format = LearningPreviewFormatDetector.Detect(Path.GetExtension(fileUrl), signature);
            return new PreviewSource(
                format,
                metadata.ContentLength.Value,
                fileUrl,
                null,
                metadata.LastModified);
        }

        if (fileUrl == $"{LocalFileUrlPrefix}{itemId}/file")
        {
            var clubStoragePath = Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "learning-files",
                clubId.ToString(CultureInfo.InvariantCulture));
            var storedPath = Directory.Exists(clubStoragePath)
                ? Directory.EnumerateFiles(clubStoragePath, $"{itemId}.*").FirstOrDefault()
                : null;
            if (storedPath is null)
            {
                throw new LearningPreviewException(
                    LearningPreviewFailure.NotFound,
                    "上传文件不存在。");
            }

            var info = new FileInfo(storedPath);
            var signature = await ReadLocalSignatureAsync(storedPath, cancellationToken);
            var format = LearningPreviewFormatDetector.Detect(info.Extension, signature);
            return new PreviewSource(format, info.Length, null, storedPath, info.LastWriteTimeUtc);
        }

        throw new LearningPreviewException(
            LearningPreviewFailure.Unsupported,
            "当前资源不是受保护的上传文件，暂不支持在线预览。");
    }

    private async Task<PreparedLearningPreview> PrepareStoredOfficePreviewAsync(
        PreviewSource source,
        int itemId,
        int clubId,
        CancellationToken cancellationToken)
    {
        var previewReference = BuildPreviewReference(clubId, itemId);
        var gate = GetConversionLock(previewReference);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!await _objectStorage.ExistsAsync(previewReference, cancellationToken))
            {
                var storedObject = await _objectStorage.OpenReadAsync(
                    source.StorageReference!,
                    null,
                    cancellationToken);
                await using var sourceStream = storedObject.Content;
                await using var artifact = await ConvertWithLimitAsync(
                    sourceStream,
                    source.Length,
                    source.Format.Extension,
                    cancellationToken);
                await using var pdf = new FileStream(
                    artifact.PdfPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await _objectStorage.SaveAsync(
                    previewReference,
                    pdf,
                    pdf.Length,
                    "application/pdf",
                    "inline; filename=preview.pdf",
                    cancellationToken);
            }

            var metadata = await _objectStorage.GetMetadataAsync(previewReference, cancellationToken);
            if (metadata.ContentLength is null or <= 0)
            {
                throw new LearningPreviewException(
                    LearningPreviewFailure.ConversionFailed,
                    "Office 文档转换后的预览文件为空。");
            }

            return new PreparedLearningPreview(
                LearningPreviewKind.Pdf,
                "application/pdf",
                metadata.ContentLength.Value,
                previewReference,
                null,
                true);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PreparedLearningPreview> PrepareLocalOfficePreviewAsync(
        PreviewSource source,
        int itemId,
        int clubId,
        CancellationToken cancellationToken)
    {
        var previewPath = GetLocalPreviewPath(clubId, itemId);
        var gate = GetConversionLock(previewPath);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(previewPath) ||
                File.GetLastWriteTimeUtc(previewPath) < source.LastModified?.UtcDateTime)
            {
                await using var sourceStream = new FileStream(
                    source.PhysicalPath!,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var artifact = await ConvertWithLimitAsync(
                    sourceStream,
                    source.Length,
                    source.Format.Extension,
                    cancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
                File.Copy(artifact.PdfPath, previewPath, true);
            }

            var info = new FileInfo(previewPath);
            return new PreparedLearningPreview(
                LearningPreviewKind.Pdf,
                "application/pdf",
                info.Length,
                null,
                previewPath,
                true);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<byte[]> ReadStoredSignatureAsync(
        string storageReference,
        long contentLength,
        CancellationToken cancellationToken)
    {
        var end = Math.Min(contentLength, SignatureBytes) - 1;
        var storedObject = await _objectStorage.OpenReadAsync(
            storageReference,
            new StoredObjectRange(0, end),
            cancellationToken);
        await using var content = storedObject.Content;
        return await ReadSignatureAsync(content, cancellationToken);
    }

    private static async Task<byte[]> ReadLocalSignatureAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var content = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            SignatureBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadSignatureAsync(content, cancellationToken);
    }

    private static async Task<byte[]> ReadSignatureAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[SignatureBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await content.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return buffer[..total];
    }

    private string GetLocalPreviewPath(int clubId, int itemId) => Path.Combine(
        _environment.ContentRootPath,
        "App_Data",
        "learning-previews",
        clubId.ToString(CultureInfo.InvariantCulture),
        $"{itemId}.pdf");

    private static string BuildPreviewReference(int clubId, int itemId) =>
        $"clubs/{clubId}/learning/{itemId}/preview/converted.pdf";

    private SemaphoreSlim GetConversionLock(string key)
    {
        var index = (key.GetHashCode(StringComparison.Ordinal) & int.MaxValue) % _conversionLocks.Length;
        return _conversionLocks[index];
    }

    private Task<OfficePreviewArtifact> ConvertWithLimitAsync(
        Stream source,
        long sourceLength,
        string extension,
        CancellationToken cancellationToken) =>
        _conversionLimiter.RunAsync(
            token => _converter.ConvertAsync(source, sourceLength, extension, token),
            cancellationToken);

    /// <summary>
    /// 释放固定数量的转换互斥锁。
    /// </summary>
    public void Dispose()
    {
        foreach (var conversionLock in _conversionLocks)
        {
            conversionLock.Dispose();
        }
    }

    private static LearningPreviewException InvalidRange(long contentLength) => new(
        LearningPreviewFailure.InvalidRange,
        $"请求的文件范围无效，资源长度为 {contentLength} 字节。");

    private sealed record PreviewSource(
        LearningPreviewFormat Format,
        long Length,
        string? StorageReference,
        string? PhysicalPath,
        DateTimeOffset? LastModified);
}

internal static class LearningPreviewFormatDetector
{
    internal static LearningPreviewFormat Detect(string? extension, ReadOnlySpan<byte> signature)
    {
        var normalizedExtension = (extension ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedExtension == ".pdf" && signature.StartsWith("%PDF-"u8))
        {
            return new LearningPreviewFormat(LearningPreviewKind.Pdf, "application/pdf", ".pdf", false);
        }
        if (normalizedExtension is ".jpg" or ".jpeg" &&
            signature.Length >= 3 && signature[0] == 0xff && signature[1] == 0xd8 && signature[2] == 0xff)
        {
            return new LearningPreviewFormat(LearningPreviewKind.Image, "image/jpeg", normalizedExtension, false);
        }
        if (normalizedExtension == ".png" && signature.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
        {
            return new LearningPreviewFormat(LearningPreviewKind.Image, "image/png", ".png", false);
        }
        if (normalizedExtension == ".gif" &&
            (signature.StartsWith("GIF87a"u8) || signature.StartsWith("GIF89a"u8)))
        {
            return new LearningPreviewFormat(LearningPreviewKind.Image, "image/gif", ".gif", false);
        }
        if (normalizedExtension == ".webp" && signature.Length >= 12 &&
            signature[..4].SequenceEqual("RIFF"u8) && signature.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return new LearningPreviewFormat(LearningPreviewKind.Image, "image/webp", ".webp", false);
        }
        if (normalizedExtension == ".mp4" && signature.Length >= 12 &&
            signature.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return new LearningPreviewFormat(LearningPreviewKind.Video, "video/mp4", ".mp4", false);
        }
        if (normalizedExtension == ".webm" && signature.StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }))
        {
            return new LearningPreviewFormat(LearningPreviewKind.Video, "video/webm", ".webm", false);
        }
        if (normalizedExtension is ".doc" or ".ppt" &&
            signature.StartsWith(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }))
        {
            return new LearningPreviewFormat(LearningPreviewKind.Pdf, "application/pdf", normalizedExtension, true);
        }
        if (normalizedExtension is ".docx" or ".pptx" &&
            signature.Length >= 4 && signature[0] == 0x50 && signature[1] == 0x4b &&
            signature[2] is 0x03 or 0x05 or 0x07 && signature[3] is 0x04 or 0x06 or 0x08)
        {
            return new LearningPreviewFormat(LearningPreviewKind.Pdf, "application/pdf", normalizedExtension, true);
        }

        throw new LearningPreviewException(
            LearningPreviewFailure.Unsupported,
            "当前格式暂不支持在线预览，或文件内容与扩展名不一致。");
    }
}

public sealed class OfficePreviewConverter
{
    private readonly LearningPreviewOptions _options;

    public OfficePreviewConverter(IOptions<LearningPreviewOptions> options)
    {
        _options = options.Value;
    }

    public async Task<OfficePreviewArtifact> ConvertAsync(
        Stream source,
        long contentLength,
        string extension,
        CancellationToken cancellationToken)
    {
        if (contentLength <= 0 || contentLength > _options.MaxInputBytes)
        {
            throw new LearningPreviewException(
                LearningPreviewFailure.ConversionFailed,
                "Office 文件为空或超过允许转换的大小。");
        }

        var workingDirectory = Path.Combine(Path.GetTempPath(), $"clubhub-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var inputPath = Path.Combine(workingDirectory, $"source{extension}");
        var outputDirectory = Path.Combine(workingDirectory, "output");
        var profileDirectory = Path.Combine(workingDirectory, "profile");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(profileDirectory);
        try
        {
            await using (var input = new FileStream(
                inputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyBoundedAsync(source, input, _options.MaxInputBytes, cancellationToken);
            }

            ValidateOpenXmlPackage(inputPath, extension);

            var usePrLimit = OperatingSystem.IsLinux() &&
                _options.MaxWorkingSetBytes > 0 &&
                File.Exists("/usr/bin/prlimit");
            var startInfo = new ProcessStartInfo
            {
                FileName = usePrLimit ? "/usr/bin/prlimit" : _options.OfficeExecutablePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (usePrLimit)
            {
                startInfo.ArgumentList.Add($"--as={_options.MaxWorkingSetBytes}");
                startInfo.ArgumentList.Add("--");
                startInfo.ArgumentList.Add(_options.OfficeExecutablePath);
            }
            foreach (var argument in new[]
            {
                $"-env:UserInstallation={new Uri(profileDirectory).AbsoluteUri}",
                "--headless",
                "--nologo",
                "--nodefault",
                "--nolockcheck",
                "--nofirststartwizard",
                "--norestore",
                "--convert-to",
                "pdf",
                "--outdir",
                outputDirectory,
                inputPath
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start()) throw new InvalidOperationException("Office converter did not start.");
            }
            catch (Exception exception)
            {
                throw new LearningPreviewException(
                    LearningPreviewFailure.ConversionFailed,
                    "服务器未安装或无法启动 Office 预览转换工具。",
                    exception);
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ConversionTimeoutSeconds)));
            try
            {
                while (!process.HasExited)
                {
                    await Task.Delay(200, timeout.Token);
                    process.Refresh();
                    if (_options.MaxWorkingSetBytes > 0 && process.WorkingSet64 > _options.MaxWorkingSetBytes)
                    {
                        process.Kill(true);
                        throw new LearningPreviewException(
                            LearningPreviewFailure.ConversionFailed,
                            "Office 文档转换占用资源过高，已安全终止。");
                    }
                }
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited) process.Kill(true);
                throw new LearningPreviewException(
                    LearningPreviewFailure.ConversionFailed,
                    "Office 文档转换超时，请稍后重试或下载后查看。");
            }

            var output = await standardOutput;
            var error = await standardError;
            if (process.ExitCode != 0)
            {
                throw new LearningPreviewException(
                    LearningPreviewFailure.ConversionFailed,
                    $"Office 文档转换失败（退出码 {process.ExitCode}）。{TrimDiagnostic(error, output)}");
            }

            var pdfPath = Path.Combine(outputDirectory, "source.pdf");
            if (!File.Exists(pdfPath))
            {
                throw new LearningPreviewException(
                    LearningPreviewFailure.ConversionFailed,
                    "Office 文档转换失败，未生成 PDF 预览副本。");
            }

            var outputLength = new FileInfo(pdfPath).Length;
            if (outputLength <= 0 || outputLength > _options.MaxOutputBytes)
            {
                throw new LearningPreviewException(
                    LearningPreviewFailure.ConversionFailed,
                    "Office 文档转换结果为空或超过预览大小限制。");
            }

            return new OfficePreviewArtifact(workingDirectory, pdfPath);
        }
        catch
        {
            TryDeleteDirectory(workingDirectory);
            throw;
        }
    }

    private static async Task CopyBoundedAsync(
        Stream source,
        Stream destination,
        long limit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > limit)
            {
                throw new LearningPreviewException(
                    LearningPreviewFailure.ConversionFailed,
                    "Office 文件超过允许转换的大小。");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void ValidateOpenXmlPackage(string inputPath, string extension)
    {
        if (extension is not (".docx" or ".pptx")) return;
        try
        {
            using var archive = ZipFile.OpenRead(inputPath);
            if (archive.Entries.Count > 10_000)
            {
                throw new InvalidDataException("The Office package contains too many entries.");
            }

            var requiredPart = extension == ".docx"
                ? "word/document.xml"
                : "ppt/presentation.xml";
            if (archive.GetEntry("[Content_Types].xml") is null || archive.GetEntry(requiredPart) is null)
            {
                throw new InvalidDataException("The expected Office document part is missing.");
            }
        }
        catch (InvalidDataException exception)
        {
            throw new LearningPreviewException(
                LearningPreviewFailure.Unsupported,
                "文件内容不是有效的 Word 或 PowerPoint 文档，无法在线预览。",
                exception);
        }
    }

    private static string TrimDiagnostic(params string[] values)
    {
        var text = string.Join(" ", values)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return text.Length == 0 ? string.Empty : $" {text[..Math.Min(200, text.Length)]}";
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class OfficePreviewArtifact : IAsyncDisposable
{
    private readonly string _workingDirectory;

    public OfficePreviewArtifact(string workingDirectory, string pdfPath)
    {
        _workingDirectory = workingDirectory;
        PdfPath = pdfPath;
    }

    public string PdfPath { get; }

    public ValueTask DisposeAsync()
    {
        OfficePreviewConverter.TryDeleteDirectory(_workingDirectory);
        return ValueTask.CompletedTask;
    }
}

public enum LearningPreviewKind
{
    Image,
    Video,
    Pdf
}

public sealed record PreparedLearningPreview(
    LearningPreviewKind Kind,
    string ContentType,
    long Length,
    string? StorageReference,
    string? PhysicalPath,
    bool IsConverted);

public sealed record LearningPreviewStream(
    Stream? Content,
    string? PhysicalPath,
    PreviewByteRange? Range,
    long ContentLength);

public sealed record PreviewByteRange(long Start, long End, long TotalLength)
{
    public long Length => End - Start + 1;
}

internal sealed record LearningPreviewFormat(
    LearningPreviewKind Kind,
    string ContentType,
    string Extension,
    bool RequiresOfficeConversion);

public enum LearningPreviewFailure
{
    Unsupported,
    InvalidRange,
    ConversionFailed,
    NotFound
}

public sealed class LearningPreviewException : Exception
{
    public LearningPreviewException(
        LearningPreviewFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public LearningPreviewFailure Failure { get; }
}

internal static class LearningPreviewAccessPolicy
{
    internal static bool CanPreview(
        bool isVisible,
        bool isPublished,
        bool canManage,
        bool canReview,
        bool canDelete) =>
        isVisible && (isPublished || canManage || canReview || canDelete);
}

internal static class LearningPreviewHttpPolicy
{
    internal static void Apply(HttpResponse response, string contentType, string fileName)
    {
        var safeFileName = fileName.Replace('\r', '_').Replace('\n', '_');
        response.ContentType = contentType;
        response.Headers.ContentDisposition =
            $"inline; filename*=UTF-8''{Uri.EscapeDataString(safeFileName)}";
        response.Headers.AcceptRanges = "bytes";
        response.Headers.CacheControl = "private, no-store, max-age=0";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] =
            "sandbox; default-src 'none'; img-src 'self' data: blob:; media-src 'self' blob:";
    }
}
