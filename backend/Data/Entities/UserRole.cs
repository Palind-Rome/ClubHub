using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("USER_ROLES")]
public class UserRole
{
    [Column("USER_ROLE_ID")]
    public int UserRoleId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("ROLE_ID")]
    public int RoleId { get; set; }

    [Column("CLUB_ID")]
    public int? ClubId { get; set; }

    [Column("ASSIGNED_AT")]
    public DateTime AssignedAt { get; set; }

    [ForeignKey("UserId")]
    public User? User { get; set; }

    [ForeignKey("RoleId")]
    public Role? Role { get; set; }
}
