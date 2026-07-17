using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiCreateNoticeRequest = Org.OpenAPITools.Models.CreateNoticeRequest;
using ApiMarkNoticeReadRequest = Org.OpenAPITools.Models.MarkNoticeReadRequest;
using ApiNotice = Org.OpenAPITools.Models.Notice;
using ApiNoticeReadResult = Org.OpenAPITools.Models.NoticeReadResult;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NoticesController : ControllerBase
{
    private const string TargetSchool = "school";
    private const string TargetClub = "club";
    private const string TargetDepartment = "department";
    private const string TargetMember = "member";
    private const string StatusDraft = "draft";
    private const string StatusPublished = "published";
    private const string StatusExpired = "expired";
    private const string MemberActive = "active";

    private static readonly HashSet<string> NoticePublisherRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "club_officer",
        "club_leader",
        "club_president",
        "club_manager",
        "president"
    };

    private readonly ClubHubDbContext _db;

    public NoticesController(ClubHubDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int viewerUserId,
        [FromQuery] string? noticeStatus,
        [FromQuery] string? targetType,
        [FromQuery] int? clubId,
        [FromQuery] bool unreadOnly = false)
    {
        if (viewerUserId <= 0) return BadRequest(new { message = "请提供当前查看用户。" });

        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null) return NotFound(new { message = "当前查看用户不存在。" });
        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return StatusCode(403, new { message = "当前用户账号不可用，不能查看通知。" });
        }

        var normalizedStatus = NormalizeStatus(noticeStatus);
        if (noticeStatus is not null && normalizedStatus is null)
        {
            return BadRequest(new { message = "通知状态只能是 draft、published 或 expired。" });
        }

        var normalizedTargetType = NormalizeTargetType(targetType);
        if (targetType is not null && normalizedTargetType is null)
        {
            return BadRequest(new { message = "定向类型只能是 school、club、department 或 member。" });
        }

        var now = DateTime.UtcNow;
        var query = NoticeQuery();
        if (clubId is not null) query = query.Where(n => n.ClubId == clubId.Value);
        if (normalizedTargetType is not null) query = query.Where(n => n.TargetType == normalizedTargetType);

        var notices = await query
            .OrderByDescending(n => n.PublishAt)
            .ThenByDescending(n => n.NoticeId)
            .ToListAsync();
        var context = await NoticeListContext.LoadAsync(_db, notices);
        var result = new List<ApiNotice>();

        foreach (var notice in notices)
        {
            var effectiveStatus = EffectiveStatus(notice, now);
            if (normalizedStatus is not null)
            {
                if (effectiveStatus != normalizedStatus) continue;
            }
            else if (effectiveStatus != StatusPublished)
            {
                continue;
            }

            if (!CanViewNotice(viewer, notice, effectiveStatus, context))
            {
                continue;
            }

            var dto = ToApiNotice(notice, viewer.UserId, context, effectiveStatus);
            if (unreadOnly && dto.IsRead) continue;
            result.Add(dto);
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ApiCreateNoticeRequest req)
    {
        var validationError = ValidateCreateRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var publisher = await LoadUserAsync(req.CurrentUserId);
        if (publisher is null) return NotFound(new { message = "发布人不存在。" });
        if (!UsersController.IsActive(publisher.AccountStatus))
        {
            return StatusCode(403, new { message = "发布人账号不可用，不能发布通知。" });
        }

        var targetType = NormalizeTargetType(req.TargetType)!;
        var noticeType = req.NoticeType.Trim();
        var status = NormalizePublishStatus(req.NoticeStatus);
        var now = DateTime.UtcNow;
        if (req.ExpireAt is not null && req.ExpireAt.Value <= now)
        {
            return BadRequest(new { message = "过期时间必须晚于当前时间。" });
        }

        var target = await ResolveCreateTargetAsync(req, publisher, targetType);
        if (target.Result is not null) return target.Result;

        var maxId = await _db.Notices.MaxAsync(n => (int?)n.NoticeId) ?? 0;
        var notice = new Notice
        {
            NoticeId = maxId + 1,
            ClubId = target.ClubId,
            PublisherUserId = publisher.UserId,
            NoticeType = noticeType,
            Title = req.Title.Trim(),
            Content = req.Content.Trim(),
            TargetType = targetType,
            TargetId = target.TargetId,
            PublishAt = now,
            ExpireAt = req.ExpireAt,
            NoticeStatus = status
        };

        _db.Notices.Add(notice);
        await _db.SaveChangesAsync();

        var created = await NoticeQuery().FirstAsync(n => n.NoticeId == notice.NoticeId);
        var context = await NoticeListContext.LoadAsync(_db, [created]);
        return CreatedAtAction(nameof(GetAll), new { viewerUserId = publisher.UserId }, ToApiNotice(created, publisher.UserId, context));
    }

    [HttpPatch("{noticeId:int}")]
    public async Task<IActionResult> UpdateDraft(int noticeId, [FromBody] ApiCreateNoticeRequest req)
    {
        var validationError = ValidateCreateRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var operatorUser = await LoadUserAsync(req.CurrentUserId);
        if (operatorUser is null) return NotFound(new { message = "当前操作用户不存在。" });
        if (!UsersController.IsActive(operatorUser.AccountStatus))
        {
            return StatusCode(403, new { message = "当前用户账号不可用，不能维护通知草稿。" });
        }

        var notice = await NoticeQuery().FirstOrDefaultAsync(n => n.NoticeId == noticeId);
        if (notice is null) return NotFound(new { message = "通知草稿不存在。" });
        if (EffectiveStatus(notice, DateTime.UtcNow) != StatusDraft)
        {
            return Conflict(new { message = "只有草稿通知可以编辑或发布。" });
        }

        if (notice.PublisherUserId != operatorUser.UserId && !CanManageNotice(operatorUser, notice))
        {
            return StatusCode(403, new { message = "当前用户没有维护该通知草稿的权限。" });
        }

        var targetType = NormalizeTargetType(req.TargetType)!;
        var status = NormalizePublishStatus(req.NoticeStatus)!;
        var now = DateTime.UtcNow;
        if (req.ExpireAt is not null && req.ExpireAt.Value <= now)
        {
            return BadRequest(new { message = "过期时间必须晚于当前时间。" });
        }

        var target = await ResolveCreateTargetAsync(req, operatorUser, targetType);
        if (target.Result is not null) return target.Result;

        notice.ClubId = target.ClubId;
        notice.NoticeType = req.NoticeType.Trim();
        notice.Title = req.Title.Trim();
        notice.Content = req.Content.Trim();
        notice.TargetType = targetType;
        notice.TargetId = target.TargetId;
        notice.ExpireAt = req.ExpireAt;
        notice.NoticeStatus = status;
        // 复用现有时间字段：草稿记录最近保存时间，发布时记录正式发布时间。
        notice.PublishAt = now;

        // 草稿不应产生阅读记录；同时清理旧版前端误为草稿写入的记录。
        if (notice.Reads.Count > 0) _db.NoticeReads.RemoveRange(notice.Reads);

        await _db.SaveChangesAsync();

        var updated = await NoticeQuery().FirstAsync(n => n.NoticeId == noticeId);
        var context = await NoticeListContext.LoadAsync(_db, [updated]);
        return Ok(ToApiNotice(updated, operatorUser.UserId, context));
    }

    [HttpDelete("{noticeId:int}")]
    public async Task<IActionResult> DeleteDraft(int noticeId, [FromQuery] int currentUserId)
    {
        if (currentUserId <= 0) return BadRequest(new { message = "请提供当前操作用户。" });

        var operatorUser = await LoadUserAsync(currentUserId);
        if (operatorUser is null) return NotFound(new { message = "当前操作用户不存在。" });
        if (!UsersController.IsActive(operatorUser.AccountStatus))
        {
            return StatusCode(403, new { message = "当前用户账号不可用，不能删除通知草稿。" });
        }

        var notice = await NoticeQuery().FirstOrDefaultAsync(n => n.NoticeId == noticeId);
        if (notice is null) return NotFound(new { message = "通知草稿不存在。" });
        if (EffectiveStatus(notice, DateTime.UtcNow) != StatusDraft)
        {
            return Conflict(new { message = "只有草稿通知可以删除。" });
        }

        if (notice.PublisherUserId != operatorUser.UserId && !CanManageNotice(operatorUser, notice))
        {
            return StatusCode(403, new { message = "当前用户没有删除该通知草稿的权限。" });
        }

        if (notice.Reads.Count > 0) _db.NoticeReads.RemoveRange(notice.Reads);
        _db.Notices.Remove(notice);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{noticeId:int}/reads")]
    public async Task<IActionResult> MarkRead(int noticeId, [FromBody] ApiMarkNoticeReadRequest req)
    {
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请提供当前阅读用户。" });

        var user = await LoadUserAsync(req.CurrentUserId);
        if (user is null) return NotFound(new { message = "当前阅读用户不存在。" });
        if (!UsersController.IsActive(user.AccountStatus))
        {
            return StatusCode(403, new { message = "当前用户账号不可用，不能标记通知已读。" });
        }

        var notice = await NoticeQuery().FirstOrDefaultAsync(n => n.NoticeId == noticeId);
        if (notice is null) return NotFound(new { message = "通知不存在。" });

        var effectiveStatus = EffectiveStatus(notice, DateTime.UtcNow);
        if (effectiveStatus == StatusDraft)
        {
            return Conflict(new { message = "通知草稿尚未发布，不能标记已读。" });
        }
        var context = await NoticeListContext.LoadAsync(_db, [notice]);
        if (!CanViewNotice(user, notice, effectiveStatus, context))
        {
            return StatusCode(403, new { message = "当前用户不可阅读该通知。" });
        }

        var existing = await _db.NoticeReads.FirstOrDefaultAsync(r =>
            r.NoticeId == noticeId &&
            r.UserId == user.UserId);
        if (existing is not null)
        {
            return Ok(new ApiNoticeReadResult
            {
                NoticeId = noticeId,
                UserId = user.UserId,
                IsRead = true,
                ReadAt = existing.ReadAt
            });
        }

        var now = DateTime.UtcNow;
        var nextId = (await _db.NoticeReads.MaxAsync(r => (int?)r.ReadId) ?? 0) + 1;
        var read = new NoticeRead
        {
            ReadId = nextId,
            NoticeId = noticeId,
            UserId = user.UserId,
            ReadAt = now
        };

        _db.NoticeReads.Add(read);
        await _db.SaveChangesAsync();

        return Ok(new ApiNoticeReadResult
        {
            NoticeId = noticeId,
            UserId = user.UserId,
            IsRead = true,
            ReadAt = now
        });
    }

    private IQueryable<Notice> NoticeQuery() =>
        _db.Notices
            .Include(n => n.Club)
            .Include(n => n.Publisher)
            .Include(n => n.Reads);

    private async Task<User?> LoadUserAsync(int userId) =>
        await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId);

    private async Task<(IActionResult? Result, int? ClubId, int? TargetId)> ResolveCreateTargetAsync(
        ApiCreateNoticeRequest req,
        User publisher,
        string targetType)
    {
        return targetType switch
        {
            TargetSchool => await ResolveSchoolTargetAsync(req, publisher),
            TargetClub => await ResolveClubTargetAsync(req, publisher),
            TargetDepartment => await ResolveDepartmentTargetAsync(req, publisher),
            TargetMember => await ResolveMemberTargetAsync(req, publisher),
            _ => (BadRequest(new { message = "定向类型不合法。" }), null, null)
        };
    }

    private Task<(IActionResult? Result, int? ClubId, int? TargetId)> ResolveSchoolTargetAsync(
        ApiCreateNoticeRequest req,
        User publisher)
    {
        if (req.TargetId is not null || req.ClubId is not null)
        {
            return Task.FromResult<(IActionResult? Result, int? ClubId, int? TargetId)>(
                (BadRequest(new { message = "全校通知不需要填写社团或目标 ID。" }), null, null));
        }

        if (!UsersController.IsPlatformAdmin(publisher))
        {
            return Task.FromResult<(IActionResult? Result, int? ClubId, int? TargetId)>(
                (StatusCode(403, new { message = "只有社团管理员或系统管理员可以发布全校通知。" }), null, null));
        }

        return Task.FromResult<(IActionResult?, int?, int?)>((null, null, null));
    }

    private async Task<(IActionResult? Result, int? ClubId, int? TargetId)> ResolveClubTargetAsync(
        ApiCreateNoticeRequest req,
        User publisher)
    {
        if (req.TargetId is null or <= 0)
        {
            return (BadRequest(new { message = "社团通知必须选择目标社团。" }), null, null);
        }

        var club = await _db.Clubs.FindAsync(req.TargetId.Value);
        if (club is null) return (NotFound(new { message = "目标社团不存在。" }), null, null);
        if (!CanPublishForClub(publisher, club.ClubId))
        {
            return (StatusCode(403, new { message = "当前用户没有该社团的通知发布权限。" }), null, null);
        }

        return (null, club.ClubId, club.ClubId);
    }

    private async Task<(IActionResult? Result, int? ClubId, int? TargetId)> ResolveDepartmentTargetAsync(
        ApiCreateNoticeRequest req,
        User publisher)
    {
        if (req.TargetId is null or <= 0)
        {
            return (BadRequest(new { message = "部门通知必须选择一个成员任期作为部门目标。" }), null, null);
        }

        var member = await _db.ClubMembers
            .Include(cm => cm.Club)
            .FirstOrDefaultAsync(cm => cm.MemberId == req.TargetId.Value);
        if (member is null) return (NotFound(new { message = "目标部门样本不存在。" }), null, null);
        if (string.IsNullOrWhiteSpace(member.DepartmentName))
        {
            return (BadRequest(new { message = "目标成员未登记部门，不能作为部门通知目标。" }), null, null);
        }

        if (!CanPublishForClub(publisher, member.ClubId))
        {
            return (StatusCode(403, new { message = "当前用户没有该社团部门的通知发布权限。" }), null, null);
        }

        return (null, member.ClubId, member.MemberId);
    }

    private async Task<(IActionResult? Result, int? ClubId, int? TargetId)> ResolveMemberTargetAsync(
        ApiCreateNoticeRequest req,
        User publisher)
    {
        if (req.TargetId is null or <= 0)
        {
            return (BadRequest(new { message = "成员通知必须选择目标成员。" }), null, null);
        }

        var targetUser = await _db.Users.FindAsync(req.TargetId.Value);
        if (targetUser is null) return (NotFound(new { message = "目标成员用户不存在。" }), null, null);

        if (req.ClubId is null)
        {
            if (!UsersController.IsPlatformAdmin(publisher))
            {
                return (StatusCode(403, new { message = "未指定社团时，只有社团管理员或系统管理员可以定向通知单个成员。" }), null, null);
            }

            return (null, null, targetUser.UserId);
        }

        var club = await _db.Clubs.FindAsync(req.ClubId.Value);
        if (club is null) return (NotFound(new { message = "成员所属社团不存在。" }), null, null);
        if (!CanPublishForClub(publisher, club.ClubId))
        {
            return (StatusCode(403, new { message = "当前用户没有该社团的成员通知发布权限。" }), null, null);
        }

        var today = DateTime.UtcNow.Date;
        var isActiveMember = await _db.ClubMembers.AnyAsync(cm =>
            cm.ClubId == club.ClubId &&
            cm.UserId == targetUser.UserId &&
            (cm.MemberStatus == null ||
             cm.MemberStatus == "" ||
             cm.MemberStatus.ToLower() == "active" ||
             cm.MemberStatus.ToLower() == "normal" ||
             cm.MemberStatus.ToLower() == "enabled") &&
            (cm.TermStart == null || cm.TermStart <= today) &&
            (cm.TermEnd == null || cm.TermEnd >= today));
        if (!isActiveMember)
        {
            return (BadRequest(new { message = "目标用户不是该社团当前有效成员。" }), null, null);
        }

        return (null, club.ClubId, targetUser.UserId);
    }

    private bool CanViewNotice(User viewer, Notice notice, string effectiveStatus, NoticeListContext context)
    {
        if (effectiveStatus == StatusDraft)
        {
            return notice.PublisherUserId == viewer.UserId || CanManageNotice(viewer, notice);
        }

        if (effectiveStatus != StatusPublished)
        {
            return CanManageNotice(viewer, notice);
        }

        return notice.TargetType switch
        {
            TargetSchool => true,
            TargetClub => notice.ClubId is not null && CanAccessClubNotice(viewer, notice.ClubId.Value),
            TargetDepartment => CanAccessDepartmentNotice(viewer, notice, context),
            TargetMember => notice.TargetId == viewer.UserId || CanManageNotice(viewer, notice),
            _ => false
        };
    }

    private bool CanManageNotice(User user, Notice notice)
    {
        if (UsersController.IsPlatformAdmin(user)) return true;
        return notice.ClubId is not null && CanPublishForClub(user, notice.ClubId.Value);
    }

    private bool CanPublishForClub(User user, int clubId)
    {
        if (UsersController.IsPlatformAdmin(user)) return true;
        if (UsersController.IsClubPrincipal(user, clubId)) return true;

        return user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            NoticePublisherRoleCodes.Contains(ur.Role.RoleCode));
    }

    private bool CanAccessClubNotice(User user, int clubId)
    {
        if (UsersController.IsPlatformAdmin(user)) return true;
        if (CanPublishForClub(user, clubId)) return true;

        return user.ClubMemberships.Any(cm => cm.ClubId == clubId && IsActiveMemberTerm(cm)) ||
               user.UserRoles.Any(ur => ur.ClubId == clubId && ur.Role is not null);
    }

    private bool CanAccessDepartmentNotice(User user, Notice notice, NoticeListContext context)
    {
        if (notice.TargetId is null || notice.ClubId is null) return false;
        if (CanManageNotice(user, notice)) return true;

        if (!context.DepartmentTargets.TryGetValue(notice.TargetId.Value, out var target) ||
            string.IsNullOrWhiteSpace(target.DepartmentName))
        {
            return false;
        }

        return user.ClubMemberships.Any(cm =>
            cm.ClubId == target.ClubId &&
            IsActiveMemberTerm(cm) &&
            string.Equals(cm.DepartmentName?.Trim(), target.DepartmentName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private ApiNotice ToApiNotice(
        Notice notice,
        int viewerUserId,
        NoticeListContext context,
        string? effectiveStatus = null)
    {
        effectiveStatus ??= EffectiveStatus(notice, DateTime.UtcNow);
        var read = notice.Reads
            .Where(r => r.UserId == viewerUserId)
            .OrderByDescending(r => r.ReadAt)
            .FirstOrDefault();
        var readCount = notice.Reads.Select(r => r.UserId).Distinct().Count();
        return new ApiNotice
        {
            Id = notice.NoticeId,
            ClubId = notice.ClubId,
            ClubName = notice.Club?.ClubName,
            PublisherUserId = notice.PublisherUserId,
            PublisherName = DisplayUser(notice.Publisher),
            NoticeType = notice.NoticeType ?? "announcement",
            Title = notice.Title,
            Content = notice.Content,
            TargetType = ToApiTargetType(notice.TargetType),
            TargetId = notice.TargetId,
            TargetName = ResolveTargetName(notice, context),
            PublishAt = notice.PublishAt,
            ExpireAt = notice.ExpireAt,
            NoticeStatus = ToApiNoticeStatus(effectiveStatus),
            IsRead = read is not null,
            ReadAt = read?.ReadAt,
            AudienceCount = CountAudience(notice, context),
            ReadCount = readCount
        };
    }

    private static string ResolveTargetName(Notice notice, NoticeListContext context)
    {
        if (notice.TargetType == TargetSchool) return "全校";
        if (notice.TargetType == TargetClub) return notice.Club?.ClubName ?? $"社团 {notice.TargetId}";

        if (notice.TargetType == TargetMember && notice.TargetId is not null)
        {
            context.MemberTargets.TryGetValue(notice.TargetId.Value, out var user);
            var name = DisplayUser(user) ?? $"用户 {notice.TargetId}";
            return notice.Club is null ? name : $"{notice.Club.ClubName} / {name}";
        }

        if (notice.TargetType == TargetDepartment &&
            notice.TargetId is not null &&
            context.DepartmentTargets.TryGetValue(notice.TargetId.Value, out var member))
        {
            return $"{member.Club?.ClubName ?? $"社团 {member.ClubId}"} / {member.DepartmentName ?? "未命名部门"}";
        }

        return "未指定目标";
    }

    private static int? CountAudience(Notice notice, NoticeListContext context)
    {
        if (notice.TargetType == TargetSchool) return context.ActiveUserCount;
        if (notice.TargetType == TargetMember) return notice.TargetId is null ? 0 : 1;
        if (notice.ClubId is null) return null;

        if (notice.TargetType == TargetClub)
        {
            return context.ClubAudienceCounts.GetValueOrDefault(notice.ClubId.Value, 0);
        }

        if (notice.TargetType == TargetDepartment && notice.TargetId is not null)
        {
            if (!context.DepartmentTargets.TryGetValue(notice.TargetId.Value, out var target) ||
                string.IsNullOrWhiteSpace(target.DepartmentName))
            {
                return 0;
            }

            return context.DepartmentAudienceCounts.GetValueOrDefault(
                DepartmentAudienceKey(target.ClubId, target.DepartmentName),
                0);
        }

        return null;
    }

    private static string? ValidateCreateRequest(ApiCreateNoticeRequest req)
    {
        if (req.CurrentUserId <= 0) return "请选择当前发布人。";
        if (string.IsNullOrWhiteSpace(req.NoticeType)) return "通知类型不能为空。";
        if (req.NoticeType.Trim().Length > 50) return "通知类型不能超过 50 个字符。";
        if (string.IsNullOrWhiteSpace(req.Title)) return "通知标题不能为空。";
        if (req.Title.Trim().Length > 120) return "通知标题不能超过 120 个字符。";
        if (string.IsNullOrWhiteSpace(req.Content)) return "通知内容不能为空。";
        if (req.Content.Trim().Length > 4000) return "通知内容不能超过 4000 个字符。";
        if (NormalizeTargetType(req.TargetType) is null) return "定向类型只能是 school、club、department 或 member。";
        if (NormalizePublishStatus(req.NoticeStatus) is null) return "通知状态只能是 draft 或 published。";
        return null;
    }

    private static string? NormalizeTargetType(ApiCreateNoticeRequest.TargetTypeEnum targetType) =>
        targetType switch
        {
            ApiCreateNoticeRequest.TargetTypeEnum.SchoolEnum => TargetSchool,
            ApiCreateNoticeRequest.TargetTypeEnum.ClubEnum => TargetClub,
            ApiCreateNoticeRequest.TargetTypeEnum.DepartmentEnum => TargetDepartment,
            ApiCreateNoticeRequest.TargetTypeEnum.MemberEnum => TargetMember,
            _ => null
        };

    private static string? NormalizeTargetType(string? targetType) =>
        (targetType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            TargetSchool => TargetSchool,
            TargetClub => TargetClub,
            TargetDepartment => TargetDepartment,
            TargetMember => TargetMember,
            _ => null
        };

    private static string? NormalizeStatus(string? status) =>
        status is null
            ? null
            : status.Trim().ToLowerInvariant() switch
            {
                StatusDraft => StatusDraft,
                StatusPublished => StatusPublished,
                StatusExpired => StatusExpired,
                _ => null
            };

    private static string? NormalizePublishStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return StatusPublished;
        return status.Trim().ToLowerInvariant() switch
        {
            StatusDraft => StatusDraft,
            StatusPublished => StatusPublished,
            _ => null
        };
    }

    private static string? NormalizePublishStatus(ApiCreateNoticeRequest.NoticeStatusEnum status) =>
        status switch
        {
            0 => StatusPublished,
            ApiCreateNoticeRequest.NoticeStatusEnum.DraftEnum => StatusDraft,
            ApiCreateNoticeRequest.NoticeStatusEnum.PublishedEnum => StatusPublished,
            _ => null
        };

    private static ApiNotice.TargetTypeEnum ToApiTargetType(string targetType) =>
        targetType switch
        {
            TargetSchool => ApiNotice.TargetTypeEnum.SchoolEnum,
            TargetClub => ApiNotice.TargetTypeEnum.ClubEnum,
            TargetDepartment => ApiNotice.TargetTypeEnum.DepartmentEnum,
            TargetMember => ApiNotice.TargetTypeEnum.MemberEnum,
            _ => throw new InvalidOperationException($"Unknown notice target type '{targetType}'.")
        };

    private static ApiNotice.NoticeStatusEnum ToApiNoticeStatus(string status) =>
        status switch
        {
            StatusDraft => ApiNotice.NoticeStatusEnum.DraftEnum,
            StatusPublished => ApiNotice.NoticeStatusEnum.PublishedEnum,
            StatusExpired => ApiNotice.NoticeStatusEnum.ExpiredEnum,
            _ => throw new InvalidOperationException($"Unknown notice status '{status}'.")
        };

    private static string EffectiveStatus(Notice notice, DateTime now)
    {
        var status = string.IsNullOrWhiteSpace(notice.NoticeStatus)
            ? StatusPublished
            : notice.NoticeStatus.Trim().ToLowerInvariant();
        if (status == StatusPublished && notice.ExpireAt is not null && notice.ExpireAt.Value <= now)
        {
            return StatusExpired;
        }

        return status;
    }

    private sealed class NoticeListContext
    {
        public Dictionary<int, ClubMember> DepartmentTargets { get; private init; } = [];

        public Dictionary<int, User> MemberTargets { get; private init; } = [];

        public Dictionary<int, int> ClubAudienceCounts { get; private init; } = [];

        public Dictionary<string, int> DepartmentAudienceCounts { get; private init; } = [];

        public int ActiveUserCount { get; private init; }

        public static async Task<NoticeListContext> LoadAsync(ClubHubDbContext db, IReadOnlyCollection<Notice> notices)
        {
            var departmentTargetIds = notices
                .Where(n => n.TargetType == TargetDepartment && n.TargetId is not null)
                .Select(n => n.TargetId!.Value)
                .Distinct()
                .ToList();

            var memberTargetIds = notices
                .Where(n => n.TargetType == TargetMember && n.TargetId is not null)
                .Select(n => n.TargetId!.Value)
                .Distinct()
                .ToList();

            var departmentTargets = departmentTargetIds.Count == 0
                ? []
                : await db.ClubMembers
                    .AsNoTracking()
                    .Include(cm => cm.Club)
                    .Where(cm => departmentTargetIds.Contains(cm.MemberId))
                    .ToDictionaryAsync(cm => cm.MemberId);

            var memberTargets = memberTargetIds.Count == 0
                ? []
                : await db.Users
                    .AsNoTracking()
                    .Where(u => memberTargetIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId);

            var needsSchoolCount = notices.Any(n => n.TargetType == TargetSchool);
            var activeUserCount = needsSchoolCount
                ? await db.Users.CountAsync(u =>
                    u.AccountStatus == null ||
                    u.AccountStatus == "" ||
                    u.AccountStatus.ToLower() == "active" ||
                    u.AccountStatus.ToLower() == "normal" ||
                    u.AccountStatus.ToLower() == "enabled")
                : 0;

            var clubIds = notices
                .Where(n => n.ClubId is not null && (n.TargetType == TargetClub || n.TargetType == TargetDepartment))
                .Select(n => n.ClubId!.Value)
                .Concat(departmentTargets.Values.Select(cm => cm.ClubId))
                .Distinct()
                .ToList();

            var activeMembers = clubIds.Count == 0
                ? []
                : await LoadActiveClubMembersAsync(db, clubIds);

            var clubAudienceCounts = activeMembers
                .GroupBy(cm => cm.ClubId)
                .ToDictionary(g => g.Key, g => g.Select(cm => cm.UserId).Distinct().Count());

            var departmentAudienceCounts = activeMembers
                .Where(cm => !string.IsNullOrWhiteSpace(cm.DepartmentName))
                .GroupBy(cm => DepartmentAudienceKey(cm.ClubId, cm.DepartmentName!))
                .ToDictionary(g => g.Key, g => g.Select(cm => cm.UserId).Distinct().Count());

            return new NoticeListContext
            {
                DepartmentTargets = departmentTargets,
                MemberTargets = memberTargets,
                ActiveUserCount = activeUserCount,
                ClubAudienceCounts = clubAudienceCounts,
                DepartmentAudienceCounts = departmentAudienceCounts
            };
        }

        private static async Task<List<ClubMember>> LoadActiveClubMembersAsync(ClubHubDbContext db, IReadOnlyCollection<int> clubIds)
        {
            var today = DateTime.UtcNow.Date;
            return await db.ClubMembers
                .AsNoTracking()
                .Where(cm => clubIds.Contains(cm.ClubId))
                .Where(cm =>
                    cm.MemberStatus == null ||
                    cm.MemberStatus == "" ||
                    cm.MemberStatus.ToLower() == "active" ||
                    cm.MemberStatus.ToLower() == "normal" ||
                    cm.MemberStatus.ToLower() == "enabled")
                .Where(cm => cm.TermStart == null || cm.TermStart <= today)
                .Where(cm => cm.TermEnd == null || cm.TermEnd >= today)
                .ToListAsync();
        }
    }

    private static string DepartmentAudienceKey(int clubId, string departmentName) =>
        $"{clubId}:{departmentName.Trim().ToUpperInvariant()}";

    private static bool IsActiveMemberTerm(ClubMember member)
    {
        var today = DateTime.UtcNow.Date;
        return UsersController.IsActive(member.MemberStatus ?? MemberActive) &&
               (member.TermStart is null || member.TermStart.Value.Date <= today) &&
               (member.TermEnd is null || member.TermEnd.Value.Date >= today);
    }

    private static string? DisplayUser(User? user)
    {
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(user.RealName)) return user.RealName;
        if (!string.IsNullOrWhiteSpace(user.Username)) return user.Username;
        return $"用户 {user.UserId}";
    }
}
