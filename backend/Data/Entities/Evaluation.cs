using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("EVALUATIONS")]
public class Evaluation
{
    [Column("EVALUATION_ID")]
    public int EvaluationId { get; set; }

    [Column("EVALUATION_TYPE")]
    public string? EvaluationType { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("EVALUATOR_USER_ID")]
    public int? EvaluatorUserId { get; set; }

    [Column("TERM_NAME")]
    public string? TermName { get; set; }

    [Column("AWARD_TITLE")]
    public string? AwardTitle { get; set; }

    [Column("AWARD_LEVEL")]
    public string? AwardLevel { get; set; }

    [Column("AWARD_REASON")]
    public string? AwardReason { get; set; }

    [Column("ACTIVITY_SCORE")]
    public decimal? ActivityScore { get; set; }

    [Column("TASK_SCORE")]
    public decimal? TaskScore { get; set; }

    [Column("LEARNING_SCORE")]
    public decimal? LearningScore { get; set; }

    [Column("AWARD_SCORE")]
    public decimal? AwardScore { get; set; }

    [Column("TOTAL_SCORE")]
    public decimal? TotalScore { get; set; }

    [Column("GRADE")]
    public string? Grade { get; set; }

    [Column("PUBLIC_STATUS")]
    public string? PublicStatus { get; set; }

    [Column("COMMENT_TEXT")]
    public string? CommentText { get; set; }

    [Column("CREATED_AT")]
    public DateTime? CreatedAt { get; set; }

    public Club? Club { get; set; }

    public User? User { get; set; }

    [ForeignKey(nameof(EvaluatorUserId))]
    public User? Evaluator { get; set; }
}
