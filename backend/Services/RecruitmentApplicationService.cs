using System.Data;
using ClubHub.Api.Controllers;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Oracle.ManagedDataAccess.Client;
using CreateRecruitmentApplicationRequest = Org.OpenAPITools.Models.CreateRecruitmentApplicationRequest;
using RecruitmentApplicationDto = Org.OpenAPITools.Models.RecruitmentApplication;
using ReviewRecruitmentApplicationRequest = Org.OpenAPITools.Models.ReviewRecruitmentApplicationRequest;
using static ClubHub.Api.Services.RecruitmentWorkflow;

namespace ClubHub.Api.Services;

public class RecruitmentApplicationService
{
    private readonly ClubHubDbContext _db;

    public RecruitmentApplicationService(ClubHubDbContext db) => _db = db;

    public async Task<ServiceResult<IReadOnlyList<RecruitmentApplicationDto>>> GetApplicationsAsync(
        int recruitId,
        int viewerUserId)
    {
        if (viewerUserId <= 0) return ServiceResult<IReadOnlyList<RecruitmentApplicationDto>>.Fail(400, "请选择当前用户。");

        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null) return ServiceResult<IReadOnlyList<RecruitmentApplicationDto>>.Fail(404, "当前用户不存在。");

        var recruitment = await RecruitmentQuery(asNoTracking: true).FirstOrDefaultAsync(r => r.RecruitId == recruitId);
        if (recruitment is null) return ServiceResult<IReadOnlyList<RecruitmentApplicationDto>>.Fail(404, "招募不存在。");

        var canManage = CanManageRecruitment(viewer, recruitment.ClubId);
        var query = ApplicationQuery(asNoTracking: true).Where(a => a.RecruitId == recruitId);
        if (!canManage)
        {
            query = query.Where(a => a.UserId == viewer.UserId);
        }

        var applications = await query
            .OrderByDescending(a => a.SubmittedAt)
            .ThenByDescending(a => a.ApplicationId)
            .ToListAsync();

