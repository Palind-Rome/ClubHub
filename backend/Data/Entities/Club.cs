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

    [Column("PRESIDENT_USER_ID")]
    public int? PresidentUserId { get; set; }

    [Column("CLUB_STATUS")]
    public string? ClubStatus { get; set; }

    [Column("FOUNDED_AT")]
    public DateTime? FoundedAt { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
}
