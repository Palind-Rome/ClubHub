using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClubHub.Api.Data;

/// <summary>
/// Owns a database transaction only when the current request has not already
/// opened one. This lets endpoint-level concurrency protection join the outer
/// idempotency transaction without nesting or committing it prematurely.
/// </summary>
public sealed class JoinableDbTransaction : IAsyncDisposable
{
    private readonly IDbContextTransaction? _ownedTransaction;

    internal JoinableDbTransaction(IDbContextTransaction? ownedTransaction)
    {
        _ownedTransaction = ownedTransaction;
    }

    public bool OwnsTransaction => _ownedTransaction is not null;

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        _ownedTransaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        _ownedTransaction?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() =>
        _ownedTransaction?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public static class JoinableDbTransactionExtensions
{
    public static Task<JoinableDbTransaction> BeginJoinableTransactionAsync(
        this DatabaseFacade database,
        CancellationToken cancellationToken = default) =>
        BeginCoreAsync(database, null, cancellationToken);

    public static async Task<JoinableDbTransaction> BeginJoinableTransactionAsync(
        this DatabaseFacade database,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default) =>
        await BeginCoreAsync(database, isolationLevel, cancellationToken);

    private static async Task<JoinableDbTransaction> BeginCoreAsync(
        DatabaseFacade database,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken)
    {
        if (database.CurrentTransaction is not null)
        {
            return new JoinableDbTransaction(null);
        }

        var transaction = isolationLevel is null
            ? await database.BeginTransactionAsync(cancellationToken)
            : await database.BeginTransactionAsync(isolationLevel.Value, cancellationToken);
        return new JoinableDbTransaction(transaction);
    }
}
