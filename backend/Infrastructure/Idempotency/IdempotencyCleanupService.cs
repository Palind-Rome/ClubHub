using ClubHub.Api.Data;
using ClubHub.Api.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClubHub.Api.Infrastructure.Idempotency;

public sealed class IdempotencyCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _enabled;

    public IdempotencyCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<RedisOptions> options)
    {
        _scopeFactory = scopeFactory;
        _enabled = options.Value.Enabled && options.Value.Features.Idempotency;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) return;

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
                var expired = await db.IdempotencyRecords
                    .Where(record => record.ExpiresAt <= DateTime.UtcNow)
                    .OrderBy(record => record.IdempotencyId)
                    .Take(500)
                    .ToListAsync(stoppingToken);
                if (expired.Count == 0) break;
                db.IdempotencyRecords.RemoveRange(expired);
                await db.SaveChangesAsync(stoppingToken);
                if (expired.Count < 500) break;
            }
        }
    }
}
