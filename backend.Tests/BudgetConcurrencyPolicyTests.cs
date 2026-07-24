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
}
