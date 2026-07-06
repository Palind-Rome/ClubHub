using ClubHub.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Data;

public class ClubHubDbContext : DbContext
{
    public ClubHubDbContext(DbContextOptions<ClubHubDbContext> options) : base(options) { }

    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Activity> Activities => Set<Activity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Club>(e =>
        {
            e.HasKey(c => c.ClubId);
            e.HasMany(c => c.Activities)
             .WithOne(a => a.Club)
             .HasForeignKey(a => a.ClubId);
        });

        modelBuilder.Entity<Activity>(e =>
        {
            e.HasKey(a => a.ActivityId);
        });
    }
}
