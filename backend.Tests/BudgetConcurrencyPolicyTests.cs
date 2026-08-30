using System.Data;
using ClubHub.Api.Controllers;

namespace ClubHub.Api.Tests;

public sealed class BudgetConcurrencyPolicyTests
{
    [Fact]
    public void ApprovalBalanceTransactionsUseReadCommittedSnapshot()
    {
        Assert.Equal(IsolationLevel.ReadCommitted, BudgetController.BudgetApprovalIsolationLevel);
    }

    [Fact]
    public void RowLockQueriesRemainUncomposedByEfPagination()
    {
        var queries = new[]
        {
            BudgetController.BudgetApplicationRowLockSql,
            BudgetController.BudgetAccountRowLockSql,
            BudgetController.BudgetClubAccountRowLockSql
        };

        foreach (var query in queries)
        {
            Assert.Contains("FOR UPDATE", query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FETCH FIRST", query, StringComparison.OrdinalIgnoreCase);
        }
    }
}
