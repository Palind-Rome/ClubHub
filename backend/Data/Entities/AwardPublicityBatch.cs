using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("AWARD_PUBLICITY_BATCHES")]
public class AwardPublicityBatch
{
    [Column("PUBLICITY_BATCH_ID")]
    public int PublicityBatchId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("TITLE")]
    public string Title { get; set; } = string.Empty;

    [Column("DESCRIPTION")]
    public string? Description { get; set; }

    [Column("PUBLICITY_START_AT")]
    public DateTime? PublicityStartAt { get; set; }

    [Column("PUBLICITY_END_AT")]
    public DateTime? PublicityEndAt { get; set; }

    [Column("PUBLICITY_STATUS")]
    public string PublicityStatus { get; set; } = "draft";

    [Column("PUBLISHER_USER_ID")]
    public int? PublisherUserId { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [ForeignKey(nameof(PublisherUserId))]
    public User? Publisher { get; set; }

    public ICollection<AwardPublicityItem> Items { get; set; } = new List<AwardPublicityItem>();
}
