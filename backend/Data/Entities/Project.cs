using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

/// <summary>
/// 社团项目立项申请实体，对应数据库 PROJECTS 表。
/// </summary>
[Table("PROJECTS")]
public class Project
{
    [Column("PROJECT_ID")]
    public int ProjectId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("PROJECT_NAME")]
    public string ProjectName { get; set; } = string.Empty;

    [Column("DESCRIPTION")]
    public string? Description { get; set; }

    [Column("LEADER_USER_ID")]
    public int? LeaderUserId { get; set; }

    [Column("START_DATE")]
    public DateTime? StartDate { get; set; }

    [Column("END_DATE")]
    public DateTime? EndDate { get; set; }

    [Column("PROJECT_STATUS")]
    public string? ProjectStatus { get; set; }

    [Column("REVIEWER_USER_ID")]
    public int? ReviewerUserId { get; set; }

    [Column("REVIEW_COMMENT")]
    public string? ReviewComment { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }
}
