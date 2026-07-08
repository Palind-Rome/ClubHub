using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("ROLES")]
public class Role
{
    [Column("ROLE_ID")]
    public int RoleId { get; set; }

    [Column("ROLE_CODE")]
    public string RoleCode { get; set; } = string.Empty;

    [Column("ROLE_NAME")]
    public string RoleName { get; set; } = string.Empty;

    [Column("ROLE_SCOPE")]
    public string RoleScope { get; set; } = string.Empty;

    [Column("PERMISSION_DESC")]
    public string? PermissionDesc { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
