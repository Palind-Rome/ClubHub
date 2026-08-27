using System.Net;
using System.Text.Json;
using ClubHub.Api.Controllers;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Rest;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ApiActivity = Org.OpenAPITools.Models.Activity;

namespace ClubHub.Api.Tests;

public sealed class ActivityResponseModelTests
{
    [Fact]
    public void EntityMappingUsesGeneratedActivityModel()
    {
        var startTime = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        var activity = new Activity
        {
            ActivityId = 119,
            ClubId = 7,
            CreatorUserId = 21,
            Title = "数据库设计交流会",
            ActivityType = "seminar",
            Description = "统一活动响应模型",
            Location = "济事楼 119",
            StartAt = startTime,
            EndAt = startTime.AddHours(2),
            ActivityStatus = "pending_review",
            Capacity = 80,
            RegistrationDeadline = startTime.AddDays(-1),
            ReviewerUserId = 22,
            ReviewComment = "等待审核",
            BudgetAmount = 1199.50m,
            BudgetPurpose = "活动物料",
            BudgetDetail = "海报与打印",
            BudgetStatus = "pending",
            BudgetReviewerId = 23,
            BudgetComment = "等待经费审核",
            PublishedAt = null,
            CheckinStartAt = startTime,
            CheckinEndAt = startTime.AddMinutes(30),
            CheckoutStartAt = startTime.AddHours(1),
            CheckoutEndAt = startTime.AddHours(2),
            Club = new Club { ClubId = 7, ClubName = "数据库社" }
        };

        var mapped = ActivitiesController.TryToApiModel(activity, 12, true, out var result);

        Assert.True(mapped);
        Assert.IsType<ApiActivity>(result);
        Assert.Equal(119, result.Id);
        Assert.Equal("数据库设计交流会", result.Title);
        Assert.Equal("数据库社", result.ClubName);
        Assert.Equal(ApiActivity.StatusEnum.PendingReviewEnum, result.Status);
        Assert.Equal(1199.50d, result.BudgetAmount);
        Assert.Equal(12, result.CurrentParticipants);
        Assert.True(result.IsRegistered);
    }

    [Theory]
    [InlineData("draft", ApiActivity.StatusEnum.DraftEnum)]
    [InlineData(" PUBLISHED ", ApiActivity.StatusEnum.PublishedEnum)]
    [InlineData("OnGoInG", ApiActivity.StatusEnum.OngoingEnum)]
    [InlineData("pending_review", ApiActivity.StatusEnum.PendingReviewEnum)]
    [InlineData("published", ApiActivity.StatusEnum.PublishedEnum)]
    [InlineData("rejected", ApiActivity.StatusEnum.RejectedEnum)]
    [InlineData("ongoing", ApiActivity.StatusEnum.OngoingEnum)]
    [InlineData("finished", ApiActivity.StatusEnum.FinishedEnum)]
    [InlineData("cancelled", ApiActivity.StatusEnum.CancelledEnum)]
    public void StatusMappingCoversOpenApiEnumValues(
        string status,
        ApiActivity.StatusEnum expected)
    {
        var parsed = ActivitiesController.TryParseActivityStatus(status, out var result);

        Assert.True(parsed);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("archived")]
    public void StatusMappingReturnsFalseForValuesOutsideOpenApiContract(string? status)
    {
        Assert.False(ActivitiesController.TryParseActivityStatus(status, out _));
    }

    [Fact]
    public async Task ActivityListKeepsOpenApiJsonWireFormat()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            db.Clubs.Add(new Club
            {
                ClubId = 1,
                ClubName = "数据库社",
                CreatedAt = DateTime.UtcNow
            });
            db.Activities.Add(new Activity
            {
                ActivityId = 119,
                ClubId = 1,
                Title = "活动响应模型测试",
                ActivityStatus = "published",
                BudgetAmount = 119.50m,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/activities?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("X-Page").Single());
        Assert.Equal("10", response.Headers.GetValues("X-Page-Size").Single());
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
        Assert.True(response.Headers.Contains("Link"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("published", item.GetProperty("status").GetString());
        Assert.Equal(119.50d, item.GetProperty("budgetAmount").GetDouble());
        Assert.Equal(0, item.GetProperty("currentParticipants").GetInt32());
        Assert.False(item.GetProperty("isRegistered").GetBoolean());
    }

    [Fact]
    public async Task ActivityListSkipsNullAndUnknownStatuses()
    {
        await using var baseFactory = new ClubHubWebApplicationFactory();
        await using var factory = WithNullActivityLogger(baseFactory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            db.Clubs.Add(new Club
            {
                ClubId = 1,
                ClubName = "数据库社",
                CreatedAt = DateTime.UtcNow
            });
            db.Activities.AddRange(
                new Activity
                {
                    ActivityId = 117,
                    ClubId = 1,
                    Title = "空状态活动",
                    ActivityStatus = null,
                    CreatedAt = DateTime.UtcNow
                },
                new Activity
                {
                    ActivityId = 118,
                    ClubId = 1,
                    Title = "未知状态活动",
                    ActivityStatus = "archived",
                    CreatedAt = DateTime.UtcNow
                },
                new Activity
                {
                    ActivityId = 119,
                    ClubId = 1,
                    Title = "有效活动",
                    ActivityStatus = "published",
                    CreatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/activities?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(119, item.GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("archived")]
    public async Task ActivityDetailReturnsControlledErrorForInvalidStatus(string? status)
    {
        await using var baseFactory = new ClubHubWebApplicationFactory();
        await using var factory = WithNullActivityLogger(baseFactory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            db.Clubs.Add(new Club
            {
                ClubId = 1,
                ClubName = "数据库社",
                CreatedAt = DateTime.UtcNow
            });
            db.Activities.Add(new Activity
            {
                ActivityId = 119,
                ClubId = 1,
                Title = "异常状态活动",
                ActivityStatus = status,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/activities/119");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ApiErrorCodes.ServiceUnavailable,
            document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "活动状态数据异常，请稍后重试。",
            document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void HandwrittenActivityDtoIsRemoved()
    {
        Assert.Null(typeof(ActivitiesController).Assembly.GetType(
            "ClubHub.Api.Controllers.ActivityDto"));
    }

    private static WebApplicationFactory<Program> WithNullActivityLogger(
        ClubHubWebApplicationFactory factory)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ILogger<ActivitiesController>>(
                    NullLogger<ActivitiesController>.Instance)));
    }
}
