using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("AWARD_APPLICATIONS")]
public class AwardApplication
{
    [Column("AWARD_APPLICATION_ID")]
    public int AwardApplicationId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("AWARD_SCHEME_ID")]
    public int AwardSchemeId { get; set; }

    [Column("AWARD_LEVEL_ID")]
    public int AwardLevelId { get; set; }

    [Column("APPLICANT_USER_ID")]
    public int ApplicantUserId { get; set; }

    [Column("RECOMMENDER_USER_ID")]
    public int? RecommenderUserId { get; set; }

    [Column("SUBMITTER_USER_ID")]
    public int SubmitterUserId { get; set; }

    [Column("APPLICATION_TYPE")]
    public string ApplicationType { get; set; } = "self";

    [Column("APPLICATION_REASON")]
    public string? ApplicationReason { get; set; }

    [Column("MATERIAL_URL")]
    public string? MaterialUrl { get; set; }

    [Column("CURRENT_STEP")]
    public string CurrentStep { get; set; } = "student_submit";

    [Column("APPLICATION_STATUS")]
    public string ApplicationStatus { get; set; } = "draft";

    [Column("PUBLIC_STATUS")]
    public string PublicStatus { get; set; } = "none";

    [Column("REVIEW_ROUND")]
    public int ReviewRound { get; set; } = 1;

    [Column("FINAL_AWARD_SCORE")]
    public decimal? FinalAwardScore { get; set; }

    [Column("FINAL_AMOUNT")]
    public decimal? FinalAmount { get; set; }

    [Column("SUBMITTED_AT")]
    public DateTime? SubmittedAt { get; set; }

    [Column("APPROVED_AT")]
    public DateTime? ApprovedAt { get; set; }

    [Column("PUBLICIZED_AT")]
    public DateTime? PublicizedAt { get; set; }

    [Column("ARCHIVED_AT")]
    public DateTime? ArchivedAt { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    public AwardScheme? Scheme { get; set; }

    public AwardLevel? Level { get; set; }

    [ForeignKey(nameof(ApplicantUserId))]
    public User? Applicant { get; set; }

    [ForeignKey(nameof(RecommenderUserId))]
    public User? Recommender { get; set; }

    [ForeignKey(nameof(SubmitterUserId))]
    public User? Submitter { get; set; }

    public ICollection<AwardReviewRecord> ReviewRecords { get; set; } = new List<AwardReviewRecord>();

    public ICollection<AwardAttachment> Attachments { get; set; } = new List<AwardAttachment>();

    public ICollection<AwardPublicityItem> PublicityItems { get; set; } = new List<AwardPublicityItem>();

    public ICollection<EvaluationAwardSource> EvaluationSources { get; set; } = new List<EvaluationAwardSource>();
}
