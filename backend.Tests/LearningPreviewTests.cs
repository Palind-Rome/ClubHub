using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ClubHub.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
