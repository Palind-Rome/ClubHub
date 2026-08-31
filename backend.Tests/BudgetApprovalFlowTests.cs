using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClubHub.Api.Tests;

public sealed class BudgetApprovalFlowTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private const int ApplicantUserId = 101;
    private const int ReviewerUserId = 102;
    private const int ClubId = 201;
    private const int AccountId = 301;
    private const int ApplicationId = 401;
    private readonly ClubHubWebApplicationFactory _factory;

    public BudgetApprovalFlowTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetReviewRecords_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/v1/budget/applications/{ApplicationId}/reviews");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResubmitEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent(
            """{"title":"test"}""",
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(
            $"/api/v1/budget/applications/{ApplicationId}/resubmit",
            content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetReviewRecords_ApplicationOwnerWithoutBudgetView_ReturnsHistory()
    {
        await using var factory = await RelationalBudgetWebApplicationFactory.CreateAsync();
        using var client = await SeedScenarioAsync(
            factory,
            applicationStatus: "rejected",
            grantApplicantReviewPermission: false,
            includeReviewRecord: true);

        using var response = await client.GetAsync(
            $"/api/v1/budget/applications/{ApplicationId}/reviews");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var record = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(ReviewerUserId, record.GetProperty("reviewerUserId").GetInt32());
        Assert.False(record.GetProperty("approved").GetBoolean());
        Assert.Equal("请补充预算明细", record.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task ReviewApplication_ByApplicantWithReviewPermission_ReturnsForbidden()
    {
        await using var factory = await RelationalBudgetWebApplicationFactory.CreateAsync();
        using var client = await SeedScenarioAsync(
            factory,
            applicationStatus: "pending",
            grantApplicantReviewPermission: true);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/budget/applications/{ApplicationId}/review",
            new { approved = true, comment = "同意" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FORBIDDEN", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "申请人不能审批自己的经费申请。",
            document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ResubmitApplication_WhenRejected_UpdatesFieldsAndReturnsPending()
    {
        await using var factory = await RelationalBudgetWebApplicationFactory.CreateAsync();
        using var client = await SeedScenarioAsync(
            factory,
            applicationStatus: "rejected",
            grantApplicantReviewPermission: false,
            includeReviewRecord: true);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/budget/applications/{ApplicationId}/resubmit",
            new
            {
                type = "purchase",
                title = "修订后的采购申请",
                amount = 800,
                purpose = "采购活动物料",
                detail = "横幅与打印材料"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("pending", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("purchase", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("修订后的采购申请", document.RootElement.GetProperty("title").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var application = await db.BudgetApplications.SingleAsync(
            item => item.ApplicationId == ApplicationId);
        Assert.Equal("pending", application.ApplicationStatus);
        Assert.Null(application.ReviewerUserId);
        Assert.Null(application.ReviewComment);
        Assert.Null(application.ReviewedAt);
        Assert.Equal(
            1,
            await db.BudgetReviewRecords.CountAsync(
                item => item.ApplicationId == ApplicationId));
    }

    [Fact]
    public async Task ResubmitApplication_WhenTypeIsOmitted_PreservesExistingType()
    {
        await using var factory = await RelationalBudgetWebApplicationFactory.CreateAsync();
        using var client = await SeedScenarioAsync(
            factory,
            applicationStatus: "rejected",
            grantApplicantReviewPermission: false);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/budget/applications/{ApplicationId}/resubmit",
            new { title = "保留原申请类型" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("activity_budget", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ResubmitApplication_WhenTypeIsNull_PreservesExistingType()
    {
        await using var factory = await RelationalBudgetWebApplicationFactory.CreateAsync();
        using var client = await SeedScenarioAsync(
            factory,
            applicationStatus: "rejected",
            grantApplicantReviewPermission: false);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/budget/applications/{ApplicationId}/resubmit",
            new { type = (string?)null, title = "接受可空申请类型" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("activity_budget", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ResubmitApplication_WhenNotRejected_ReturnsBadRequest()
    {
        await using var factory = await RelationalBudgetWebApplicationFactory.CreateAsync();
        using var client = await SeedScenarioAsync(
            factory,
            applicationStatus: "pending",
            grantApplicantReviewPermission: false);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/budget/applications/{ApplicationId}/resubmit",
            new { title = "不应被接受" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "只有已驳回的经费申请才能重新提交。",
            document.RootElement.GetProperty("message").GetString());
    }

    private static async Task<HttpClient> SeedScenarioAsync(
        RelationalBudgetWebApplicationFactory factory,
        string applicationStatus,
        bool grantApplicantReviewPermission,
        bool includeReviewRecord = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var now = DateTime.UtcNow;
        var applicant = new User
        {
            UserId = ApplicantUserId,
            Username = "budget-applicant",
            PasswordHash = "unused",
            RealName = "申请人",
            AccountStatus = "normal",
            CreatedAt = now
        };
        var reviewer = new User
        {
            UserId = ReviewerUserId,
            Username = "budget-reviewer",
            PasswordHash = "unused",
            RealName = "审核人",
            AccountStatus = "normal",
            CreatedAt = now
        };

        db.Users.AddRange(applicant, reviewer);
        db.Clubs.Add(new Club
        {
            ClubId = ClubId,
            ClubName = "预算测试社团",
            ClubStatus = "active",
            CreatedAt = now
        });
        db.BudgetAccounts.Add(new BudgetAccount
        {
            AccountId = AccountId,
            ClubId = ClubId,
            FiscalYear = "2026",
            AccountName = "2026 年度经费",
            InitialAmount = 10_000m,
            AccountStatus = "active",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BudgetApplications.Add(new BudgetApplication
        {
            ApplicationId = ApplicationId,
            AccountId = AccountId,
            ClubId = ClubId,
            ApplicantUserId = ApplicantUserId,
            ApplicationType = "activity_budget",
            Title = "原经费申请",
            Amount = 1_000m,
            Purpose = "原经费用途",
            Detail = "原预算明细",
            ApplicationStatus = applicationStatus,
            SubmittedAt = now.AddDays(-1),
            ReviewerUserId = applicationStatus == "rejected" ? ReviewerUserId : null,
            ReviewComment = applicationStatus == "rejected" ? "请补充预算明细" : null,
            ReviewedAt = applicationStatus == "rejected" ? now.AddHours(-1) : null,
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now
        });

        if (grantApplicantReviewPermission)
        {
            db.Roles.Add(new Role
            {
                RoleId = 501,
                RoleCode = "SYSTEM_ADMIN",
                RoleName = "系统管理员",
                RoleScope = "system",
                CreatedAt = now
            });
            db.UserRoles.Add(new UserRole
            {
                UserRoleId = 601,
                UserId = ApplicantUserId,
                RoleId = 501,
                AssignedAt = now
            });
        }

        if (includeReviewRecord)
        {
            db.BudgetReviewRecords.Add(new BudgetReviewRecord
            {
                ReviewId = 701,
                ApplicationId = ApplicationId,
                ReviewerUserId = ReviewerUserId,
                Approved = 0,
                CommentText = "请补充预算明细",
                ReviewedAt = now.AddHours(-1)
            });
        }

        await db.SaveChangesAsync();
        var token = scope.ServiceProvider
            .GetRequiredService<AuthTokenService>()
            .CreateToken(applicant);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "budget-flow-test-key");
        return client;
    }
}

internal sealed class RelationalBudgetWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    private RelationalBudgetWebApplicationFactory() => _connection.Open();

    public static async Task<RelationalBudgetWebApplicationFactory> CreateAsync()
    {
        var factory = new RelationalBudgetWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        await db.Database.EnsureCreatedAsync();
        return factory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:TokenSigningKey"] = "ClubHub.Tests.TokenSigningKey",
                ["ConnectionStrings:Default"] = "Tests use an isolated SQLite in-memory database"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ClubHubDbContext>();
            services.RemoveAll<DbContextOptions<ClubHubDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ClubHubDbContext>>();
            services.RemoveAll<IDatabaseProvider>();

            var options = new DbContextOptionsBuilder<ClubHubDbContext>()
                .UseSqlite(_connection)
                .Options;
            services.AddScoped<ClubHubDbContext>(_ => new SqliteClubHubDbContext(options));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private sealed class SqliteClubHubDbContext(DbContextOptions<ClubHubDbContext> options)
        : ClubHubDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var property in modelBuilder.Model
                         .GetEntityTypes()
                         .SelectMany(entity => entity.GetProperties()))
            {
                property.SetDefaultValueSql(null);
            }
        }
    }

}
