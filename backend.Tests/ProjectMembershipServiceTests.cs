using System.Net.Http.Headers;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Identity;
using ClubHub.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class ProjectMembershipServiceTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;
    private static int _sequence;

    public ProjectMembershipServiceTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    [InlineData("Active")]
    public async Task IsActiveUserAsync_WithVariousAccountStatuses_ReturnsCorrectly(string? accountStatus)
    {
        var baseId = 1000 + Interlocked.Increment(ref _sequence) * 10;
        var userId = baseId;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        db.Add(new User
        {
            UserId = userId,
            Username = $"user-{baseId}",
            PasswordHash = "unused",
            RealName = "Test User",
            AccountStatus = accountStatus,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var isActive = await service.IsActiveUserAsync(userId);
        Assert.True(isActive, $"User with AccountStatus='{accountStatus}' should be considered active");
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("suspended")]
    [InlineData("INACTIVE")]
    [InlineData("deleted")]
    public async Task IsActiveUserAsync_WithInactiveStatus_ReturnsFalse(string inactiveStatus)
    {
        var baseId = 1000 + Interlocked.Increment(ref _sequence) * 10;
        var userId = baseId;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        db.Add(new User
        {
            UserId = userId,
            Username = $"user-{baseId}",
            PasswordHash = "unused",
            RealName = "Test User",
            AccountStatus = inactiveStatus,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var isActive = await service.IsActiveUserAsync(userId);
        Assert.False(isActive, $"User with AccountStatus='{inactiveStatus}' should not be active");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    public async Task IsActiveClubMemberAsync_WithVariousMemberStatuses_ReturnsCorrectly(string? memberStatus)
    {
        var baseId = 2000 + Interlocked.Increment(ref _sequence) * 10;
        var clubId = baseId;
        var userId = baseId + 1;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        db.Add(new User { UserId = userId, Username = $"user-{baseId}", PasswordHash = "unused", RealName = "Test", AccountStatus = "active", CreatedAt = now });
        db.Add(new Club { ClubId = clubId, ClubName = "Test Club", ClubStatus = "active", CreatedAt = now });
        db.Add(new ClubMember
        {
            MemberId = baseId + 2,
            ClubId = clubId,
            UserId = userId,
            MemberStatus = memberStatus,
            TermStart = now.AddMonths(-1),
            TermEnd = now.AddMonths(1)
        });
        await db.SaveChangesAsync();

        var isActive = await service.IsActiveClubMemberAsync(clubId, userId);
        Assert.True(isActive, $"Member with MemberStatus='{memberStatus}' within term should be active");
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("quit")]
    [InlineData("removed")]
    [InlineData("INACTIVE")]
    public async Task IsActiveClubMemberAsync_WithInactiveStatus_ReturnsFalse(string inactiveStatus)
    {
        var baseId = 2000 + Interlocked.Increment(ref _sequence) * 10;
        var clubId = baseId;
        var userId = baseId + 1;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        db.Add(new User { UserId = userId, Username = $"user-{baseId}", PasswordHash = "unused", RealName = "Test", AccountStatus = "active", CreatedAt = now });
        db.Add(new Club { ClubId = clubId, ClubName = "Test Club", ClubStatus = "active", CreatedAt = now });
        db.Add(new ClubMember
        {
            MemberId = baseId + 2,
            ClubId = clubId,
            UserId = userId,
            MemberStatus = inactiveStatus,
            TermStart = now.AddMonths(-1),
            TermEnd = now.AddMonths(1)
        });
        await db.SaveChangesAsync();

        var isActive = await service.IsActiveClubMemberAsync(clubId, userId);
        Assert.False(isActive, $"Member with MemberStatus='{inactiveStatus}' should not be active");
    }

    [Fact]
    public async Task IsActiveClubMemberAsync_BeforeTermStart_ReturnsFalse()
    {
        var baseId = 2100 + Interlocked.Increment(ref _sequence) * 10;
        var clubId = baseId;
        var userId = baseId + 1;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        db.Add(new User { UserId = userId, Username = $"user-{baseId}", PasswordHash = "unused", RealName = "Test", AccountStatus = "active", CreatedAt = now });
        db.Add(new Club { ClubId = clubId, ClubName = "Test Club", ClubStatus = "active", CreatedAt = now });
        db.Add(new ClubMember
        {
            MemberId = baseId + 2,
            ClubId = clubId,
            UserId = userId,
            MemberStatus = "active",
            TermStart = now.AddMonths(1),
            TermEnd = now.AddMonths(2)
        });
        await db.SaveChangesAsync();

        var isActive = await service.IsActiveClubMemberAsync(clubId, userId);
        Assert.False(isActive, "Member before term start should not be active");
    }

    [Fact]
    public async Task IsActiveClubMemberAsync_AfterTermEnd_ReturnsFalse()
    {
        var baseId = 2200 + Interlocked.Increment(ref _sequence) * 10;
        var clubId = baseId;
        var userId = baseId + 1;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        db.Add(new User { UserId = userId, Username = $"user-{baseId}", PasswordHash = "unused", RealName = "Test", AccountStatus = "active", CreatedAt = now });
        db.Add(new Club { ClubId = clubId, ClubName = "Test Club", ClubStatus = "active", CreatedAt = now });
        db.Add(new ClubMember
        {
            MemberId = baseId + 2,
            ClubId = clubId,
            UserId = userId,
            MemberStatus = "active",
            TermStart = now.AddMonths(-2),
            TermEnd = now.AddMonths(-1)
        });
        await db.SaveChangesAsync();

        var isActive = await service.IsActiveClubMemberAsync(clubId, userId);
        Assert.False(isActive, "Member after term end should not be active");
    }

    [Fact]
    public async Task IsActiveClubMemberAsync_OnTermBoundaryDates_ReturnsCorrectly()
    {
        var baseId = 2300 + Interlocked.Increment(ref _sequence) * 10;
        var clubId = baseId;
        var userId = baseId + 1;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var businessDate = DateTime.UtcNow.Date;

        db.Add(new User { UserId = userId, Username = $"user-{baseId}", PasswordHash = "unused", RealName = "Test", AccountStatus = "active", CreatedAt = businessDate });
        db.Add(new Club { ClubId = clubId, ClubName = "Test Club", ClubStatus = "active", CreatedAt = businessDate });
        db.Add(new ClubMember
        {
            MemberId = baseId + 2,
            ClubId = clubId,
            UserId = userId,
            MemberStatus = "active",
            TermStart = businessDate,
            TermEnd = businessDate
        });
        await db.SaveChangesAsync();

        var isActive = await service.IsActiveClubMemberAsync(clubId, userId);
        Assert.True(isActive, "Member on exact term start and end dates should be active");
    }

    [Fact]
    public async Task GetCandidateUsersQuery_ReturnsCorrectOrdering()
    {
        var baseId = 3000 + Interlocked.Increment(ref _sequence) * 10;
        var clubId = baseId;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        var project = new Project
        {
            ProjectId = baseId,
            ProjectName = "Test Project",
            ClubId = clubId,
            LeaderUserId = baseId + 10,
            ProjectStatus = "active",
            CreatedAt = now
        };

        db.Add(new Club { ClubId = clubId, ClubName = "Test Club", ClubStatus = "active", CreatedAt = now });
        db.Add(project);

        // Add candidate users in non-alphabetical order
        var users = new[]
        {
            ("Zebra Zhang", "00002", baseId + 1),
            ("Alice Wang", "00001", baseId + 2),
            ("Bob Liu", null, baseId + 3)
        };

        foreach (var (realName, studentNo, userId) in users)
        {
            db.Add(new User
            {
                UserId = userId,
                Username = $"user-{userId}",
                PasswordHash = "unused",
                RealName = realName,
                StudentNo = studentNo,
                AccountStatus = "active",
                CreatedAt = now
            });
            db.Add(new ClubMember
            {
                MemberId = userId + 100,
                ClubId = clubId,
                UserId = userId,
                MemberStatus = "active",
                TermStart = now.AddMonths(-1),
                TermEnd = now.AddMonths(1)
            });
        }
        await db.SaveChangesAsync();

        var candidates = await service.GetCandidateUsersQuery(project).ToListAsync();

        // Verify ordering: RealName, then StudentNo, then UserId
        Assert.Equal(3, candidates.Count);
        Assert.Equal("Alice Wang", candidates[0].RealName);
        Assert.Equal("Bob Liu", candidates[1].RealName);
        Assert.Equal("Zebra Zhang", candidates[2].RealName);
    }

    [Fact]
    public async Task GetCandidateUsersQuery_ExcludesInactiveUsers()
    {
        var baseId = 3100 + Interlocked.Increment(ref _sequence) * 10;
        var clubId = baseId;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        var project = new Project
        {
            ProjectId = baseId,
            ProjectName = "Test Project",
            ClubId = clubId,
            LeaderUserId = baseId + 10,
            ProjectStatus = "active",
            CreatedAt = now
        };

        db.Add(new Club { ClubId = clubId, ClubName = "Test Club", ClubStatus = "active", CreatedAt = now });
        db.Add(project);

        // Active user
        db.Add(new User { UserId = baseId + 1, Username = "active-user", PasswordHash = "unused", RealName = "Active", AccountStatus = "active", CreatedAt = now });
        db.Add(new ClubMember { MemberId = baseId + 100, ClubId = clubId, UserId = baseId + 1, MemberStatus = "active", TermStart = now.AddMonths(-1), TermEnd = now.AddMonths(1) });

        // Inactive user
        db.Add(new User { UserId = baseId + 2, Username = "inactive-user", PasswordHash = "unused", RealName = "Inactive", AccountStatus = "inactive", CreatedAt = now });
        db.Add(new ClubMember { MemberId = baseId + 101, ClubId = clubId, UserId = baseId + 2, MemberStatus = "active", TermStart = now.AddMonths(-1), TermEnd = now.AddMonths(1) });

        await db.SaveChangesAsync();

        var candidates = await service.GetCandidateUsersQuery(project).ToListAsync();

        Assert.Single(candidates);
        Assert.Equal("Active", candidates[0].RealName);
    }

    [Fact]
    public async Task GetCandidateUsersQuery_ExcludesExistingProjectMembers()
    {
        var baseId = 3200 + Interlocked.Increment(ref _sequence) * 10;
        var clubId = baseId;
        var projectId = baseId;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        var project = new Project
        {
            ProjectId = projectId,
            ProjectName = "Test Project",
            ClubId = clubId,
            LeaderUserId = baseId + 10,
            ProjectStatus = "active",
            CreatedAt = now
        };

        db.Add(new Club { ClubId = clubId, ClubName = "Test Club", ClubStatus = "active", CreatedAt = now });
        db.Add(project);

        // Candidate user not yet in project
        db.Add(new User { UserId = baseId + 1, Username = "candidate", PasswordHash = "unused", RealName = "Candidate", AccountStatus = "active", CreatedAt = now });
        db.Add(new ClubMember { MemberId = baseId + 100, ClubId = clubId, UserId = baseId + 1, MemberStatus = "active", TermStart = now.AddMonths(-1), TermEnd = now.AddMonths(1) });

        // Existing project member
        db.Add(new User { UserId = baseId + 2, Username = "existing", PasswordHash = "unused", RealName = "Existing", AccountStatus = "active", CreatedAt = now });
        db.Add(new ClubMember { MemberId = baseId + 101, ClubId = clubId, UserId = baseId + 2, MemberStatus = "active", TermStart = now.AddMonths(-1), TermEnd = now.AddMonths(1) });
        db.Add(new ProjectMember { MemberId = baseId + 200, ProjectId = projectId, UserId = baseId + 2, MemberStatus = "active", AssignedAt = now });

        await db.SaveChangesAsync();

        var candidates = await service.GetCandidateUsersQuery(project).ToListAsync();

        Assert.Single(candidates);
        Assert.Equal("Candidate", candidates[0].RealName);
    }

    [Fact]
    public async Task GetCandidateUsersQuery_Defers_CompositionUntilMaterialization()
    {
        var baseId = 3300 + Interlocked.Increment(ref _sequence) * 10;
        var clubId = baseId;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProjectMembershipService>();
        var now = DateTime.UtcNow;

        var project = new Project
        {
            ProjectId = baseId,
            ProjectName = "Test Project",
            ClubId = clubId,
            LeaderUserId = baseId + 10,
            ProjectStatus = "active",
            CreatedAt = now
        };

        db.Add(new Club { ClubId = clubId, ClubName = "Test Club", ClubStatus = "active", CreatedAt = now });
        db.Add(project);
        await db.SaveChangesAsync();

        var query = service.GetCandidateUsersQuery(project);

        // Query should be IQueryable, not materialized
        Assert.IsAssignableFrom<IQueryable<User>>(query);

        // Add data after query creation but before materialization
        db.Add(new User { UserId = baseId + 1, Username = "user", PasswordHash = "unused", RealName = "User", AccountStatus = "active", CreatedAt = now });
        db.Add(new ClubMember { MemberId = baseId + 100, ClubId = clubId, UserId = baseId + 1, MemberStatus = "active", TermStart = now.AddMonths(-1), TermEnd = now.AddMonths(1) });
        await db.SaveChangesAsync();

        // Materialization should include the newly added data
        var candidates = await query.ToListAsync();
        Assert.Single(candidates);
    }
}
