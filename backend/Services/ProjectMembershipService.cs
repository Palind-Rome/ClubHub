using System.Data;
using ClubHub.Api.Controllers;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Services;

/// <summary>
/// 统一维护项目成员边界，并为任务模块提供 active 成员校验。
/// </summary>
public class ProjectMembershipService
{
    public const string LeaderRole = "leader";
    public const string MemberRole = "member";
    public const string MentorRole = "mentor";
    public const string ActiveStatus = "active";
    public const string RemovedStatus = "removed";

    private const int MaxWriteRetries = 3;
    private const string ManageProjectTasksPermission = "project:task:manage";

    private readonly ClubHubDbContext _db;
    private readonly AuthService _authService;

    public ProjectMembershipService(ClubHubDbContext db, AuthService authService)
    {
        _db = db;
        _authService = authService;
    }

    /// <summary>
    /// 判断账号正常的用户是否为项目 active 成员，供 #17 的任务分配与更新复用。
    /// </summary>
    public Task<bool> IsActiveMemberAsync(int projectId, int userId)
    {
        return _db.ProjectMembers.AnyAsync(pm =>
            pm.ProjectId == projectId &&
            pm.UserId == userId &&
            pm.MemberStatus == ActiveStatus &&
            pm.User != null &&
            (pm.User.AccountStatus == null ||
             pm.User.AccountStatus == string.Empty ||
             pm.User.AccountStatus.ToLower() == "active" ||
             pm.User.AccountStatus.ToLower() == "normal" ||
             pm.User.AccountStatus.ToLower() == "enabled" ||
             pm.User.AccountStatus == "在任" ||
             pm.User.AccountStatus == "正常"));
    }

    public Task<bool> IsActiveUserAsync(int userId)
    {
        return _db.Users.AnyAsync(user =>
            user.UserId == userId &&
            (user.AccountStatus == null ||
             user.AccountStatus == string.Empty ||
             user.AccountStatus.ToLower() == "active" ||
             user.AccountStatus.ToLower() == "normal" ||
             user.AccountStatus.ToLower() == "enabled" ||
             user.AccountStatus == "在任" ||
             user.AccountStatus == "正常"));
    }

    public Task<bool> IsActiveClubMemberAsync(int clubId, int userId)
    {
        var businessDate = DateTime.UtcNow.Date;
        return _db.ClubMembers.AnyAsync(member =>
            member.ClubId == clubId &&
            member.UserId == userId &&
            (member.MemberStatus == null ||
             member.MemberStatus == string.Empty ||
             member.MemberStatus.ToLower() == "active" ||
             member.MemberStatus.ToLower() == "normal" ||
             member.MemberStatus.ToLower() == "enabled" ||
             member.MemberStatus == "在任" ||
             member.MemberStatus == "正常") &&
            (member.TermStart == null || member.TermStart <= businessDate) &&
            (member.TermEnd == null || member.TermEnd >= businessDate));
    }

    public static bool IsClosed(Project project) =>
        string.Equals(project.ProjectStatus, "closed", StringComparison.OrdinalIgnoreCase);

    public static bool IsTeacher(User user) =>
        !string.IsNullOrWhiteSpace(user.StudentNo) && user.StudentNo.Length == 5 && user.StudentNo.All(char.IsDigit);

    /// <summary>
    /// 项目负责人、具备项目任务管理权限的社团角色，以及兼容旧数据的负责人/干部均可维护成员。
    /// </summary>
    public async Task<bool> CanManageMembersAsync(Project project, int userId)
    {
        if (!await IsActiveUserAsync(userId)) return false;
        if (project.LeaderUserId == userId) return true;

        var permission = await _authService.CheckPermissionAsync(
            userId,
            ManageProjectTasksPermission,
            project.ClubId);
        if (permission.Succeeded && permission.Value?.Allowed == true)
        {
            return true;
        }

        var user = await _db.Users
            .AsNoTracking()
            .Include(candidate => candidate.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(candidate => candidate.ClubMemberships)
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId);

        return user is not null &&
            (UsersController.IsClubPrincipal(user, project.ClubId) ||
             UsersController.IsClubOfficer(user, project.ClubId));
    }

    public Task<List<User>> GetCandidateUsersAsync(Project project)
    {
        var businessDate = DateTime.UtcNow.Date;

        return _db.Users
            .AsNoTracking()
            .Where(user =>
                user.AccountStatus == null ||
                user.AccountStatus == string.Empty ||
                user.AccountStatus.ToLower() == "active" ||
                user.AccountStatus.ToLower() == "normal" ||
                user.AccountStatus.ToLower() == "enabled" ||
                user.AccountStatus == "在任" ||
                user.AccountStatus == "正常")
            .Where(user => user.ClubMemberships.Any(member =>
                member.ClubId == project.ClubId &&
                (member.MemberStatus == null ||
                 member.MemberStatus == string.Empty ||
                 member.MemberStatus.ToLower() == "active" ||
                 member.MemberStatus.ToLower() == "normal" ||
                 member.MemberStatus.ToLower() == "enabled" ||
                 member.MemberStatus == "在任" ||
                 member.MemberStatus == "正常") &&
                (member.TermStart == null || member.TermStart <= businessDate) &&
                (member.TermEnd == null || member.TermEnd >= businessDate)))
            .Where(user => !_db.ProjectMembers.Any(member =>
                member.ProjectId == project.ProjectId &&
                member.UserId == user.UserId &&
                member.MemberStatus == ActiveStatus))
            .OrderBy(user => user.RealName)
            .ThenBy(user => user.StudentNo)
            .ThenBy(user => user.UserId)
            .ToListAsync();
    }

