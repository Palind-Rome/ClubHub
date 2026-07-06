using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("ACTIVITIES")]
public class Activity
{
    [Column("ACTIVITY_ID")]
    public int ActivityId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("TITLE")]
    public string Title { get; set; } = string.Empty;

    [Column("ACTIVITY_TYPE")]
    public string? ActivityType { get; set; }

    [Column("DESCRIPTION")]
    public string? Description { get; set; }

    [Column("LOCATION")]
    public string? Location { get; set; }

    [Column("START_AT")]
    public DateTime? StartAt { get; set; }

    [Column("END_AT")]
    public DateTime? EndAt { get; set; }

    [Column("CAPACITY")]
    public int? Capacity { get; set; }

    [Column("REGISTRATION_DEADLINE")]
    public DateTime? RegistrationDeadline { get; set; }

    [Column("ACTIVITY_STATUS")]
    public string? ActivityStatus { get; set; }

    [Column("PUBLISHED_AT")]
    public DateTime? PublishedAt { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("ClubId")]
    public Club? Club { get; set; }
}
