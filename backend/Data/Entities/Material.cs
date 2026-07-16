using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("MATERIALS")]
public class Material
{
    [Column("MATERIAL_ID")]
    public int MaterialId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("MATERIAL_NAME")]
    public string? MaterialName { get; set; }

    [Column("SPECIFICATION")]
    public string? Specification { get; set; }

    [Column("TOTAL_QTY")]
    public int? TotalQty { get; set; }

    [Column("AVAILABLE_QTY")]
    public int? AvailableQty { get; set; }

    [Column("STORAGE_LOCATION")]
    public string? StorageLocation { get; set; }

    [Column("MATERIAL_STATUS")]
    public string? MaterialStatus { get; set; }

    [Column("CREATED_AT")]
    public DateTime? CreatedAt { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    public ICollection<MaterialBorrow> Borrows { get; set; } = new List<MaterialBorrow>();
}
