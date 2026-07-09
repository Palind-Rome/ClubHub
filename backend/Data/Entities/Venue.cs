using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("VENUES")]
public class Venue
{
    [Column("VENUE_ID")]
    public int VenueId { get; set; }

    [Column("MANAGER_USER_ID")]
    public int? ManagerUserId { get; set; }

    [Column("VENUE_NAME")]
    public string? VenueName { get; set; }

    [Column("BUILDING")]
    public string? Building { get; set; }

    [Column("ROOM_NO")]
    public string? RoomNo { get; set; }

    [Column("CAPACITY")]
    public int? Capacity { get; set; }

    [Column("VENUE_STATUS")]
    public string? VenueStatus { get; set; }

    [Column("CREATED_AT")]
    public DateTime? CreatedAt { get; set; }

    public ICollection<VenueReservation> Reservations { get; set; } = new List<VenueReservation>();
}
