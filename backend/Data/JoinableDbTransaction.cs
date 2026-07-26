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
    private readonly IDbContextTransaction _transaction;
    private readonly bool _ownsTransaction;
    private readonly string? _savepoint;
    private bool _completed;

    internal JoinableDbTransaction(
        IDbContextTransaction transaction,
        bool ownsTransaction,
        string? savepoint)
    {
        _transaction = transaction;
        _ownsTransaction = ownsTransaction;
        _savepoint = savepoint;
    }

    public bool OwnsTransaction => _ownsTransaction;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) return;
        if (_ownsTransaction)
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        else if (_savepoint is not null)
        {
            await _transaction.ReleaseSavepointAsync(_savepoint, cancellationToken);
        }
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) return;
        if (_ownsTransaction)
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        else if (_savepoint is not null)
        {
            await _transaction.RollbackToSavepointAsync(_savepoint, cancellationToken);
            await _transaction.ReleaseSavepointAsync(_savepoint, cancellationToken);
        }
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsTransaction)
        {
            await _transaction.DisposeAsync();
            return;
        }

        if (!_completed && _savepoint is not null)
        {
            await _transaction.RollbackToSavepointAsync(_savepoint);
            await _transaction.ReleaseSavepointAsync(_savepoint);
            _completed = true;
        }
    }
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
            var savepoint = $"CH_{Guid.NewGuid():N}"[..27];
            await database.CurrentTransaction.CreateSavepointAsync(
                savepoint,
                cancellationToken);
            return new JoinableDbTransaction(
                database.CurrentTransaction,
                ownsTransaction: false,
                savepoint);
        }

        var transaction = isolationLevel is null
            ? await database.BeginTransactionAsync(cancellationToken)
            : await database.BeginTransactionAsync(isolationLevel.Value, cancellationToken);
        return new JoinableDbTransaction(
            transaction,
            ownsTransaction: true,
            savepoint: null);
    }
}
