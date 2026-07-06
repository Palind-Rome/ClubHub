using ClubHub.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Data;

public class ClubHubDbContext : DbContext
{
    public ClubHubDbContext(DbContextOptions<ClubHubDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Activity> Activities => Set<Activity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.UserId);
            e.HasMany(u => u.UserRoles)
             .WithOne(ur => ur.User)
             .HasForeignKey(ur => ur.UserId);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.HasKey(r => r.RoleId);
            e.HasMany(r => r.UserRoles)
             .WithOne(ur => ur.Role)
             .HasForeignKey(ur => ur.RoleId);
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.HasKey(ur => ur.UserRoleId);
        });

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
