using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("ACTIVITY_PARTICIPATIONS")]
public class ActivityParticipation
{
    [Column("PARTICIPATION_ID")]
    public int ParticipationId { get; set; }

    [Column("ACTIVITY_ID")]
    public int ActivityId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("REGISTER_STATUS")]
    public string? RegisterStatus { get; set; }

    [Column("REGISTERED_AT")]
    public DateTime? RegisteredAt { get; set; }

    [Column("CHECKIN_AT")]
    public DateTime? CheckinAt { get; set; }

    [Column("CHECKOUT_AT")]
    public DateTime? CheckoutAt { get; set; }

    [Column("SIGN_STATUS")]
    public string? SignStatus { get; set; }

    [Column("REMARK")]
    public string? Remark { get; set; }

    [ForeignKey("ActivityId")]
    public Activity? Activity { get; set; }

    [ForeignKey("UserId")]
    public User? User { get; set; }
}
