using System.Net;
using System.Text.Json;
using ClubHub.Api.Controllers;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
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

        var result = ActivitiesController.ToApiModel(activity, 12, true);

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
        Assert.Equal(expected, ActivitiesController.ParseActivityStatus(status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("archived")]
    public void StatusMappingRejectsValuesOutsideOpenApiContract(string? status)
    {
        Assert.Throws<InvalidOperationException>(
            () => ActivitiesController.ParseActivityStatus(status));
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
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("published", item.GetProperty("status").GetString());
        Assert.Equal(119.50d, item.GetProperty("budgetAmount").GetDouble());
        Assert.Equal(0, item.GetProperty("currentParticipants").GetInt32());
        Assert.False(item.GetProperty("isRegistered").GetBoolean());
    }

    [Fact]
    public void HandwrittenActivityDtoIsRemoved()
    {
        Assert.Null(typeof(ActivitiesController).Assembly.GetType(
            "ClubHub.Api.Controllers.ActivityDto"));
    }
}
