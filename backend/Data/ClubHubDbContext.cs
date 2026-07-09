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
    public DbSet<ActivityParticipation> ActivityParticipations => Set<ActivityParticipation>();
    public DbSet<ClubMember> ClubMembers => Set<ClubMember>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<LearningItem> LearningItems => Set<LearningItem>();
    public DbSet<LearningRecord> LearningRecords => Set<LearningRecord>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NoticeRead> NoticeReads => Set<NoticeRead>();
    public DbSet<Recruitment> Recruitments => Set<Recruitment>();
    public DbSet<RecruitmentApplication> RecruitmentApplications => Set<RecruitmentApplication>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<VenueReservation> VenueReservations => Set<VenueReservation>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();

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
            e.HasMany(u => u.ActivityParticipations)
             .WithOne(p => p.User)
             .HasForeignKey(p => p.UserId)
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
            e.HasMany(c => c.Recruitments)
             .WithOne(r => r.Club)
             .HasForeignKey(r => r.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasMany(c => c.LearningItems)
             .WithOne(item => item.Club)
             .HasForeignKey(item => item.ClubId)
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
            e.HasMany(a => a.Participations)
             .WithOne(p => p.Activity)
             .HasForeignKey(p => p.ActivityId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>()
             .WithMany()
             .HasForeignKey(a => a.CreatorUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>()
             .WithMany()
             .HasForeignKey(a => a.ReviewerUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>()
             .WithMany()
             .HasForeignKey(a => a.BudgetReviewerId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ActivityParticipation>(e =>
        {
            e.HasKey(p => p.ParticipationId);
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

        modelBuilder.Entity<LearningItem>(e =>
        {
            e.HasKey(item => item.ItemId);
            e.HasOne(item => item.Uploader)
             .WithMany()
             .HasForeignKey(item => item.UploaderUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(item => item.Teacher)
             .WithMany()
             .HasForeignKey(item => item.TeacherUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasMany(item => item.Records)
             .WithOne(record => record.Item)
             .HasForeignKey(record => record.ItemId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LearningRecord>(e =>
        {
            e.HasKey(record => record.RecordId);
            e.HasOne(record => record.User)
             .WithMany()
             .HasForeignKey(record => record.UserId)
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

        modelBuilder.Entity<Recruitment>(e =>
        {
            e.HasKey(r => r.RecruitId);
            e.HasMany(r => r.Applications)
             .WithOne(a => a.Recruitment)
             .HasForeignKey(a => a.RecruitId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RecruitmentApplication>(e =>
        {
            e.HasKey(a => a.ApplicationId);
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.Reviewer)
             .WithMany()
             .HasForeignKey(a => a.ReviewerUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Venue>(e =>
        {
            e.HasKey(v => v.VenueId);
            e.HasMany(v => v.Reservations)
             .WithOne(r => r.Venue)
             .HasForeignKey(r => r.VenueId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<VenueReservation>(e =>
        {
            e.HasKey(r => r.ReservationId);
            e.HasOne(r => r.Club)
             .WithMany()
             .HasForeignKey(r => r.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.Activity)
             .WithMany()
             .HasForeignKey(r => r.ActivityId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.ApplicantUser)
             .WithMany()
             .HasForeignKey(r => r.ApplicantUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.ReviewerUser)
             .WithMany()
             .HasForeignKey(r => r.ReviewerUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Evaluation>(e =>
        {
            e.HasKey(ev => ev.EvaluationId);
            e.HasOne(ev => ev.Club)
             .WithMany()
             .HasForeignKey(ev => ev.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(ev => ev.User)
             .WithMany()
             .HasForeignKey(ev => ev.UserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(ev => ev.Evaluator)
             .WithMany()
             .HasForeignKey(ev => ev.EvaluatorUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
