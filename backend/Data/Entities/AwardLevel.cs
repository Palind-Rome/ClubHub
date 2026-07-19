using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("AWARD_LEVELS")]
public class AwardLevel
{
    [Column("AWARD_LEVEL_ID")]
    public int AwardLevelId { get; set; }

    [Column("AWARD_SCHEME_ID")]
    public int AwardSchemeId { get; set; }

    [Column("LEVEL_NAME")]
    public string LevelName { get; set; } = string.Empty;

    [Column("AWARD_SCORE")]
    public decimal AwardScore { get; set; }

    [Column("AMOUNT")]
    public decimal? Amount { get; set; }

    [Column("QUOTA")]
    public int? Quota { get; set; }

    [Column("DISPLAY_ORDER")]
    public int DisplayOrder { get; set; }

    [Column("LEVEL_STATUS")]
    public string LevelStatus { get; set; } = "active";

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(AwardSchemeId))]
    public AwardScheme? Scheme { get; set; }

    public ICollection<AwardApplication> Applications { get; set; } = new List<AwardApplication>();
}
