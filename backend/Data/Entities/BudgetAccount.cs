using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("BUDGET_ACCOUNTS")]
public class BudgetAccount
{
    [Column("ACCOUNT_ID")]
    public int AccountId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("FISCAL_YEAR")]
    public string FiscalYear { get; set; } = string.Empty;

    [Column("ACCOUNT_NAME")]
    public string AccountName { get; set; } = string.Empty;

    [Column("INITIAL_AMOUNT")]
    public decimal InitialAmount { get; set; }

    [Column("ACCOUNT_STATUS")]
    public string AccountStatus { get; set; } = "active";

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    public ICollection<BudgetApplication> Applications { get; set; } = new List<BudgetApplication>();

    public ICollection<BudgetTransaction> Transactions { get; set; } = new List<BudgetTransaction>();
}
