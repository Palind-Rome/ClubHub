using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("CLUB_GROUPS")]
public class ClubGroup
{
    [Column("GROUP_ID")]
    public int GroupId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("DEPARTMENT_ID")]
    public int DepartmentId { get; set; }

    [Column("GROUP_NAME")]
    public string GroupName { get; set; } = string.Empty;

    [Column("GROUP_CODE")]
    public string? GroupCode { get; set; }

    [Column("DESCRIPTION")]
    public string? Description { get; set; }

    [Column("RESPONSIBILITIES")]
    public string? Responsibilities { get; set; }

    [Column("CONTACT_PHONE")]
    public string? ContactPhone { get; set; }

    [Column("CONTACT_EMAIL")]
    public string? ContactEmail { get; set; }

    [Column("ACTIVITY_LOCATION")]
    public string? ActivityLocation { get; set; }

    [Column("DISPLAY_ORDER")]
    public int DisplayOrder { get; set; }

    [Column("GROUP_STATUS")]
    public string GroupStatus { get; set; } = "active";

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    public Club? Club { get; set; }

    public ClubDepartment? Department { get; set; }

    public ICollection<ClubMember> Members { get; set; } = new List<ClubMember>();
}
