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
    public DbSet<ClubDepartment> ClubDepartments => Set<ClubDepartment>();
    public DbSet<ClubGroup> ClubGroups => Set<ClubGroup>();
    public DbSet<AwardScheme> AwardSchemes => Set<AwardScheme>();
    public DbSet<AwardLevel> AwardLevels => Set<AwardLevel>();
    public DbSet<AwardApplication> AwardApplications => Set<AwardApplication>();
    public DbSet<AwardReviewRecord> AwardReviewRecords => Set<AwardReviewRecord>();
    public DbSet<AwardAttachment> AwardAttachments => Set<AwardAttachment>();
    public DbSet<AwardPublicityBatch> AwardPublicityBatches => Set<AwardPublicityBatch>();
    public DbSet<AwardPublicityItem> AwardPublicityItems => Set<AwardPublicityItem>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<ActivityParticipation> ActivityParticipations => Set<ActivityParticipation>();
    public DbSet<ClubMember> ClubMembers => Set<ClubMember>();
    public DbSet<Recruitment> Recruitments => Set<Recruitment>();
    public DbSet<RecruitmentApplication> RecruitmentApplications => Set<RecruitmentApplication>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<ProjectTaskAssignee> ProjectTaskAssignees => Set<ProjectTaskAssignee>();
    public DbSet<ProjectTaskProgressReport> ProjectTaskProgressReports => Set<ProjectTaskProgressReport>();
    public DbSet<LearningItem> LearningItems => Set<LearningItem>();
    public DbSet<LearningRecord> LearningRecords => Set<LearningRecord>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NoticeRead> NoticeReads => Set<NoticeRead>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<VenueReservation> VenueReservations => Set<VenueReservation>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<EvaluationAwardSource> EvaluationAwardSources => Set<EvaluationAwardSource>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialBorrow> MaterialBorrows => Set<MaterialBorrow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.UserId);
            e.Property(u => u.UserId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_USERS.NEXTVAL");
            e.HasIndex(u => u.Username)
             .IsUnique()
             .HasDatabaseName("UQ_USERS_USERNAME");
            e.HasIndex(u => u.StudentNo)
             .IsUnique()
             .HasDatabaseName("UQ_USERS_STUDENT_NO");
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
            e.Property(ur => ur.UserRoleId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_USER_ROLES.NEXTVAL");
            e.HasOne(ur => ur.Club)
             .WithMany(c => c.UserRoles)
             .HasForeignKey(ur => ur.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Club>(e =>
        {
            e.HasKey(c => c.ClubId);
            e.Property(c => c.ClubId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_CLUBS.NEXTVAL");
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
            e.HasMany(c => c.Departments)
             .WithOne(d => d.Club)
             .HasForeignKey(d => d.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasMany(c => c.Groups)
             .WithOne(g => g.Club)
             .HasForeignKey(g => g.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasMany(c => c.AwardSchemes)
             .WithOne(s => s.Club)
             .HasForeignKey(s => s.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasMany(c => c.AwardApplications)
             .WithOne(a => a.Club)
             .HasForeignKey(a => a.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasMany(c => c.AwardPublicityBatches)
             .WithOne(b => b.Club)
             .HasForeignKey(b => b.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ClubDepartment>(e =>
        {
            e.HasKey(d => d.DepartmentId);
            e.HasAlternateKey(d => new { d.ClubId, d.DepartmentId })
             .HasName("UQ_CLUB_DEPARTMENTS_SCOPE");
            e.Property(d => d.DepartmentId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_CLUB_DEPARTMENTS.NEXTVAL");
            e.Property(d => d.DepartmentName).HasMaxLength(255);
            e.Property(d => d.DepartmentCode).HasMaxLength(100);
            e.Property(d => d.Description).HasColumnType("CLOB");
            e.Property(d => d.Responsibilities).HasColumnType("CLOB");
            e.Property(d => d.ContactPhone).HasMaxLength(255);
            e.Property(d => d.ContactEmail).HasMaxLength(255);
            e.Property(d => d.OfficeLocation).HasMaxLength(255);
            e.Property(d => d.DisplayOrder).HasDefaultValue(0);
            e.Property(d => d.DepartmentStatus).HasMaxLength(30).HasDefaultValue("active");
            e.Property(d => d.CreatedAt).HasDefaultValueSql("SYSDATE");
            e.Property(d => d.UpdatedAt).HasDefaultValueSql("SYSDATE");
            e.HasIndex(d => new { d.ClubId, d.DepartmentName })
             .IsUnique()
             .HasDatabaseName("UQ_CLUB_DEPARTMENTS_NAME");
            e.HasIndex(d => new { d.ClubId, d.DisplayOrder, d.DepartmentName })
             .HasDatabaseName("IX_CLUB_DEPARTMENTS_ORDER");
        });

        modelBuilder.Entity<ClubGroup>(e =>
        {
            e.HasKey(g => g.GroupId);
            e.HasAlternateKey(g => new { g.ClubId, g.GroupId })
             .HasName("UQ_CLUB_GROUPS_SCOPE");
            e.HasAlternateKey(g => new { g.ClubId, g.DepartmentId, g.GroupId })
             .HasName("UQ_CLUB_GROUPS_DEPT_SCOPE");
            e.Property(g => g.GroupId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_CLUB_GROUPS.NEXTVAL");
            e.Property(g => g.GroupName).HasMaxLength(255);
            e.Property(g => g.GroupCode).HasMaxLength(100);
            e.Property(g => g.Description).HasColumnType("CLOB");
            e.Property(g => g.Responsibilities).HasColumnType("CLOB");
            e.Property(g => g.ContactPhone).HasMaxLength(255);
            e.Property(g => g.ContactEmail).HasMaxLength(255);
            e.Property(g => g.ActivityLocation).HasMaxLength(255);
            e.Property(g => g.DisplayOrder).HasDefaultValue(0);
            e.Property(g => g.GroupStatus).HasMaxLength(30).HasDefaultValue("active");
            e.Property(g => g.CreatedAt).HasDefaultValueSql("SYSDATE");
            e.Property(g => g.UpdatedAt).HasDefaultValueSql("SYSDATE");
            e.HasIndex(g => new { g.ClubId, g.DepartmentId, g.GroupName })
             .IsUnique()
             .HasDatabaseName("UQ_CLUB_GROUPS_NAME");
            e.HasIndex(g => new { g.ClubId, g.DepartmentId, g.DisplayOrder, g.GroupName })
             .HasDatabaseName("IX_CLUB_GROUPS_ORDER");
            e.HasOne(g => g.Department)
             .WithMany(d => d.Groups)
             .HasForeignKey(g => new { g.ClubId, g.DepartmentId })
             .HasPrincipalKey(d => new { d.ClubId, d.DepartmentId })
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AwardScheme>(e =>
        {
            e.HasKey(s => s.AwardSchemeId);
            e.HasAlternateKey(s => new { s.ClubId, s.AwardSchemeId })
             .HasName("UQ_AWARD_SCHEMES_SCOPE");
            e.Property(s => s.AwardSchemeId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_AWARD_SCHEMES.NEXTVAL");
            e.Property(s => s.AwardName).HasMaxLength(255);
            e.Property(s => s.AwardCategory).HasMaxLength(100).HasDefaultValue("honor");
            e.Property(s => s.AcademicYear).HasMaxLength(50);
            e.Property(s => s.TermName).HasMaxLength(80);
            e.Property(s => s.SponsorUnit).HasMaxLength(255);
            e.Property(s => s.RewardLevel).HasMaxLength(100);
            e.Property(s => s.FundingSource).HasMaxLength(255);
            e.Property(s => s.IsRanked).HasDefaultValue(1);
            e.Property(s => s.IsFixedAmount).HasDefaultValue(1);
            e.Property(s => s.Description).HasColumnType("CLOB");
            e.Property(s => s.MaterialDescription).HasColumnType("CLOB");
            e.Property(s => s.SchemeStatus).HasMaxLength(30).HasDefaultValue("draft");
            e.Property(s => s.CreatedAt).HasDefaultValueSql("SYSDATE");
            e.Property(s => s.UpdatedAt).HasDefaultValueSql("SYSDATE");
            e.HasIndex(s => new { s.ClubId, s.SchemeStatus, s.ApplicationStartAt })
             .HasDatabaseName("IX_AWARD_SCHEMES_CLUB_STATUS");
            e.HasOne(s => s.CreatedByUser)
             .WithMany()
             .HasForeignKey(s => s.CreatedByUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AwardLevel>(e =>
        {
            e.HasKey(l => l.AwardLevelId);
            e.HasAlternateKey(l => new { l.AwardSchemeId, l.AwardLevelId })
             .HasName("UQ_AWARD_LEVELS_SCOPE");
            e.Property(l => l.AwardLevelId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_AWARD_LEVELS.NEXTVAL");
            e.Property(l => l.LevelName).HasMaxLength(255);
            e.Property(l => l.AwardScore).HasDefaultValue(0);
            e.Property(l => l.DisplayOrder).HasDefaultValue(0);
            e.Property(l => l.LevelStatus).HasMaxLength(30).HasDefaultValue("active");
            e.Property(l => l.CreatedAt).HasDefaultValueSql("SYSDATE");
            e.Property(l => l.UpdatedAt).HasDefaultValueSql("SYSDATE");
            e.HasIndex(l => new { l.AwardSchemeId, l.LevelName })
             .IsUnique()
             .HasDatabaseName("UQ_AWARD_LEVELS_NAME");
            e.HasIndex(l => new { l.AwardSchemeId, l.DisplayOrder, l.LevelName })
             .HasDatabaseName("IX_AWARD_LEVELS_ORDER");
            e.HasOne(l => l.Scheme)
             .WithMany(s => s.Levels)
             .HasForeignKey(l => l.AwardSchemeId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AwardApplication>(e =>
        {
            e.HasKey(a => a.AwardApplicationId);
            e.HasAlternateKey(a => new { a.ClubId, a.AwardApplicationId })
             .HasName("UQ_AWARD_APPLICATIONS_SCOPE");
            e.Property(a => a.AwardApplicationId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_AWARD_APPLICATIONS.NEXTVAL");
            e.Property(a => a.ApplicationType).HasMaxLength(30).HasDefaultValue("self");
            e.Property(a => a.ApplicationReason).HasColumnType("CLOB");
            e.Property(a => a.MaterialUrl).HasMaxLength(1000);
            e.Property(a => a.CurrentStep).HasMaxLength(30).HasDefaultValue("student_submit");
            e.Property(a => a.ApplicationStatus).HasMaxLength(30).HasDefaultValue("draft");
            e.Property(a => a.PublicStatus).HasMaxLength(30).HasDefaultValue("none");
            e.Property(a => a.ReviewRound).HasDefaultValue(1);
            e.Property(a => a.CreatedAt).HasDefaultValueSql("SYSDATE");
            e.Property(a => a.UpdatedAt).HasDefaultValueSql("SYSDATE");
            e.HasIndex(a => new { a.AwardSchemeId, a.ApplicantUserId })
             .IsUnique()
             .HasDatabaseName("UQ_AWARD_APPLICATIONS_APPLICANT");
            e.HasIndex(a => new { a.ClubId, a.ApplicationStatus, a.CurrentStep })
             .HasDatabaseName("IX_AWARD_APPLICATIONS_STATUS");
            e.HasIndex(a => new { a.ApplicantUserId, a.ClubId, a.AwardSchemeId })
             .HasDatabaseName("IX_AWARD_APPLICATIONS_USER");
            e.HasOne(a => a.Scheme)
             .WithMany(s => s.Applications)
             .HasForeignKey(a => new { a.ClubId, a.AwardSchemeId })
             .HasPrincipalKey(s => new { s.ClubId, s.AwardSchemeId })
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.Level)
             .WithMany(l => l.Applications)
             .HasForeignKey(a => new { a.AwardSchemeId, a.AwardLevelId })
             .HasPrincipalKey(l => new { l.AwardSchemeId, l.AwardLevelId })
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.Applicant)
             .WithMany()
             .HasForeignKey(a => a.ApplicantUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.Recommender)
             .WithMany()
             .HasForeignKey(a => a.RecommenderUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.Submitter)
             .WithMany()
             .HasForeignKey(a => a.SubmitterUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AwardReviewRecord>(e =>
        {
            e.HasKey(r => r.ReviewId);
            e.Property(r => r.ReviewId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_AWARD_REVIEW_RECORDS.NEXTVAL");
            e.Property(r => r.ReviewRound).HasDefaultValue(1);
            e.Property(r => r.ReviewStep).HasMaxLength(30);
            e.Property(r => r.ReviewResult).HasMaxLength(30);
            e.Property(r => r.ReviewComment).HasColumnType("CLOB");
            e.Property(r => r.FromStatus).HasMaxLength(30);
            e.Property(r => r.ToStatus).HasMaxLength(30);
            e.Property(r => r.ReviewedAt).HasDefaultValueSql("SYSDATE");
            e.HasIndex(r => new { r.AwardApplicationId, r.ReviewRound, r.ReviewedAt })
             .HasDatabaseName("IX_AWARD_REVIEWS_APPLICATION");
            e.HasOne(r => r.Application)
             .WithMany(a => a.ReviewRecords)
             .HasForeignKey(r => r.AwardApplicationId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.Reviewer)
             .WithMany()
             .HasForeignKey(r => r.ReviewerUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AwardAttachment>(e =>
        {
            e.HasKey(a => a.AttachmentId);
            e.Property(a => a.AttachmentId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_AWARD_ATTACHMENTS.NEXTVAL");
            e.Property(a => a.AttachmentName).HasMaxLength(255);
            e.Property(a => a.AttachmentUrl).HasMaxLength(1000);
            e.Property(a => a.AttachmentType).HasMaxLength(100);
            e.Property(a => a.UploadedAt).HasDefaultValueSql("SYSDATE");
            e.HasIndex(a => new { a.AwardApplicationId, a.UploadedAt })
             .HasDatabaseName("IX_AWARD_ATTACHMENTS_APP");
            e.HasOne(a => a.Application)
             .WithMany(application => application.Attachments)
             .HasForeignKey(a => a.AwardApplicationId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.UploadedByUser)
             .WithMany()
             .HasForeignKey(a => a.UploadedByUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AwardPublicityBatch>(e =>
        {
            e.HasKey(b => b.PublicityBatchId);
            e.HasAlternateKey(b => new { b.ClubId, b.PublicityBatchId })
             .HasName("UQ_AWARD_PUBLICITY_BATCH_SCOPE");
            e.Property(b => b.PublicityBatchId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_AWARD_PUBLICITY_BATCHES.NEXTVAL");
            e.Property(b => b.Title).HasMaxLength(255);
            e.Property(b => b.Description).HasColumnType("CLOB");
            e.Property(b => b.PublicityStatus).HasMaxLength(30).HasDefaultValue("draft");
            e.Property(b => b.CreatedAt).HasDefaultValueSql("SYSDATE");
            e.Property(b => b.UpdatedAt).HasDefaultValueSql("SYSDATE");
            e.HasIndex(b => new { b.ClubId, b.PublicityStatus, b.PublicityStartAt })
             .HasDatabaseName("IX_AWARD_PUBLICITY_CLUB");
            e.HasOne(b => b.Publisher)
             .WithMany()
             .HasForeignKey(b => b.PublisherUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AwardPublicityItem>(e =>
        {
            e.HasKey(i => i.PublicityItemId);
            e.Property(i => i.PublicityItemId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_AWARD_PUBLICITY_ITEMS.NEXTVAL");
            e.Property(i => i.DisplayOrder).HasDefaultValue(0);
            e.Property(i => i.PublicityResult).HasMaxLength(30).HasDefaultValue("normal");
            e.Property(i => i.CreatedAt).HasDefaultValueSql("SYSDATE");
            e.HasIndex(i => new { i.PublicityBatchId, i.AwardApplicationId })
             .IsUnique()
             .HasDatabaseName("UQ_AWARD_PUBLICITY_ITEMS_APP");
            e.HasIndex(i => new { i.PublicityBatchId, i.DisplayOrder })
             .HasDatabaseName("IX_AWARD_PUBLICITY_ITEMS_ORDER");
            e.HasOne(i => i.Batch)
             .WithMany(b => b.Items)
             .HasForeignKey(i => new { i.ClubId, i.PublicityBatchId })
             .HasPrincipalKey(b => new { b.ClubId, b.PublicityBatchId })
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(i => i.Application)
             .WithMany(a => a.PublicityItems)
             .HasForeignKey(i => new { i.ClubId, i.AwardApplicationId })
             .HasPrincipalKey(a => new { a.ClubId, a.AwardApplicationId })
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Activity>(e =>
        {
            e.HasKey(a => a.ActivityId);
            e.Property(a => a.ActivityId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_ACTIVITIES.NEXTVAL");
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
            e.Property(p => p.ParticipationId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_ACTIVITY_PARTICIPATIONS.NEXTVAL");
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

        modelBuilder.Entity<ClubMember>(e =>
        {
            e.HasKey(cm => cm.MemberId);
            e.Property(cm => cm.MemberId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_CLUB_MEMBERS.NEXTVAL");
            e.HasOne(cm => cm.Club)
             .WithMany(c => c.Members)
             .HasForeignKey(cm => cm.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(cm => cm.Department)
             .WithMany(d => d.Members)
             .HasForeignKey(cm => new { cm.ClubId, cm.DepartmentId })
             .HasPrincipalKey(d => new { d.ClubId, d.DepartmentId })
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(cm => cm.Group)
             .WithMany(g => g.Members)
             .HasForeignKey(cm => new { cm.ClubId, cm.DepartmentId, cm.GroupId })
             .HasPrincipalKey(g => new { g.ClubId, g.DepartmentId, g.GroupId })
             .OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(cm => new { cm.ClubId, cm.DepartmentId, cm.GroupId })
             .HasDatabaseName("IX_CLUB_MEMBERS_ORG");
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

        modelBuilder.Entity<ProjectMember>(e =>
        {
            e.HasKey(pm => pm.ProjectMemberId);
            e.HasIndex(pm => new { pm.ProjectId, pm.UserId })
             .IsUnique()
             .HasDatabaseName("UQ_PROJECT_MEMBERS_USER");
            e.Property(pm => pm.MemberRole)
             .HasMaxLength(30)
             .HasDefaultValue("member");
            e.Property(pm => pm.MemberStatus)
             .HasMaxLength(30)
             .HasDefaultValue("active");
            e.Property(pm => pm.Remark)
             .HasMaxLength(255);
            e.HasOne(pm => pm.Project)
             .WithMany(p => p.Members)
             .HasForeignKey(pm => pm.ProjectId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(pm => pm.User)
             .WithMany(u => u.ProjectMemberships)
             .HasForeignKey(pm => pm.UserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ProjectTask>(e =>
        {
            e.HasKey(t => t.TaskId);
            e.Property(t => t.Title).HasMaxLength(255);
            e.Property(t => t.Priority).HasMaxLength(255);
            e.Property(t => t.TaskStatus).HasMaxLength(255);
            e.Property(t => t.DelayReason).HasMaxLength(255);
            e.Property(t => t.DeliverableTitle).HasMaxLength(255);
            e.Property(t => t.DeliverableUrl).HasMaxLength(255);
            e.Property(t => t.DeliverableStatus).HasMaxLength(255);
            e.Property(t => t.ReviewComment).HasMaxLength(255);
            e.HasOne(t => t.Project)
             .WithMany(p => p.Tasks)
             .HasForeignKey(t => t.ProjectId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.AssigneeUser)
             .WithMany()
             .HasForeignKey(t => t.AssigneeUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.ReviewerUser)
             .WithMany()
             .HasForeignKey(t => t.ReviewerUserId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.DeliverableSubmitter)
             .WithMany()
             .HasForeignKey(t => t.DeliverableSubmitterId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ProjectTaskAssignee>(e =>
        {
            e.HasKey(item => item.TaskAssigneeId);
            e.HasIndex(item => new { item.TaskId, item.UserId })
             .IsUnique()
             .HasDatabaseName("UQ_PROJECT_TASK_ASSIGNEES");
            e.HasIndex(item => new { item.UserId, item.TaskId })
             .HasDatabaseName("IX_PROJECT_TASK_ASSIGNEES_USER");
            e.HasOne(item => item.Task)
             .WithMany(task => task.Assignees)
             .HasForeignKey(item => item.TaskId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(item => item.User)
             .WithMany()
             .HasForeignKey(item => item.UserId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ProjectTaskProgressReport>(e =>
        {
            e.HasKey(item => item.TaskProgressReportId);
            e.Property(item => item.TaskStatus).HasMaxLength(30);
            e.Property(item => item.ReportContent).HasMaxLength(1000);
            e.Property(item => item.DelayReason).HasMaxLength(255);
            e.HasIndex(item => new { item.TaskId, item.SubmittedAt })
                .HasDatabaseName("IX_PT_PROGRESS_REPORTS_TASK");
            e.HasOne(item => item.Task)
                .WithMany(item => item.ProgressReports)
                .HasForeignKey(item => item.TaskId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.Reporter)
                .WithMany()
                .HasForeignKey(item => item.ReporterUserId)
                .OnDelete(DeleteBehavior.Restrict);
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

        modelBuilder.Entity<OperationLog>(e =>
        {
            e.HasKey(log => log.LogId);
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
            e.Property(r => r.ReadId)
             .HasDefaultValueSql("SEQ_NOTICE_READS.NEXTVAL")
             .ValueGeneratedOnAdd();
            e.HasIndex(r => new { r.NoticeId, r.UserId })
             .IsUnique()
             .HasDatabaseName("UQ_NOTICE_READS_NOTICE_USER");
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
            e.Property(ev => ev.EvaluationId)
             .ValueGeneratedOnAdd()
             .HasDefaultValueSql("SEQ_EVALUATIONS.NEXTVAL");
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
            e.HasMany(ev => ev.AwardSources)
             .WithOne(source => source.Evaluation)
             .HasForeignKey(source => source.EvaluationId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<EvaluationAwardSource>(e =>
        {
            e.HasKey(source => new { source.EvaluationId, source.AwardApplicationId });
            e.Property(source => source.AwardScore).HasDefaultValue(0);
            e.Property(source => source.CreatedAt).HasDefaultValueSql("SYSDATE");
            e.HasOne(source => source.Application)
             .WithMany(application => application.EvaluationSources)
             .HasForeignKey(source => source.AwardApplicationId)
             .OnDelete(DeleteBehavior.NoAction);
        });


        modelBuilder.Entity<Material>(e =>
        {
            e.HasKey(m => m.MaterialId);
            e.HasOne(m => m.Club)
             .WithMany()
             .HasForeignKey(m => m.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasMany(m => m.Borrows)
             .WithOne(b => b.Material)
             .HasForeignKey(b => b.MaterialId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<MaterialBorrow>(e =>
        {
            e.HasKey(b => b.BorrowId);
            e.HasOne(b => b.Club)
             .WithMany()
             .HasForeignKey(b => b.ClubId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(b => b.BorrowerUser)
             .WithMany()
             .HasForeignKey(b => b.BorrowerUserId)
             .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
