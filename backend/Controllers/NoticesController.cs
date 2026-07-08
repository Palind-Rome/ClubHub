using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        var result = new List<NoticeDto>();

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

            if (!await CanViewNoticeAsync(viewer, notice, effectiveStatus))
            {
                continue;
            }

            var dto = await ToNoticeDtoAsync(notice, viewer.UserId, effectiveStatus);
            if (unreadOnly && dto.IsRead) continue;
            result.Add(dto);
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNoticeRequest req)
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
        return CreatedAtAction(nameof(GetAll), new { viewerUserId = publisher.UserId }, await ToNoticeDtoAsync(created, publisher.UserId));
    }

    [HttpPost("{noticeId:int}/reads")]
    public async Task<IActionResult> MarkRead(int noticeId, [FromBody] MarkNoticeReadRequest req)
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
        if (!await CanViewNoticeAsync(user, notice, effectiveStatus))
        {
            return StatusCode(403, new { message = "当前用户不可阅读该通知。" });
        }

        var existing = await _db.NoticeReads.FirstOrDefaultAsync(r =>
            r.NoticeId == noticeId &&
            r.UserId == user.UserId);
        if (existing is not null)
        {
            return Ok(new NoticeReadResultDto(noticeId, user.UserId, true, existing.ReadAt));
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

        return Ok(new NoticeReadResultDto(noticeId, user.UserId, true, now));
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
        CreateNoticeRequest req,
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
        CreateNoticeRequest req,
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
        CreateNoticeRequest req,
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
        CreateNoticeRequest req,
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
        CreateNoticeRequest req,
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

    private async Task<bool> CanViewNoticeAsync(User viewer, Notice notice, string effectiveStatus)
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
            TargetDepartment => await CanAccessDepartmentNoticeAsync(viewer, notice),
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

    private async Task<bool> CanAccessDepartmentNoticeAsync(User user, Notice notice)
    {
        if (notice.TargetId is null || notice.ClubId is null) return false;
        if (CanManageNotice(user, notice)) return true;

        var target = await _db.ClubMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.MemberId == notice.TargetId.Value);
        if (target is null || string.IsNullOrWhiteSpace(target.DepartmentName)) return false;

        return user.ClubMemberships.Any(cm =>
            cm.ClubId == target.ClubId &&
            IsActiveMemberTerm(cm) &&
            string.Equals(cm.DepartmentName?.Trim(), target.DepartmentName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<NoticeDto> ToNoticeDtoAsync(Notice notice, int viewerUserId, string? effectiveStatus = null)
    {
        effectiveStatus ??= EffectiveStatus(notice, DateTime.UtcNow);
        var read = notice.Reads
            .Where(r => r.UserId == viewerUserId)
            .OrderByDescending(r => r.ReadAt)
            .FirstOrDefault();
        var readCount = notice.Reads.Select(r => r.UserId).Distinct().Count();
        var targetName = await ResolveTargetNameAsync(notice);
        var audienceCount = await CountAudienceAsync(notice);

        return new NoticeDto(
            notice.NoticeId,
            notice.ClubId,
            notice.Club?.ClubName,
            notice.PublisherUserId,
            DisplayUser(notice.Publisher),
            notice.NoticeType ?? "announcement",
            notice.Title,
            notice.Content,
            notice.TargetType,
            notice.TargetId,
            targetName,
            notice.PublishAt,
            notice.ExpireAt,
            effectiveStatus,
            read is not null,
            read?.ReadAt,
            audienceCount,
            readCount);
    }

    private async Task<string> ResolveTargetNameAsync(Notice notice)
    {
        if (notice.TargetType == TargetSchool) return "全校";
        if (notice.TargetType == TargetClub) return notice.Club?.ClubName ?? $"社团 {notice.TargetId}";

        if (notice.TargetType == TargetMember && notice.TargetId is not null)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == notice.TargetId.Value);
            var name = DisplayUser(user) ?? $"用户 {notice.TargetId}";
            return notice.Club is null ? name : $"{notice.Club.ClubName} / {name}";
        }

        if (notice.TargetType == TargetDepartment && notice.TargetId is not null)
        {
            var member = await _db.ClubMembers
                .AsNoTracking()
                .Include(cm => cm.Club)
                .FirstOrDefaultAsync(cm => cm.MemberId == notice.TargetId.Value);
            if (member is not null)
            {
                return $"{member.Club?.ClubName ?? $"社团 {member.ClubId}"} / {member.DepartmentName ?? "未命名部门"}";
            }
        }

        return "未指定目标";
    }

    private async Task<int?> CountAudienceAsync(Notice notice)
    {
        if (notice.TargetType == TargetSchool)
        {
            return await _db.Users.CountAsync(u =>
                u.AccountStatus == null ||
                u.AccountStatus == "" ||
                u.AccountStatus.ToLower() == "active" ||
                u.AccountStatus.ToLower() == "normal" ||
                u.AccountStatus.ToLower() == "enabled");
        }

        if (notice.TargetType == TargetMember)
        {
            return notice.TargetId is null ? 0 : 1;
        }

        if (notice.ClubId is null) return null;

        if (notice.TargetType == TargetClub)
        {
            var today = DateTime.UtcNow.Date;
            return await _db.ClubMembers
                .Where(cm => cm.ClubId == notice.ClubId.Value)
                .Where(cm =>
                    cm.MemberStatus == null ||
                    cm.MemberStatus == "" ||
                    cm.MemberStatus.ToLower() == "active" ||
                    cm.MemberStatus.ToLower() == "normal" ||
                    cm.MemberStatus.ToLower() == "enabled")
                .Where(cm => cm.TermStart == null || cm.TermStart <= today)
                .Where(cm => cm.TermEnd == null || cm.TermEnd >= today)
                .Select(cm => cm.UserId)
                .Distinct()
                .CountAsync();
        }

        if (notice.TargetType == TargetDepartment && notice.TargetId is not null)
        {
            var target = await _db.ClubMembers.AsNoTracking().FirstOrDefaultAsync(cm => cm.MemberId == notice.TargetId.Value);
            if (target is null || string.IsNullOrWhiteSpace(target.DepartmentName)) return 0;
            var departmentName = target.DepartmentName.Trim();
            var today = DateTime.UtcNow.Date;

            return await _db.ClubMembers
                .Where(cm => cm.ClubId == target.ClubId)
                .Where(cm =>
                    cm.MemberStatus == null ||
                    cm.MemberStatus == "" ||
                    cm.MemberStatus.ToLower() == "active" ||
                    cm.MemberStatus.ToLower() == "normal" ||
                    cm.MemberStatus.ToLower() == "enabled")
                .Where(cm => cm.TermStart == null || cm.TermStart <= today)
                .Where(cm => cm.TermEnd == null || cm.TermEnd >= today)
                .Where(cm => cm.DepartmentName != null && cm.DepartmentName.ToUpper() == departmentName.ToUpper())
                .Select(cm => cm.UserId)
                .Distinct()
                .CountAsync();
        }

        return null;
    }

    private static string? ValidateCreateRequest(CreateNoticeRequest req)
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

public record NoticeDto(
    int Id,
    int? ClubId,
    string? ClubName,
    int PublisherUserId,
    string? PublisherName,
    string NoticeType,
    string Title,
    string Content,
    string TargetType,
    int? TargetId,
    string? TargetName,
    DateTime PublishAt,
    DateTime? ExpireAt,
    string NoticeStatus,
    bool IsRead,
    DateTime? ReadAt,
    int? AudienceCount,
    int ReadCount);

public class CreateNoticeRequest
{
    public int CurrentUserId { get; set; }
    public string NoticeType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public int? ClubId { get; set; }
    public int? TargetId { get; set; }
    public DateTime? ExpireAt { get; set; }
    public string? NoticeStatus { get; set; }
}

public class MarkNoticeReadRequest
{
    public int CurrentUserId { get; set; }
}

public record NoticeReadResultDto(
    int NoticeId,
    int UserId,
    bool IsRead,
    DateTime ReadAt);
