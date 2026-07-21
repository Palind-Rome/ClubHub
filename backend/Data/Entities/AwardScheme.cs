using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("AWARD_SCHEMES")]
public class AwardScheme
{
    [Column("AWARD_SCHEME_ID")]
    public int AwardSchemeId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("AWARD_NAME")]
    public string AwardName { get; set; } = string.Empty;

    [Column("AWARD_CATEGORY")]
    public string AwardCategory { get; set; } = "honor";

    [Column("ACADEMIC_YEAR")]
    public string AcademicYear { get; set; } = string.Empty;

    [Column("TERM_NAME")]
    public string? TermName { get; set; }

    [Column("SPONSOR_UNIT")]
    public string? SponsorUnit { get; set; }

    [Column("REWARD_LEVEL")]
    public string? RewardLevel { get; set; }

    [Column("FUNDING_SOURCE")]
    public string? FundingSource { get; set; }

    [Column("IS_RANKED")]
    public int IsRanked { get; set; } = 1;

    [Column("IS_FIXED_AMOUNT")]
    public int IsFixedAmount { get; set; } = 1;

    [Column("DESCRIPTION")]
    public string? Description { get; set; }

    [Column("MATERIAL_DESCRIPTION")]
    public string? MaterialDescription { get; set; }

    [Column("APPLICATION_START_AT")]
    public DateTime? ApplicationStartAt { get; set; }

    [Column("APPLICATION_END_AT")]
    public DateTime? ApplicationEndAt { get; set; }

    [Column("PUBLICITY_START_AT")]
    public DateTime? PublicityStartAt { get; set; }

    [Column("PUBLICITY_END_AT")]
    public DateTime? PublicityEndAt { get; set; }

    [Column("SCHEME_STATUS")]
    public string SchemeStatus { get; set; } = "draft";

    [Column("CREATED_BY_USER_ID")]
    public int? CreatedByUserId { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public User? CreatedByUser { get; set; }

    public ICollection<AwardLevel> Levels { get; set; } = new List<AwardLevel>();

    public ICollection<AwardApplication> Applications { get; set; } = new List<AwardApplication>();
}
