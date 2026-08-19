using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClubHub.Api.Tests;

public sealed class LearningUploadCompensationTests
{
    [Fact]
    public async Task UploadResource_WhenStorageFails_RemovesPendingDatabaseRecord()
    {
        var objectStorage = new FailingLearningObjectStorage();
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"LearningUploadCompensation-{Guid.NewGuid():N}";
        await using var baseFactory = new ClubHubWebApplicationFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ClubHubDbContext>();
                services.RemoveAll<DbContextOptions<ClubHubDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ClubHubDbContext>>();
                services.RemoveAll<IDatabaseProvider>();
                services.AddDbContext<ClubHubDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName, databaseRoot));
                services.RemoveAll<ILearningObjectStorage>();
                services.AddSingleton<ILearningObjectStorage>(objectStorage);
            }));

        const int userId = 910001;
        const int clubId = 920001;
        await SeedAuthorizedUploaderAsync(factory.Services, userId, clubId);

        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider
            .GetRequiredService<AuthTokenService>()
            .CreateToken(new User { UserId = userId, Username = "upload-review-test" });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(clubId.ToString()), "clubId" },
            { new StringContent("review upload"), "title" },
            { new StringContent("club"), "visibility" },
            { new StringContent("allow"), "downloadPermission" }
        };
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes("test content"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "review.txt");

        using var response = await client.PostAsync("/api/learning/resources/upload", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(objectStorage.UploadItemId > 0);

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        Assert.Empty(await db.LearningItems.AsNoTracking().ToListAsync());
    }

    private static async Task SeedAuthorizedUploaderAsync(
        IServiceProvider services,
        int userId,
        int clubId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var now = DateTime.UtcNow;
        var role = new Role
        {
            RoleId = 930001,
            RoleCode = "CLUB_OFFICER",
            RoleName = "社团干部",
            RoleScope = "club",
            CreatedAt = now
        };
        db.AddRange(
            new User
            {
                UserId = userId,
                Username = "upload-review-test",
                PasswordHash = "not-used",
                RealName = "上传评审测试",
                AccountStatus = "normal",
                CreatedAt = now
            },
            new Club
            {
                ClubId = clubId,
                ClubName = "上传评审测试社团",
                ClubStatus = "normal",
                CreatedAt = now
            },
            role,
            new UserRole
            {
                UserRoleId = 940001,
                UserId = userId,
                RoleId = role.RoleId,
                ClubId = clubId,
                AssignedAt = now
            });
        await db.SaveChangesAsync();
    }

    private sealed class FailingLearningObjectStorage : ILearningObjectStorage
    {
        public int UploadItemId { get; private set; }

        public bool IsStorageReference(string? value) => false;

        public Task<string> UploadAsync(
            int clubId,
            int itemId,
            string extension,
            Stream content,
            long contentLength,
            string? contentType,
            string originalFileName,
            CancellationToken cancellationToken)
        {
            UploadItemId = itemId;
            throw new LearningObjectStorageException("Expected test failure.");
        }

        public Task<StoredObjectMetadata> GetMetadataAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredObjectDownload> OpenReadAsync(
            string storageReference,
            StoredObjectRange? range,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            string storageReference,
            Stream content,
            long contentLength,
            string contentType,
            string contentDisposition,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