        return ServiceResult<IReadOnlyList<RecruitmentApplicationDto>>.Ok(applications.Select(ToApplicationDto).ToList());
    }

    public async Task<ServiceResult<RecruitmentApplicationDto>> CreateApplicationAsync(
        int recruitId,
        CreateRecruitmentApplicationRequest req)
    {
        if (req.CurrentUserId <= 0) return ServiceResult<RecruitmentApplicationDto>.Fail(400, "请选择当前报名用户。");
        if (string.IsNullOrWhiteSpace(req.ApplicationReason)) return ServiceResult<RecruitmentApplicationDto>.Fail(400, "报名理由不能为空。");

        var applicant = await LoadUserAsync(req.CurrentUserId);
        if (applicant is null) return ServiceResult<RecruitmentApplicationDto>.Fail(404, "当前用户不存在。");
        if (!UsersController.IsActive(applicant.AccountStatus))
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(400, "当前用户账号不可用，不能提交招募报名。");
        }
        if (!UsersController.IsStudent(applicant) || UsersController.IsPlatformAdmin(applicant))
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(403, "只有普通学生可以提交招募报名。");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lockResult = await LockRecruitmentRowAsync(recruitId);
        if (lockResult == RecruitmentRowLockResult.Missing)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(404, "招募不存在。");
        }
        if (lockResult == RecruitmentRowLockResult.Busy)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "招募正在处理其他报名，请稍后重试。");
        }

        var recruitment = await RecruitmentQuery().FirstOrDefaultAsync(r => r.RecruitId == recruitId);
        if (recruitment is null) return ServiceResult<RecruitmentApplicationDto>.Fail(404, "招募不存在。");
        if (recruitment.Club is null || !IsMaintainableClub(recruitment.Club))
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "社团状态不允许接收报名。");
        }
        var now = BusinessNow();
        if (EffectiveRecruitmentStatus(recruitment, now) != RecruitmentStatuses.Accepting)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "只有申请中的纳新可以提交报名。");
        }

        var hasSubmitted = await _db.RecruitmentApplications.AnyAsync(a =>
            a.RecruitId == recruitId &&
            a.UserId == applicant.UserId);
        if (hasSubmitted) return ServiceResult<RecruitmentApplicationDto>.Fail(409, "你已经提交过该招募报名，请勿重复提交。");

        if (await IsCurrentClubMemberAsync(recruitment.ClubId, applicant.UserId))
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "你已经是该社团成员，无需再次报名招募。");
        }

        var currentClubCount = await CountCurrentMembershipClubsAsync(applicant.UserId);
        if (currentClubCount >= MaxStudentClubMemberships)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "一个学生最多只能同时加入 3 个社团，当前已达到上限。");
        }

        if (recruitment.Quota is not null && recruitment.Applications.Count(a => a.ApplicationStatus == ApplicationAccepted) >= recruitment.Quota.Value)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "招募名额已满，暂时不能继续报名。");
        }

        var nextId = (await _db.RecruitmentApplications.MaxAsync(a => (int?)a.ApplicationId) ?? 0) + 1;
        var application = new RecruitmentApplication
        {
            ApplicationId = nextId,
            RecruitId = recruitment.RecruitId,
            UserId = applicant.UserId,
            ApplicationReason = req.ApplicationReason.Trim(),
            ApplicationStatus = ApplicationPending,
            SubmittedAt = DateTime.UtcNow,
            Recruitment = recruitment,
            User = applicant
        };

        _db.RecruitmentApplications.Add(application);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var created = await ApplicationQuery().FirstAsync(a => a.ApplicationId == application.ApplicationId);
        return ServiceResult<RecruitmentApplicationDto>.Ok(ToApplicationDto(created));
    }

    public async Task<ServiceResult<RecruitmentApplicationDto>> ReviewApplicationAsync(
        int applicationId,
        ReviewRecruitmentApplicationRequest req)
    {
        if (req.CurrentUserId <= 0) return ServiceResult<RecruitmentApplicationDto>.Fail(400, "请选择当前筛选用户。");

        var decision = NormalizeApplicationStatus(req.Decision);
        if (decision is not ApplicationAccepted and not ApplicationRejected)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(400, "筛选结果只能是 accepted 或 rejected。");
        }

        if (req.InterviewScore is not null && (req.InterviewScore < 0 || req.InterviewScore > 100))
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(400, "面试分数必须在 0 到 100 之间。");
        }

        var reviewer = await LoadUserAsync(req.CurrentUserId);
        if (reviewer is null) return ServiceResult<RecruitmentApplicationDto>.Fail(404, "当前用户不存在。");

        var application = await ApplicationQuery().FirstOrDefaultAsync(a => a.ApplicationId == applicationId);
        if (application is null) return ServiceResult<RecruitmentApplicationDto>.Fail(404, "招募报名不存在。");
        if (application.Recruitment is null) return ServiceResult<RecruitmentApplicationDto>.Fail(404, "招募不存在。");

        if (!CanManageRecruitment(reviewer, application.Recruitment.ClubId))
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(403, "只有系统管理员或本社团干部可以筛选招募报名。");
        }

        if (application.ApplicationStatus != ApplicationPending)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "只有待筛选的报名可以录入筛选结果。");
        }

        if (decision == ApplicationAccepted)
        {
            return await AcceptApplicationAsync(application, reviewer, req);
        }

        return await RejectApplicationAsync(application, reviewer, req);
    }

    private async Task<ServiceResult<RecruitmentApplicationDto>> AcceptApplicationAsync(
        RecruitmentApplication application,
        User reviewer,
        ReviewRecruitmentApplicationRequest req)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lockResult = await LockRecruitmentRowAsync(application.RecruitId);
        if (lockResult == RecruitmentRowLockResult.Missing)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(404, "招募不存在。");
        }
        if (lockResult == RecruitmentRowLockResult.Busy)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "招募正在处理其他筛选结果，请稍后重试。");
        }

        await _db.Entry(application).ReloadAsync();
        if (application.Recruitment is null)
        {
            await _db.Entry(application).Reference(a => a.Recruitment).LoadAsync();
        }

        if (application.Recruitment is null)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(404, "招募不存在。");
        }

        if (application.ApplicationStatus != ApplicationPending)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "只有待筛选的报名可以录入筛选结果。");
        }

        if (await IsCurrentClubMemberAsync(application.Recruitment.ClubId, application.UserId))
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "该学生已经是社团成员，不能重复录取。");
        }

        var currentClubCount = await CountCurrentMembershipClubsAsync(application.UserId);
        if (currentClubCount >= MaxStudentClubMemberships)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "一个学生最多只能同时加入 3 个社团，该学生已达到上限。");
        }

        var acceptedCount = await _db.RecruitmentApplications.CountAsync(a =>
            a.RecruitId == application.RecruitId &&
            a.ApplicationStatus == ApplicationAccepted);
        if (application.Recruitment.Quota is not null && acceptedCount >= application.Recruitment.Quota.Value)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "招募名额已满，不能继续录取。");
        }

        await AddAcceptedMemberAsync(application, DateTime.UtcNow);

        application.ApplicationStatus = ApplicationAccepted;
        application.InterviewScore = req.InterviewScore is null ? null : Convert.ToDecimal(req.InterviewScore.Value);
        application.ReviewerUserId = reviewer.UserId;
        application.Reviewer = reviewer;
        application.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var accepted = await ApplicationQuery().FirstAsync(a => a.ApplicationId == application.ApplicationId);
        return ServiceResult<RecruitmentApplicationDto>.Ok(ToApplicationDto(accepted));
    }

    private async Task<ServiceResult<RecruitmentApplicationDto>> RejectApplicationAsync(
        RecruitmentApplication application,
        User reviewer,
        ReviewRecruitmentApplicationRequest req)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lockResult = await LockRecruitmentRowAsync(application.RecruitId);
        if (lockResult == RecruitmentRowLockResult.Missing)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(404, "招募不存在。");
        }
        if (lockResult == RecruitmentRowLockResult.Busy)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "招募正在处理其他筛选结果，请稍后重试。");
        }

        await _db.Entry(application).ReloadAsync();
        if (application.ApplicationStatus != ApplicationPending)
        {
            return ServiceResult<RecruitmentApplicationDto>.Fail(409, "只有待筛选的报名可以录入筛选结果。");
        }

        application.ApplicationStatus = ApplicationRejected;
        application.InterviewScore = req.InterviewScore is null ? null : Convert.ToDecimal(req.InterviewScore.Value);
        application.ReviewerUserId = reviewer.UserId;
        application.Reviewer = reviewer;
        application.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var rejected = await ApplicationQuery().FirstAsync(a => a.ApplicationId == application.ApplicationId);
        return ServiceResult<RecruitmentApplicationDto>.Ok(ToApplicationDto(rejected));
    }

    private IQueryable<Recruitment> RecruitmentQuery(bool asNoTracking = false)
    {
        var query = _db.Recruitments
            .Include(r => r.Club)
            .Include(r => r.Applications);

        return asNoTracking ? query.AsNoTracking() : query;
    }

    private IQueryable<RecruitmentApplication> ApplicationQuery(bool asNoTracking = false)
    {
        var query = _db.RecruitmentApplications
            .Include(a => a.Recruitment)
                .ThenInclude(r => r!.Club)
            .Include(a => a.User)
            .Include(a => a.Reviewer);

        return asNoTracking ? query.AsNoTracking() : query;
    }

    private async Task<RecruitmentRowLockResult> LockRecruitmentRowAsync(int recruitId)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT RECRUIT_ID FROM RECRUITMENTS WHERE RECRUIT_ID = :recruitId FOR UPDATE WAIT 5";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "recruitId";
        parameter.Value = recruitId;
        command.Parameters.Add(parameter);

        try
        {
            var lockedRecruitId = await command.ExecuteScalarAsync();
            return lockedRecruitId is not null && lockedRecruitId != DBNull.Value
                ? RecruitmentRowLockResult.Locked
                : RecruitmentRowLockResult.Missing;
        }
        catch (OracleException ex) when (IsLockContention(ex))
        {
            return RecruitmentRowLockResult.Busy;
        }
    }

    private static bool IsLockContention(OracleException ex) =>
        ex.Number is 54 or 60 or 30006;

    private enum RecruitmentRowLockResult
    {
        Locked,
        Missing,
        Busy
    }

    private async Task<User?> LoadUserAsync(int userId) =>
        await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId);

    private async Task<bool> IsCurrentClubMemberAsync(int clubId, int userId)
    {
        var today = BusinessToday();
        var currentMemberRecordExists = await _db.ClubMembers.AnyAsync(m =>
            m.ClubId == clubId &&
            m.UserId == userId &&
            (m.MemberStatus == null || m.MemberStatus == MemberActive) &&
            (m.TermStart == null || m.TermStart <= today) &&
            (m.TermEnd == null || m.TermEnd >= today));
        if (currentMemberRecordExists) return true;

        var roleAssignments = await _db.UserRoles
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId && ur.ClubId == clubId)
            .ToListAsync();
        return roleAssignments.Any(ur => IsClubMembershipRole(ur.Role));
    }

    private async Task<int> CountCurrentMembershipClubsAsync(int userId, int? excludingClubId = null)
    {
        var today = BusinessToday();
        var memberClubIds = await _db.ClubMembers
            .Where(m =>
                m.UserId == userId &&
                (excludingClubId == null || m.ClubId != excludingClubId.Value) &&
                (m.MemberStatus == null || m.MemberStatus == MemberActive) &&
                (m.TermStart == null || m.TermStart <= today) &&
                (m.TermEnd == null || m.TermEnd >= today))
            .Select(m => m.ClubId)
            .ToListAsync();

        var roleAssignments = await _db.UserRoles
            .Include(ur => ur.Role)
            .Where(ur =>
                ur.UserId == userId &&
                ur.ClubId != null &&
                (excludingClubId == null || ur.ClubId != excludingClubId.Value))
            .ToListAsync();

        return memberClubIds
            .Concat(roleAssignments
                .Where(ur => IsClubMembershipRole(ur.Role))
                .Select(ur => ur.ClubId!.Value))
            .Distinct()
            .Count();
    }

    private async Task AddAcceptedMemberAsync(RecruitmentApplication application, DateTime now)
    {
        if (application.Recruitment is null) return;

        var nextMemberId = (await _db.ClubMembers.MaxAsync(m => (int?)m.MemberId) ?? 0) + 1;
        _db.ClubMembers.Add(new ClubMember
        {
            MemberId = nextMemberId,
            ClubId = application.Recruitment.ClubId,
            UserId = application.UserId,
            PositionName = "社员",
            TermName = $"{BusinessDate(now).Year} 招新录取",
            TermStart = BusinessDate(now),
            MemberStatus = MemberActive,
            JoinAt = now,
            ContributionScore = 0
        });

        await EnsureClubMemberRoleAsync(application.Recruitment.ClubId, application.UserId, now);
    }

    private async Task EnsureClubMemberRoleAsync(int clubId, int userId, DateTime now)
    {
        var role = await EnsureClubRoleAsync(
            ClubMemberRoleCode,
            "社团成员",
            "指定社团内角色，可查看社团内部信息、资源、通知和参与讨论、签到。",
            now);

        var hasRole = await _db.UserRoles.AnyAsync(ur =>
            ur.UserId == userId &&
            ur.ClubId == clubId &&
            ur.Role != null &&
            ur.Role.RoleCode.ToUpper() == ClubMemberRoleCode);
        if (hasRole) return;

        _db.UserRoles.Add(new UserRole
        {
            UserRoleId = await NextUserRoleIdAsync(),
            UserId = userId,
            RoleId = role.RoleId,
            ClubId = clubId,
            AssignedAt = now
        });
    }

    private async Task<Role> EnsureClubRoleAsync(string roleCode, string roleName, string permissionDesc, DateTime now)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleCode.ToUpper() == roleCode);
        if (role is not null) return role;

        role = new Role
        {
            RoleId = await NextRoleIdAsync(),
            RoleCode = roleCode,
            RoleName = roleName,
            RoleScope = "club",
            PermissionDesc = permissionDesc,
            CreatedAt = now
        };
        _db.Roles.Add(role);
        return role;
    }

    private async Task<int> NextRoleIdAsync()
    {
        var maxSaved = await _db.Roles.MaxAsync(r => (int?)r.RoleId) ?? 0;
        var maxAdded = _db.ChangeTracker.Entries<Role>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.RoleId)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(maxSaved, maxAdded) + 1;
    }

    private async Task<int> NextUserRoleIdAsync()
    {
        var maxSaved = await _db.UserRoles.MaxAsync(ur => (int?)ur.UserRoleId) ?? 0;
        var maxAdded = _db.ChangeTracker.Entries<UserRole>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.UserRoleId)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(maxSaved, maxAdded) + 1;
    }
}
