using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

/// <summary>
/// 项目与用户之间的成员关系，对应数据库 PROJECT_MEMBERS 表。
/// </summary>
[Table("PROJECT_MEMBERS")]
public class ProjectMember
{
    [Column("PROJECT_MEMBER_ID")]
    public int ProjectMemberId { get; set; }

    [Column("PROJECT_ID")]
    public int ProjectId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("MEMBER_ROLE")]
    public string MemberRole { get; set; } = "member";

    [Column("MEMBER_STATUS")]
    public string MemberStatus { get; set; } = "active";

    [Column("JOINED_AT")]
    public DateTime JoinedAt { get; set; }

    [Column("LEFT_AT")]
    public DateTime? LeftAt { get; set; }

    [Column("REMARK")]
    public string? Remark { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(ProjectId))]
    public Project? Project { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
