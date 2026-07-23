using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("BUDGET_APPLICATIONS")]
public class BudgetApplication
{
    [Column("APPLICATION_ID")]
    public int ApplicationId { get; set; }

    [Column("ACCOUNT_ID")]
    public int AccountId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("ACTIVITY_ID")]
    public int? ActivityId { get; set; }

    [Column("APPLICANT_USER_ID")]
    public int ApplicantUserId { get; set; }

    [Column("APPLICATION_TYPE")]
    public string ApplicationType { get; set; } = string.Empty;

    [Column("TITLE")]
    public string Title { get; set; } = string.Empty;

    [Column("AMOUNT")]
    public decimal Amount { get; set; }

    [Column("PURPOSE")]
    public string Purpose { get; set; } = string.Empty;

    [Column("DETAIL")]
    public string? Detail { get; set; }

    [Column("APPLICATION_STATUS")]
    public string ApplicationStatus { get; set; } = "pending";

    [Column("SUBMITTED_AT")]
    public DateTime SubmittedAt { get; set; }

    [Column("REVIEWED_AT")]
    public DateTime? ReviewedAt { get; set; }

    [Column("REVIEWER_USER_ID")]
    public int? ReviewerUserId { get; set; }

    [Column("REVIEW_COMMENT")]
    public string? ReviewComment { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    public BudgetAccount? Account { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [ForeignKey(nameof(ActivityId))]
    public Activity? Activity { get; set; }

    [ForeignKey(nameof(ApplicantUserId))]
    public User? ApplicantUser { get; set; }

    [ForeignKey(nameof(ReviewerUserId))]
    public User? ReviewerUser { get; set; }

    public ICollection<BudgetReviewRecord> ReviewRecords { get; set; } =
        new List<BudgetReviewRecord>();

    public ICollection<BudgetTransaction> Transactions { get; set; } = new List<BudgetTransaction>();
}
