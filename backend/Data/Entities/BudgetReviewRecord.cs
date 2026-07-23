using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("BUDGET_REVIEW_RECORDS")]
public class BudgetReviewRecord
{
    [Column("REVIEW_ID")]
    public int ReviewId { get; set; }

    [Column("APPLICATION_ID")]
    public int ApplicationId { get; set; }

    [Column("REVIEWER_USER_ID")]
    public int ReviewerUserId { get; set; }

    [Column("APPROVED")]
    public int Approved { get; set; }

    [Column("COMMENT_TEXT")]
    public string? CommentText { get; set; }

    [Column("REVIEWED_AT")]
    public DateTime ReviewedAt { get; set; }

    [ForeignKey(nameof(ApplicationId))]
    public BudgetApplication? Application { get; set; }

    [ForeignKey(nameof(ReviewerUserId))]
    public User? ReviewerUser { get; set; }
}
