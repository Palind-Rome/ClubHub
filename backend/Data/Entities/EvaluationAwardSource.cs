using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("EVALUATION_AWARD_SOURCES")]
public class EvaluationAwardSource
{
    [Column("EVALUATION_ID")]
    public int EvaluationId { get; set; }

    [Column("AWARD_APPLICATION_ID")]
    public int AwardApplicationId { get; set; }

    [Column("AWARD_SCORE")]
    public decimal AwardScore { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(EvaluationId))]
    public Evaluation? Evaluation { get; set; }

    [ForeignKey(nameof(AwardApplicationId))]
    public AwardApplication? Application { get; set; }
}
