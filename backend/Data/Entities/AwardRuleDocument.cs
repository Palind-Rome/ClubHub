using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("AWARD_RULE_DOCUMENTS")]
public class AwardRuleDocument
{
    [Column("RULE_DOCUMENT_ID")]
    public int RuleDocumentId { get; set; }

    [Column("CLUB_ID")]
    public int? ClubId { get; set; }

    [Column("RULE_TITLE")]
    public string RuleTitle { get; set; } = string.Empty;

    [Column("RULE_SCOPE")]
    public string RuleScope { get; set; } = "club";

    [Column("ACADEMIC_YEAR")]
    public string AcademicYear { get; set; } = string.Empty;

    [Column("TERM_NAME")]
    public string? TermName { get; set; }

    [Column("ISSUER_NAME")]
    public string? IssuerName { get; set; }

    [Column("SUMMARY")]
    public string? Summary { get; set; }

    [Column("CONTENT_TEXT")]
    public string? ContentText { get; set; }

    [Column("MATERIAL_URL")]
    public string? MaterialUrl { get; set; }

    [Column("MATERIAL_NAME")]
    public string? MaterialName { get; set; }

    [Column("VERSION_NO")]
    public string VersionNo { get; set; } = "1.0";

    [Column("RULE_STATUS")]
    public string RuleStatus { get; set; } = "draft";

    [Column("EFFECTIVE_START_AT")]
    public DateTime? EffectiveStartAt { get; set; }

    [Column("EFFECTIVE_END_AT")]
    public DateTime? EffectiveEndAt { get; set; }

    [Column("PUBLISHED_BY_USER_ID")]
    public int? PublishedByUserId { get; set; }

    [Column("PUBLISHED_AT")]
    public DateTime? PublishedAt { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [ForeignKey(nameof(PublishedByUserId))]
    public User? PublishedByUser { get; set; }
}
