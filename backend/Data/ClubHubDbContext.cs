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
    public DbSet<ClubMember> ClubMembers => Set<ClubMember>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NoticeRead> NoticeReads => Set<NoticeRead>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.UserId);
            e.HasMany(u => u.UserRoles)
             .WithOne(ur => ur.User)
             .HasForeignKey(ur => ur.UserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasMany(u => u.ClubMemberships)
             .WithOne(cm => cm.User)
             .HasForeignKey(cm => cm.UserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.HasKey(r => r.RoleId);
            e.HasMany(r => r.UserRoles)
             .WithOne(ur => ur.Role)
             .HasForeignKey(ur => ur.RoleId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.HasKey(ur => ur.UserRoleId);
            e.HasOne(ur => ur.Club)
             .WithMany(c => c.UserRoles)
             .HasForeignKey(ur => ur.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Club>(e =>
        {
            e.HasKey(c => c.ClubId);
            e.HasMany(c => c.Activities)
             .WithOne(a => a.Club)
             .HasForeignKey(a => a.ClubId);
            e.HasMany(c => c.Notices)
             .WithOne(n => n.Club)
             .HasForeignKey(n => n.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(c => c.Applicant)
             .WithMany()
             .HasForeignKey(c => c.ApplicantUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(c => c.Reviewer)
             .WithMany()
             .HasForeignKey(c => c.ReviewerUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(c => c.President)
             .WithMany()
             .HasForeignKey(c => c.PresidentUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Activity>(e =>
        {
            e.HasKey(a => a.ActivityId);
        });

        modelBuilder.Entity<ClubMember>(e =>
        {
            e.HasKey(cm => cm.MemberId);
            e.HasOne(cm => cm.Club)
             .WithMany(c => c.Members)
             .HasForeignKey(cm => cm.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.HasKey(p => p.ProjectId);
            e.HasOne(p => p.Club)
             .WithMany()
             .HasForeignKey(p => p.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>()
             .WithMany()
             .HasForeignKey(p => p.LeaderUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Notice>(e =>
        {
            e.HasKey(n => n.NoticeId);
            e.HasOne(n => n.Publisher)
             .WithMany()
             .HasForeignKey(n => n.PublisherUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasMany(n => n.Reads)
             .WithOne(r => r.Notice)
             .HasForeignKey(r => r.NoticeId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<NoticeRead>(e =>
        {
            e.HasKey(r => r.ReadId);
            e.HasOne(r => r.User)
             .WithMany()
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
