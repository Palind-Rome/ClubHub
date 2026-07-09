using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("RECRUITMENTS")]
public class Recruitment
{
    [Column("RECRUIT_ID")]
    public int RecruitId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("TITLE")]
    public string Title { get; set; } = string.Empty;

    [Column("DESCRIPTION")]
    public string? Description { get; set; }

    [Column("START_AT")]
    public DateTime? StartAt { get; set; }

    [Column("END_AT")]
    public DateTime? EndAt { get; set; }

    [Column("QUOTA")]
    public int? Quota { get; set; }

    [Column("REQUIREMENTS")]
    public string? Requirements { get; set; }

    [Column("RECRUIT_STATUS")]
    // Stores one of the persisted values defined in RecruitmentStatuses.
    public string? RecruitStatus { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    public Club? Club { get; set; }

    public ICollection<RecruitmentApplication> Applications { get; set; } = new List<RecruitmentApplication>();
}
