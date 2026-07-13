using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

/// <summary>项目任务一次进度提交的可追溯记录，对应 PROJECT_TASK_PROGRESS_REPORTS 表。</summary>
[Table("PROJECT_TASK_PROGRESS_REPORTS")]
public class ProjectTaskProgressReport
{
    [Column("TASK_PROGRESS_REPORT_ID")]
    public int TaskProgressReportId { get; set; }

    [Column("TASK_ID")]
    public int TaskId { get; set; }

    [Column("REPORTER_USER_ID")]
    public int ReporterUserId { get; set; }

    [Column("PROGRESS")]
    public int Progress { get; set; }

    [Column("TASK_STATUS")]
    public string? TaskStatus { get; set; }

    [Column("REPORT_CONTENT")]
    public string? ReportContent { get; set; }

    [Column("DELAY_REASON")]
    public string? DelayReason { get; set; }

    [Column("SUBMITTED_AT")]
    public DateTime SubmittedAt { get; set; }

    [ForeignKey(nameof(TaskId))]
    public ProjectTask? Task { get; set; }

    [ForeignKey(nameof(ReporterUserId))]
    public User? Reporter { get; set; }
}
