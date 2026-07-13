using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("PROJECT_TASK_ASSIGNEES")]
public class ProjectTaskAssignee
{
    [Column("TASK_ASSIGNEE_ID")]
    public int TaskAssigneeId { get; set; }

    [Column("TASK_ID")]
    public int TaskId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("ASSIGNED_AT")]
    public DateTime AssignedAt { get; set; }

    [ForeignKey(nameof(TaskId))]
    public ProjectTask? Task { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
