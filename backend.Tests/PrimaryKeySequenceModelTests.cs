using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClubHub.Api.Tests;

public sealed class PrimaryKeySequenceModelTests
{
    public static TheoryData<Type, string, string> RemainingSequenceBackedKeys => new()
    {
        { typeof(Role), nameof(Role.RoleId), "SEQ_ROLES.NEXTVAL" },
        { typeof(Recruitment), nameof(Recruitment.RecruitId), "SEQ_RECRUITMENTS.NEXTVAL" },
        { typeof(RecruitmentApplication), nameof(RecruitmentApplication.ApplicationId), "SEQ_RECRUITMENT_APPLICATIONS.NEXTVAL" },
        { typeof(Venue), nameof(Venue.VenueId), "SEQ_VENUES.NEXTVAL" },
        { typeof(VenueReservation), nameof(VenueReservation.ReservationId), "SEQ_VENUE_RESERVATIONS.NEXTVAL" },
        { typeof(Project), nameof(Project.ProjectId), "SEQ_PROJECTS.NEXTVAL" },
        { typeof(ProjectMember), nameof(ProjectMember.ProjectMemberId), "SEQ_PROJECT_MEMBERS.NEXTVAL" },
        { typeof(ProjectTask), nameof(ProjectTask.TaskId), "SEQ_PROJECT_TASKS.NEXTVAL" },
        { typeof(ProjectTaskAssignee), nameof(ProjectTaskAssignee.TaskAssigneeId), "SEQ_PROJECT_TASK_ASSIGNEES.NEXTVAL" },
        { typeof(ProjectTaskProgressReport), nameof(ProjectTaskProgressReport.TaskProgressReportId), "SEQ_PROJECT_TASK_PROGRESS_REPORTS.NEXTVAL" },
        { typeof(LearningItem), nameof(LearningItem.ItemId), "SEQ_LEARNING_ITEMS.NEXTVAL" },
        { typeof(LearningRecord), nameof(LearningRecord.RecordId), "SEQ_LEARNING_RECORDS.NEXTVAL" },
        { typeof(Material), nameof(Material.MaterialId), "SEQ_MATERIALS.NEXTVAL" },
        { typeof(MaterialBorrow), nameof(MaterialBorrow.BorrowId), "SEQ_MATERIAL_BORROWS.NEXTVAL" },
        { typeof(Notice), nameof(Notice.NoticeId), "SEQ_NOTICES.NEXTVAL" },
        { typeof(OperationLog), nameof(OperationLog.LogId), "SEQ_OPERATION_LOGS.NEXTVAL" }
    };

    [Theory]
    [MemberData(nameof(RemainingSequenceBackedKeys))]
    public void RemainingPrimaryKeysUseOracleSequenceDefaults(
        Type entityType,
        string propertyName,
        string expectedDefaultSql)
    {
        var options = new DbContextOptionsBuilder<ClubHubDbContext>()
            .UseOracle("Data Source=ClubHubModelTests")
            .Options;
        using var db = new ClubHubDbContext(options);

        var entity = db.Model.FindEntityType(entityType);
        Assert.NotNull(entity);

        var property = entity.FindProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
        Assert.Equal(expectedDefaultSql, property.GetDefaultValueSql());
    }
}
