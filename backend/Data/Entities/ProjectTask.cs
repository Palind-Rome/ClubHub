using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

/// <summary>
/// 项目任务实体，对应数据库 PROJECT_TASKS 表。
/// </summary>
[Table("PROJECT_TASKS")]
public class ProjectTask
{
    [Column("TASK_ID")]
    public int TaskId { get; set; }

    [Column("PROJECT_ID")]
    public int? ProjectId { get; set; }

    [Column("ASSIGNEE_USER_ID")]
    public int? AssigneeUserId { get; set; }

    [Column("TITLE")]
    public string? Title { get; set; }

    [Column("CONTENT")]
    public string? Content { get; set; }

    [Column("PRIORITY")]
    public string? Priority { get; set; }

    [Column("START_DATE")]
    public DateTime? StartDate { get; set; }

    [Column("DUE_DATE")]
    public DateTime? DueDate { get; set; }

    [Column("FINISH_DATE")]
    public DateTime? FinishDate { get; set; }

    [Column("PROGRESS")]
    public int? Progress { get; set; }

    [Column("TASK_STATUS")]
    public string? TaskStatus { get; set; }

    [Column("DELAY_REASON")]
    public string? DelayReason { get; set; }

    [Column("DELIVERABLE_TITLE")]
    public string? DeliverableTitle { get; set; }

    [Column("DELIVERABLE_DESC")]
    public string? DeliverableDescription { get; set; }

    [Column("DELIVERABLE_URL")]
    public string? DeliverableUrl { get; set; }

    [Column("DELIVERABLE_STATUS")]
    public string? DeliverableStatus { get; set; }

    [Column("REVIEWER_USER_ID")]
    public int? ReviewerUserId { get; set; }

    [Column("REVIEW_COMMENT")]
    public string? ReviewComment { get; set; }

    [Column("DELIVERABLE_SUBMITTER_ID")]
    public int? DeliverableSubmitterId { get; set; }

    [Column("DELIVERABLE_SUBMITTED_AT")]
    public DateTime? DeliverableSubmittedAt { get; set; }

    [Column("DELIVERABLE_REVIEWED_AT")]
    public DateTime? DeliverableReviewedAt { get; set; }

    [ForeignKey(nameof(ProjectId))]
    public Project? Project { get; set; }

    [ForeignKey(nameof(AssigneeUserId))]
    public User? AssigneeUser { get; set; }

    [ForeignKey(nameof(ReviewerUserId))]
    public User? ReviewerUser { get; set; }

    [ForeignKey(nameof(DeliverableSubmitterId))]
    public User? DeliverableSubmitter { get; set; }

    public ICollection<ProjectTaskAssignee> Assignees { get; set; } = new List<ProjectTaskAssignee>();

    public ICollection<ProjectTaskProgressReport> ProgressReports { get; set; } = new List<ProjectTaskProgressReport>();
}
