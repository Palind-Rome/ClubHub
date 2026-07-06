using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("ACTIVITIES")]
public class Activity
{
    [Column("activity_id")]
    public int ActivityId { get; set; }

    [Column("club_id")]
    public int ClubId { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("activity_type")]
    public string? ActivityType { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("location")]
    public string? Location { get; set; }

    [Column("start_at")]
    public DateTime? StartAt { get; set; }

    [Column("end_at")]
    public DateTime? EndAt { get; set; }

    [Column("capacity")]
    public int? Capacity { get; set; }

    [Column("registration_deadline")]
    public DateTime? RegistrationDeadline { get; set; }

    [Column("activity_status")]
    public string? ActivityStatus { get; set; }

    [Column("published_at")]
    public DateTime? PublishedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    // 导航属性：所属社团
    [ForeignKey("ClubId")]
    public Club? Club { get; set; }
}
