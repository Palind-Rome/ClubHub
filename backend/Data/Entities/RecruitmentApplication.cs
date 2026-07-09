using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("RECRUITMENT_APPLICATIONS")]
public class RecruitmentApplication
{
    [Column("APPLICATION_ID")]
    public int ApplicationId { get; set; }

    [Column("RECRUIT_ID")]
    public int RecruitId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("APPLICATION_REASON")]
    public string? ApplicationReason { get; set; }

    [Column("INTERVIEW_SCORE")]
    public decimal? InterviewScore { get; set; }

    [Column("APPLICATION_STATUS")]
    public string? ApplicationStatus { get; set; }

    [Column("REVIEWER_USER_ID")]
    public int? ReviewerUserId { get; set; }

    [Column("SUBMITTED_AT")]
    public DateTime? SubmittedAt { get; set; }

    [Column("REVIEWED_AT")]
    public DateTime? ReviewedAt { get; set; }

    public Recruitment? Recruitment { get; set; }

    public User? User { get; set; }

    public User? Reviewer { get; set; }
}
