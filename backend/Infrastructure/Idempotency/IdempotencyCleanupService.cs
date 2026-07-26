using ClubHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Infrastructure.Idempotency;

public sealed class IdempotencyCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public IdempotencyCleanupService(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            var expired = await db.IdempotencyRecords
                .Where(record => record.ExpiresAt <= DateTime.UtcNow)
                .OrderBy(record => record.IdempotencyId)
                .Take(500)
                .ToListAsync(stoppingToken);
            if (expired.Count == 0) continue;
            db.IdempotencyRecords.RemoveRange(expired);
            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
