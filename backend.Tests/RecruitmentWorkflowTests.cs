using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;

namespace ClubHub.Api.Tests;

public sealed class RecruitmentWorkflowTests
{
    private static readonly DateTime Now = new(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, "2026-07-21", "2026-07-22", 10, "要求", "招募标题不能为空。")]
    [InlineData("标题", null, "2026-07-22", 10, "要求", "招募开始时间不能为空。")]
    [InlineData("标题", "2026-07-22", "2026-07-21", 10, "要求", "招募结束时间必须晚于开始时间。")]
    [InlineData("标题", "2026-07-21", "2026-07-22", 0, "要求", "招募人数必须大于 0。")]
    [InlineData("标题", "2026-07-21", "2026-07-22", 10, " ", "招募要求不能为空。")]
    public void ValidateRecruitmentStateRejectsInvalidInput(
        string? title,
        string? startAt,
        string? endAt,
        int? quota,
        string? requirements,
        string expected)
    {
        Assert.Equal(
            expected,
            RecruitmentWorkflow.ValidateRecruitmentState(
                title,
                ParseDate(startAt),
                ParseDate(endAt),
                quota,
                requirements));
    }

    [Fact]
    public void ValidateRecruitmentStateAcceptsCompleteInput()
    {
        Assert.Null(RecruitmentWorkflow.ValidateRecruitmentState(
            "秋季纳新",
            Now,
            Now.AddDays(7),
            20,
            "欢迎在校学生报名"));
    }

    [Theory]
    [InlineData(RecruitmentStatuses.Draft, -1, 1, RecruitmentStatuses.Draft)]
    [InlineData(RecruitmentStatuses.PendingReview, -1, 1, RecruitmentStatuses.PendingReview)]
    [InlineData(RecruitmentStatuses.Published, 1, 2, RecruitmentStatuses.NotStarted)]
    [InlineData(RecruitmentStatuses.Published, -1, 1, RecruitmentStatuses.Accepting)]
    [InlineData(RecruitmentStatuses.Published, -2, -1, RecruitmentStatuses.Ended)]
    [InlineData(RecruitmentStatuses.Closed, -2, 2, RecruitmentStatuses.Ended)]
    public void EffectiveStatusCombinesWorkflowAndTimeWindow(
        string storedStatus,
        int startOffsetDays,
        int endOffsetDays,
        string expected)
    {
        var recruitment = new Recruitment
        {
            RecruitStatus = storedStatus,
            StartAt = Now.AddDays(startOffsetDays),
            EndAt = Now.AddDays(endOffsetDays)
        };

        Assert.Equal(expected, RecruitmentWorkflow.EffectiveRecruitmentStatus(recruitment, Now));
    }

    [Theory]
    [InlineData(" 待审核 ", RecruitmentStatuses.PendingReview)]
    [InlineData("OPEN", RecruitmentStatuses.Published)]
    [InlineData("已结束", RecruitmentStatuses.Closed)]
    [InlineData("unknown", null)]
    public void StorageStatusNormalizationSupportsApiAndChineseValues(string input, string? expected)
    {
        Assert.Equal(expected, RecruitmentWorkflow.NormalizeRecruitmentStorageStatus(input));
    }

    private static DateTime? ParseDate(string? value) =>
        value is null ? null : DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
