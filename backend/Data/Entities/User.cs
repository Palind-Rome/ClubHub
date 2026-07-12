using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("USERS")]
public class User
{
    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("USERNAME")]
    public string Username { get; set; } = string.Empty;

    [Column("PASSWORD_HASH")]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("REAL_NAME")]
    public string RealName { get; set; } = string.Empty;

    [Column("STUDENT_NO")]
    public string? StudentNo { get; set; }

    [Column("GENDER")]
    public string? Gender { get; set; }

    [Column("PHONE")]
    public string? Phone { get; set; }

    [Column("EMAIL")]
    public string? Email { get; set; }

    [Column("COLLEGE")]
    public string? College { get; set; }

    [Column("MAJOR")]
    public string? Major { get; set; }

    [Column("GRADE")]
    public string? Grade { get; set; }

    [Column("ACCOUNT_STATUS")]
    public string AccountStatus { get; set; } = "normal";

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime? UpdatedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public ICollection<ClubMember> ClubMemberships { get; set; } = new List<ClubMember>();

    public ICollection<ActivityParticipation> ActivityParticipations { get; set; } =
        new List<ActivityParticipation>();

    public ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();
}
