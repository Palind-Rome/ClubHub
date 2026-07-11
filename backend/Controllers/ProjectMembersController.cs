using ClubHub.Api.Data;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AddProjectMemberRequest = Org.OpenAPITools.Models.AddProjectMemberRequest;
using ApiError = Org.OpenAPITools.Models.ApiError;
using ApiProjectMember = Org.OpenAPITools.Models.ProjectMember;
using ApiProjectMemberCandidate = Org.OpenAPITools.Models.ProjectMemberCandidate;
using DbProject = ClubHub.Api.Data.Entities.Project;
using DbProjectMember = ClubHub.Api.Data.Entities.ProjectMember;
using DbUser = ClubHub.Api.Data.Entities.User;

namespace ClubHub.Api.Controllers;

/// <summary>
/// 项目成员列表、候选人、添加与软移除接口。
/// </summary>
[ApiController]
[Route("api/projects/{projectId:int}")]
[Authorize]
public class ProjectMembersController : ControllerBase
{
    private readonly ClubHubDbContext _db;
    private readonly ProjectMembershipService _membershipService;

    public ProjectMembersController(
        ClubHubDbContext db,
        ProjectMembershipService membershipService)
    {
        _db = db;
        _membershipService = membershipService;
    }

    [HttpGet("members")]
    public async Task<IActionResult> GetMembers(int projectId, [FromQuery] bool includeInactive = false)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();

        var project = await FindProjectAsync(projectId);
        if (project is null) return Error(404, "project_not_found", "项目不存在。");

        var canManage = await _membershipService.CanManageMembersAsync(project, userId.Value);
        if (includeInactive && !canManage)
        {
            return Error(403, "project_member_history_forbidden", "只有项目负责人或本社团负责人、干部可以查看历史成员。");
        }

        if (!canManage && !await _membershipService.IsActiveMemberAsync(projectId, userId.Value))
        {
            return Error(403, "project_member_view_forbidden", "只有项目 active 成员可以查看当前成员列表。");
        }

        var query = _db.ProjectMembers
            .AsNoTracking()
            .Include(member => member.User)
            .Where(member => member.ProjectId == projectId);
        if (!includeInactive)
        {
            query = query.Where(member => member.MemberStatus == ProjectMembershipService.ActiveStatus);
        }

        var members = await query.ToListAsync();
        var result = members
            .OrderBy(member => member.MemberRole == ProjectMembershipService.LeaderRole ? 0 : 1)
            .ThenBy(member => member.MemberStatus == ProjectMembershipService.ActiveStatus ? 0 : 1)
            .ThenBy(member => member.User?.RealName)
            .ThenBy(member => member.ProjectMemberId)
            .Select(ToProjectMemberDto)
            .ToList();

