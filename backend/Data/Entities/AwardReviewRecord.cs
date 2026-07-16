using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("AWARD_REVIEW_RECORDS")]
public class AwardReviewRecord
{
    [Column("REVIEW_ID")]
    public int ReviewId { get; set; }

    [Column("AWARD_APPLICATION_ID")]
    public int AwardApplicationId { get; set; }

    [Column("REVIEW_ROUND")]
    public int ReviewRound { get; set; } = 1;

    [Column("REVIEW_STEP")]
    public string ReviewStep { get; set; } = string.Empty;

    [Column("REVIEW_RESULT")]
    public string ReviewResult { get; set; } = string.Empty;

    [Column("REVIEWER_USER_ID")]
    public int? ReviewerUserId { get; set; }

    [Column("REVIEW_COMMENT")]
    public string? ReviewComment { get; set; }

    [Column("FROM_STATUS")]
    public string? FromStatus { get; set; }

    [Column("TO_STATUS")]
    public string? ToStatus { get; set; }

    [Column("REVIEWED_AT")]
    public DateTime ReviewedAt { get; set; }

    [ForeignKey(nameof(AwardApplicationId))]
    public AwardApplication? Application { get; set; }

    [ForeignKey(nameof(ReviewerUserId))]
    public User? Reviewer { get; set; }
}
