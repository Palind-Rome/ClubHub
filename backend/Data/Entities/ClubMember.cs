using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("CLUB_MEMBERS")]
public class ClubMember
{
    [Column("MEMBER_ID")]
    public int MemberId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("DEPARTMENT_ID")]
    public int? DepartmentId { get; set; }

    [Column("GROUP_ID")]
    public int? GroupId { get; set; }

    [Column("DEPARTMENT_NAME")]
    public string? DepartmentName { get; set; }

    [Column("GROUP_NAME")]
    public string? GroupName { get; set; }

    [Column("POSITION_NAME")]
    public string? PositionName { get; set; }

    [Column("TERM_NAME")]
    public string? TermName { get; set; }

    [Column("TERM_START")]
    public DateTime? TermStart { get; set; }

    [Column("TERM_END")]
    public DateTime? TermEnd { get; set; }

    [Column("MEMBER_STATUS")]
    public string? MemberStatus { get; set; }

    [Column("JOIN_AT")]
    public DateTime? JoinAt { get; set; }

    [Column("CONTRIBUTION_SCORE")]
    public decimal? ContributionScore { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public ClubDepartment? Department { get; set; }

    public ClubGroup? Group { get; set; }
}
