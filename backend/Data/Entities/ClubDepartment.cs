using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("CLUB_DEPARTMENTS")]
public class ClubDepartment
{
    [Column("DEPARTMENT_ID")]
    public int DepartmentId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("DEPARTMENT_NAME")]
    public string DepartmentName { get; set; } = string.Empty;

    [Column("DEPARTMENT_CODE")]
    public string? DepartmentCode { get; set; }

    [Column("DESCRIPTION")]
    public string? Description { get; set; }

    [Column("RESPONSIBILITIES")]
    public string? Responsibilities { get; set; }

    [Column("CONTACT_PHONE")]
    public string? ContactPhone { get; set; }

    [Column("CONTACT_EMAIL")]
    public string? ContactEmail { get; set; }

    [Column("OFFICE_LOCATION")]
    public string? OfficeLocation { get; set; }

    [Column("DISPLAY_ORDER")]
    public int DisplayOrder { get; set; }

    [Column("DEPARTMENT_STATUS")]
    public string DepartmentStatus { get; set; } = "active";

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    public Club? Club { get; set; }

    public ICollection<ClubGroup> Groups { get; set; } = new List<ClubGroup>();

    public ICollection<ClubMember> Members { get; set; } = new List<ClubMember>();
}
