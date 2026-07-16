using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("AWARD_PUBLICITY_ITEMS")]
public class AwardPublicityItem
{
    [Column("PUBLICITY_ITEM_ID")]
    public int PublicityItemId { get; set; }

    [Column("PUBLICITY_BATCH_ID")]
    public int PublicityBatchId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("AWARD_APPLICATION_ID")]
    public int AwardApplicationId { get; set; }

    [Column("DISPLAY_ORDER")]
    public int DisplayOrder { get; set; }

    [Column("PUBLICITY_RESULT")]
    public string PublicityResult { get; set; } = "normal";

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    public AwardPublicityBatch? Batch { get; set; }

    public AwardApplication? Application { get; set; }
}
