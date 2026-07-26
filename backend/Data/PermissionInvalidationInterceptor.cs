using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<Guid, int[]> _pending = new();

    public PermissionInvalidationInterceptor(IPermissionSnapshotCache snapshots) =>
        _snapshots = snapshots;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not { } context) return result;

        var changes = CollectChanges(context);
        if (changes.UserIds.Length == 0) return result;
        _pending[context.ContextId.InstanceId] = changes.UserIds;

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
            _pending.TryRemove(context.ContextId.InstanceId, out var userIds))
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
            _pending.TryRemove(context.ContextId.InstanceId, out _);
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
