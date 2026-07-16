using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("MATERIAL_BORROWS")]
public class MaterialBorrow
{
    [Column("BORROW_ID")]
    public int BorrowId { get; set; }

    [Column("MATERIAL_ID")]
    public int MaterialId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("BORROWER_USER_ID")]
    public int BorrowerUserId { get; set; }

    [Column("QUANTITY")]
    public int? Quantity { get; set; }

    [Column("BORROW_AT")]
    public DateTime? BorrowAt { get; set; }

    [Column("EXPECTED_RETURN_AT")]
    public DateTime? ExpectedReturnAt { get; set; }

    [Column("RETURN_AT")]
    public DateTime? ReturnAt { get; set; }

    [Column("BORROW_STATUS")]
    public string? BorrowStatus { get; set; }

    [Column("DAMAGE_DESC")]
    public string? DamageDesc { get; set; }

    [Column("COMPENSATION_AMOUNT")]
    public decimal? CompensationAmount { get; set; }

    [ForeignKey(nameof(MaterialId))]
    public Material? Material { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [ForeignKey(nameof(BorrowerUserId))]
    public User? BorrowerUser { get; set; }
}
