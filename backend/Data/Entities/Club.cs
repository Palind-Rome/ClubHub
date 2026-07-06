using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("CLUBS")]
public class Club
{
    [Column("club_id")]
    public int ClubId { get; set; }

    [Column("club_name")]
    public string ClubName { get; set; } = string.Empty;

    [Column("category")]
    public string? Category { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("logo_url")]
    public string? LogoUrl { get; set; }

    [Column("president_user_id")]
    public int? PresidentUserId { get; set; }

    [Column("club_status")]
    public string? ClubStatus { get; set; }

    [Column("founded_at")]
    public DateTime? FoundedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // 导航属性：关联的活动
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
}
