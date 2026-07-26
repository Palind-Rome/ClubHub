using System.Collections.Concurrent;
using System.Data.Common;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClubHub.Api.Data;

/// <summary>
/// 统一处理角色、成员任期和账号状态变化后的权限快照失效。
/// </summary>
public sealed class PermissionInvalidationInterceptor : SaveChangesInterceptor
{
    private readonly IPermissionSnapshotCache _snapshots;
    private readonly PermissionInvalidationCoordinator _coordinator;

    public PermissionInvalidationInterceptor(
        IPermissionSnapshotCache snapshots,
        PermissionInvalidationCoordinator coordinator)
    {
        _snapshots = snapshots;
        _coordinator = coordinator;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not { } context) return result;

        var changes = CollectChanges(context);
        if (changes.UserIds.Length == 0) return result;
        _coordinator.Track(context, changes.UserIds);

        if (changes.RequiresSafePreInvalidation)
        {
            foreach (var userId in changes.UserIds)
            {
                await _snapshots.InvalidateAsync(userId, true, cancellationToken);
            }
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context &&
            context.Database.CurrentTransaction is null &&
            _coordinator.TryTake(context, out var userIds))
        {
            foreach (var userId in userIds)
            {
                await _snapshots.InvalidateAsync(userId, false, cancellationToken);
            }
        }
        return result;
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            _coordinator.Clear(context);
        }
        return Task.CompletedTask;
    }

    private static PermissionChanges CollectChanges(DbContext context)
    {
        var userIds = new HashSet<int>();
        var safePreInvalidation = false;

        foreach (var entry in context.ChangeTracker.Entries<UserRole>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            userIds.Add(entry.Entity.UserId);
            safePreInvalidation |= entry.State is EntityState.Modified or EntityState.Deleted;
        }

        foreach (var entry in context.ChangeTracker.Entries<ClubMember>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            userIds.Add(entry.Entity.UserId);
            safePreInvalidation |= entry.State is EntityState.Modified or EntityState.Deleted;
        }

        foreach (var entry in context.ChangeTracker.Entries<User>()
                     .Where(entry => entry.State == EntityState.Modified &&
                                     entry.Property(user => user.AccountStatus).IsModified))
        {
            userIds.Add(entry.Entity.UserId);
            safePreInvalidation = true;
        }

        return new PermissionChanges(userIds.Where(userId => userId > 0).ToArray(), safePreInvalidation);
    }

    private sealed record PermissionChanges(int[] UserIds, bool RequiresSafePreInvalidation);
}

public sealed class PermissionTransactionInterceptor : DbTransactionInterceptor
{
    private readonly IPermissionSnapshotCache _snapshots;
    private readonly PermissionInvalidationCoordinator _coordinator;

    public PermissionTransactionInterceptor(
        IPermissionSnapshotCache snapshots,
        PermissionInvalidationCoordinator coordinator)
    {
        _snapshots = snapshots;
        _coordinator = coordinator;
    }

    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context &&
            _coordinator.TryTake(context, out var userIds))
        {
            foreach (var userId in userIds)
            {
                await _snapshots.InvalidateAsync(userId, false, cancellationToken);
            }
        }
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) _coordinator.Clear(context);
        return Task.CompletedTask;
    }

    public override Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) _coordinator.Clear(context);
        return Task.CompletedTask;
    }
}

public sealed class PermissionInvalidationCoordinator
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<int, byte>> _pending = new();

    public void Track(DbContext context, IEnumerable<int> userIds)
    {
        var users = _pending.GetOrAdd(
            context.ContextId.InstanceId,
            _ => new ConcurrentDictionary<int, byte>());
        foreach (var userId in userIds)
        {
            if (userId > 0) users.TryAdd(userId, 0);
        }
    }

    public bool TryTake(DbContext context, out int[] userIds)
    {
        if (_pending.TryRemove(context.ContextId.InstanceId, out var users))
        {
            userIds = users.Keys.ToArray();
            return userIds.Length > 0;
        }
        userIds = [];
        return false;
    }

    public void Clear(DbContext context) =>
        _pending.TryRemove(context.ContextId.InstanceId, out _);
}
