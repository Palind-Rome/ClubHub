using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("CLUBS")]
public class Club
{
    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("CLUB_NAME")]
    public string ClubName { get; set; } = string.Empty;

    [Column("CATEGORY")]
    public string? Category { get; set; }

    [Column("DESCRIPTION")]
    public string? Description { get; set; }

    [Column("LOGO_URL")]
    public string? LogoUrl { get; set; }

    [Column("APPLICANT_USER_ID")]
    public int? ApplicantUserId { get; set; }

    [Column("PRESIDENT_USER_ID")]
    public int? PresidentUserId { get; set; }

    [Column("ADVISOR_NAME")]
    public string? AdvisorName { get; set; }

    [Column("CONTACT_PHONE")]
    public string? ContactPhone { get; set; }

    [Column("APPLY_REASON")]
    public string? ApplyReason { get; set; }

    [Column("MATERIAL_URL")]
    public string? MaterialUrl { get; set; }

    [Column("AUDIT_STATUS")]
    public string? AuditStatus { get; set; }

    [Column("REVIEWER_USER_ID")]
    public int? ReviewerUserId { get; set; }

    [Column("REVIEW_COMMENT")]
    public string? ReviewComment { get; set; }

    [Column("CLUB_STATUS")]
    public string? ClubStatus { get; set; }

    [Column("FOUNDED_AT")]
    public DateTime? FoundedAt { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(ApplicantUserId))]
    public User? Applicant { get; set; }

    [ForeignKey(nameof(ReviewerUserId))]
    public User? Reviewer { get; set; }

    [ForeignKey(nameof(PresidentUserId))]
    public User? President { get; set; }

    public ICollection<Activity> Activities { get; set; } = new List<Activity>();

    public ICollection<Recruitment> Recruitments { get; set; } = new List<Recruitment>();

    public ICollection<LearningItem> LearningItems { get; set; } = new List<LearningItem>();

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public ICollection<ClubMember> Members { get; set; } = new List<ClubMember>();

    public ICollection<ClubDepartment> Departments { get; set; } = new List<ClubDepartment>();

    public ICollection<ClubGroup> Groups { get; set; } = new List<ClubGroup>();

    public ICollection<Notice> Notices { get; set; } = new List<Notice>();
}