        return Ok(result);
    }

    [HttpGet("member-candidates")]
    public async Task<IActionResult> GetMemberCandidates(int projectId)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();

        var project = await FindProjectAsync(projectId);
        if (project is null) return Error(404, "project_not_found", "项目不存在。");
        if (!await _membershipService.CanManageMembersAsync(project, userId.Value))
        {
            return Error(403, "project_member_manage_forbidden", "当前用户无权维护该项目成员。");
        }

        var candidates = await _membershipService.GetCandidateUsersAsync(project);
        return Ok(candidates.Select(ToCandidateDto).ToList());
    }

    [HttpPost("members")]
    public async Task<IActionResult> AddMember(int projectId, [FromBody] AddProjectMemberRequest? request)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();
        if (request is null)
        {
            return Error(400, "invalid_project_member_request", "请填写要添加的项目成员信息。");
        }

        var project = await FindProjectAsync(projectId);
        if (project is null) return Error(404, "project_not_found", "项目不存在。");
        if (!await _membershipService.CanManageMembersAsync(project, userId.Value))
        {
            return Error(403, "project_member_manage_forbidden", "当前用户无权维护该项目成员。");
        }

        if (request.UserId <= 0)
        {
            return Error(400, "invalid_project_member_user", "请选择有效的项目成员候选人。");
        }

        var memberRole = request.MemberRole switch
        {
            AddProjectMemberRequest.MemberRoleEnum.MemberEnum => ProjectMembershipService.MemberRole,
            AddProjectMemberRequest.MemberRoleEnum.MentorEnum => ProjectMembershipService.MentorRole,
            _ => null
        };
        if (memberRole is null)
        {
            return Error(400, "invalid_project_member_role", "项目成员角色只能是普通成员或导师。");
        }

        if (request.Remark?.Trim().Length > 255)
        {
            return Error(400, "invalid_project_member_remark", "项目成员备注不能超过 255 个字符。");
        }

        var candidateExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(candidate => candidate.UserId == request.UserId);
        if (!candidateExists)
        {
            return Error(404, "project_member_candidate_not_found", "候选用户不存在。");
        }

        var alreadyActive = await _db.ProjectMembers
            .AsNoTracking()
            .AnyAsync(member =>
                member.ProjectId == projectId &&
                member.UserId == request.UserId &&
                member.MemberStatus == ProjectMembershipService.ActiveStatus);
        if (alreadyActive)
        {
            return Error(409, "project_member_already_active", "该用户已经是项目 active 成员，请勿重复添加。");
        }

        if (!await _membershipService.IsActiveUserAsync(request.UserId))
        {
            return Error(400, "project_member_candidate_disabled", "候选用户账号状态异常，不能加入项目。");
        }

        if (!await _membershipService.IsActiveClubMemberAsync(project.ClubId, request.UserId))
        {
            return Error(400, "project_member_candidate_ineligible", "候选用户不是项目所属社团的 active 成员。");
        }

        var result = await _membershipService.AddOrRestoreMemberAsync(
            project,
            request.UserId,
            memberRole,
            request.Remark);
        if (!result.Succeeded || result.Value is null)
        {
            var code = result.ErrorMessage?.Contains("已经是", StringComparison.Ordinal) == true
                ? "project_member_already_active"
                : "project_member_write_conflict";
            return Error(result.StatusCode, code, result.ErrorMessage ?? "添加项目成员失败。");
        }

        return StatusCode(StatusCodes.Status201Created, ToProjectMemberDto(result.Value));
    }

    [HttpDelete("members/{projectMemberId:int}")]
    public async Task<IActionResult> RemoveMember(int projectId, int projectMemberId)
    {
        var userId = User.GetUserId();
        if (userId is null) return AuthenticationRequired();

        var project = await FindProjectAsync(projectId);
        if (project is null) return Error(404, "project_not_found", "项目不存在。");
        if (!await _membershipService.CanManageMembersAsync(project, userId.Value))
        {
            return Error(403, "project_member_manage_forbidden", "当前用户无权维护该项目成员。");
        }

        var member = await _db.ProjectMembers.FirstOrDefaultAsync(candidate =>
            candidate.ProjectMemberId == projectMemberId &&
            candidate.ProjectId == projectId);
        if (member is null)
        {
            return Error(404, "project_member_not_found", "项目成员不存在。");
        }

        if (project.LeaderUserId == member.UserId)
        {
            return Error(409, "project_leader_cannot_be_removed", "当前项目负责人不能被移除，请先调整项目负责人。");
        }

        if (member.MemberStatus != ProjectMembershipService.RemovedStatus || member.LeftAt is null)
        {
            var now = DateTime.UtcNow;
            member.MemberStatus = ProjectMembershipService.RemovedStatus;
            member.LeftAt ??= now;
            member.UpdatedAt = now;
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }

    private Task<DbProject?> FindProjectAsync(int projectId)
    {
        if (projectId <= 0) return Task.FromResult<DbProject?>(null);

        return _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(project => project.ProjectId == projectId);
    }

    private IActionResult AuthenticationRequired() =>
        Unauthorized(new ApiError
        {
            Code = "authentication_required",
            Message = "登录状态已失效，请重新登录。"
        });

    private ObjectResult Error(int statusCode, string code, string message, string? detail = null) =>
        StatusCode(statusCode, new ApiError
        {
            Code = code,
            Message = message,
            Detail = detail
        });

    private static ApiProjectMember ToProjectMemberDto(DbProjectMember member)
    {
        return new ApiProjectMember
        {
            ProjectMemberId = member.ProjectMemberId,
            ProjectId = member.ProjectId,
            UserId = member.UserId,
            RealName = member.User?.RealName,
            StudentNo = member.User?.StudentNo,
            MemberRole = member.MemberRole switch
            {
                ProjectMembershipService.LeaderRole => ApiProjectMember.MemberRoleEnum.LeaderEnum,
                ProjectMembershipService.MentorRole => ApiProjectMember.MemberRoleEnum.MentorEnum,
                _ => ApiProjectMember.MemberRoleEnum.MemberEnum
            },
            MemberStatus = member.MemberStatus switch
            {
                ProjectMembershipService.ActiveStatus => ApiProjectMember.MemberStatusEnum.ActiveEnum,
                ProjectMembershipService.RemovedStatus => ApiProjectMember.MemberStatusEnum.RemovedEnum,
                _ => ApiProjectMember.MemberStatusEnum.QuitEnum
            },
            JoinedAt = member.JoinedAt,
            LeftAt = member.LeftAt,
            Remark = member.Remark,
            CreatedAt = member.CreatedAt,
            UpdatedAt = member.UpdatedAt
        };
    }

    private static ApiProjectMemberCandidate ToCandidateDto(DbUser user)
    {
        var name = string.IsNullOrWhiteSpace(user.RealName)
            ? string.IsNullOrWhiteSpace(user.Username) ? $"用户 {user.UserId}" : user.Username.Trim()
            : user.RealName.Trim();
        var displayName = string.IsNullOrWhiteSpace(user.StudentNo)
            ? name
            : $"{name}（{user.StudentNo.Trim()}）";

        return new ApiProjectMemberCandidate
        {
            UserId = user.UserId,
            RealName = user.RealName,
            StudentNo = user.StudentNo,
            DisplayName = displayName
        };
    }
}
