using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("BUDGET_TRANSACTIONS")]
public class BudgetTransaction
{
    [Column("TRANSACTION_ID")]
    public int TransactionId { get; set; }

    [Column("ACCOUNT_ID")]
    public int AccountId { get; set; }

    [Column("APPLICATION_ID")]
    public int? ApplicationId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("TRANSACTION_TYPE")]
    public string TransactionType { get; set; } = string.Empty;

    [Column("AMOUNT")]
    public decimal Amount { get; set; }

    [Column("DESCRIPTION")]
    public string? Description { get; set; }

    [Column("OCCURRED_AT")]
    public DateTime OccurredAt { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    public BudgetAccount? Account { get; set; }

    public BudgetApplication? Application { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }
}