    /// <summary>
    /// 添加新成员，或复用 removed/quit 记录恢复为 active；active 重复添加返回 409。
    /// </summary>
    public async Task<ServiceResult<ProjectMember>> AddOrRestoreMemberAsync(
        Project project,
        int userId,
        string memberRole,
        string? remark)
    {
        for (var attempt = 1; attempt <= MaxWriteRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var member = await _db.ProjectMembers.FirstOrDefaultAsync(candidate =>
                    candidate.ProjectId == project.ProjectId &&
                    candidate.UserId == userId);

                if (member is not null && member.MemberStatus == ActiveStatus)
                {
                    await transaction.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    return ServiceResult<ProjectMember>.Fail(409, "该用户已经是项目 active 成员，请勿重复添加。", "project_member_already_active");
                }

                var now = DateTime.UtcNow;
                if (member is null)
                {
                    member = new ProjectMember
                    {
                        ProjectMemberId = await GetNextProjectMemberIdAsync(),
                        ProjectId = project.ProjectId,
                        UserId = userId,
                        MemberRole = memberRole,
                        MemberStatus = ActiveStatus,
                        JoinedAt = now,
                        LeftAt = null,
                        Remark = NormalizeOptionalText(remark),
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _db.ProjectMembers.Add(member);
                }
                else
                {
                    member.MemberRole = memberRole;
                    member.MemberStatus = ActiveStatus;
                    member.JoinedAt = now;
                    member.LeftAt = null;
                    member.Remark = NormalizeOptionalText(remark);
                    member.UpdatedAt = now;
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var memberId = member.ProjectMemberId;
                _db.ChangeTracker.Clear();
                var savedMember = await _db.ProjectMembers
                    .AsNoTracking()
                    .Include(candidate => candidate.User)
                    .SingleAsync(candidate => candidate.ProjectMemberId == memberId);
                return ServiceResult<ProjectMember>.Ok(savedMember);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && IsRetryableWriteConflict(ex))
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();

                if (attempt == MaxWriteRetries)
                {
                    var becameActive = await _db.ProjectMembers
                        .AsNoTracking()
                        .AnyAsync(member =>
                            member.ProjectId == project.ProjectId &&
                            member.UserId == userId &&
                            member.MemberStatus == ActiveStatus);
                    return becameActive
                        ? ServiceResult<ProjectMember>.Fail(409, "该用户已经是项目 active 成员，请勿重复添加。", "project_member_already_active")
                        : ServiceResult<ProjectMember>.Fail(409, "项目成员关系发生并发冲突，请稍后重试。");
                }
            }
        }

        return ServiceResult<ProjectMember>.Fail(409, "项目成员关系发生并发冲突，请稍后重试。");
    }

    /// <summary>
    /// 在调用方的 SERIALIZABLE 事务中同步新旧负责人关系，不单独保存或提交。
    /// </summary>
    public async Task SynchronizeLeaderAsync(
        Project project,
        int? previousLeaderUserId,
        int newLeaderUserId,
        DateTime now)
    {
        var memberships = await _db.ProjectMembers
            .Where(member => member.ProjectId == project.ProjectId)
            .ToListAsync();
        var nextMemberId = (await _db.ProjectMembers.MaxAsync(member => (int?)member.ProjectMemberId) ?? 0) + 1;

        foreach (var currentLeader in memberships.Where(member =>
                     member.UserId != newLeaderUserId &&
                     member.MemberRole == LeaderRole))
        {
            currentLeader.MemberRole = MemberRole;
            currentLeader.UpdatedAt = now;
        }

        if (previousLeaderUserId is not null && previousLeaderUserId != newLeaderUserId)
        {
            var previousLeader = memberships.FirstOrDefault(member => member.UserId == previousLeaderUserId.Value);
            if (previousLeader is null)
            {
                previousLeader = CreateMembership(
                    nextMemberId++,
                    project,
                    previousLeaderUserId.Value,
                    MemberRole,
                    project.CreatedAt == default ? now : project.CreatedAt,
                    now);
                memberships.Add(previousLeader);
                _db.ProjectMembers.Add(previousLeader);
            }
            else
            {
                ActivateAs(previousLeader, MemberRole, now);
            }
        }

        var newLeader = memberships.FirstOrDefault(member => member.UserId == newLeaderUserId);
        if (newLeader is null)
        {
            newLeader = CreateMembership(
                nextMemberId,
                project,
                newLeaderUserId,
                LeaderRole,
                now,
                now);
            _db.ProjectMembers.Add(newLeader);
        }
        else
        {
            ActivateAs(newLeader, LeaderRole, now);
        }
    }

    public static bool IsRetryableWriteConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("ORA-00001", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("ORA-08177", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("can't serialize access", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<int> GetNextProjectMemberIdAsync()
    {
        var maxId = await _db.ProjectMembers.MaxAsync(member => (int?)member.ProjectMemberId) ?? 0;
        return maxId + 1;
    }

    private static ProjectMember CreateMembership(
        int projectMemberId,
        Project project,
        int userId,
        string role,
        DateTime joinedAt,
        DateTime updatedAt)
    {
        return new ProjectMember
        {
            ProjectMemberId = projectMemberId,
            ProjectId = project.ProjectId,
            UserId = userId,
            MemberRole = role,
            MemberStatus = ActiveStatus,
            JoinedAt = joinedAt,
            LeftAt = null,
            CreatedAt = joinedAt,
            UpdatedAt = updatedAt
        };
    }

    private static void ActivateAs(ProjectMember member, string role, DateTime now)
    {
        if (member.MemberStatus != ActiveStatus)
        {
            member.JoinedAt = now;
        }

        member.MemberRole = role;
        member.MemberStatus = ActiveStatus;
        member.LeftAt = null;
        member.UpdatedAt = now;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
