using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("VENUE_RESERVATIONS")]
public class VenueReservation
{
    [Column("RESERVATION_ID")]
    public int ReservationId { get; set; }

    [Column("VENUE_ID")]
    public int VenueId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("ACTIVITY_ID")]
    public int? ActivityId { get; set; }

    [Column("APPLICANT_USER_ID")]
    public int ApplicantUserId { get; set; }

    [Column("START_AT")]
    public DateTime? StartAt { get; set; }

    [Column("END_AT")]
    public DateTime? EndAt { get; set; }

    [Column("PURPOSE")]
    public string? Purpose { get; set; }

    [Column("RESERVATION_STATUS")]
    public string? ReservationStatus { get; set; }

    [Column("REVIEWER_USER_ID")]
    public int? ReviewerUserId { get; set; }

    [Column("REVIEW_COMMENT")]
    public string? ReviewComment { get; set; }

    [Column("CREATED_AT")]
    public DateTime? CreatedAt { get; set; }

    [ForeignKey(nameof(VenueId))]
    public Venue? Venue { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [ForeignKey(nameof(ActivityId))]
    public Activity? Activity { get; set; }

    [ForeignKey(nameof(ApplicantUserId))]
    public User? ApplicantUser { get; set; }

    [ForeignKey(nameof(ReviewerUserId))]
    public User? ReviewerUser { get; set; }
}
