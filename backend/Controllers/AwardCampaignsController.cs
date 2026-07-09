using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api")]
public class AwardCampaignsController : ControllerBase
{
    private const string EvaluationAwardCampaign = "award_campaign";
    private const string EvaluationAwardApplication = "award_application";
    private const string CampaignOpen = "open";
    private const string CampaignClosed = "closed";
    private const string CampaignPublished = "published";
    private const string ApplicationSubmitted = "submitted";
    private const string ApplicationLeaderApproved = "leader_approved";
    private const string ApplicationAdvisorApproved = "advisor_approved";
    private const string ApplicationRejected = "rejected";
    private const string ApplicationPublished = "published";
    private const string ClubActive = "active";
    private const string MemberActive = "active";

    private readonly ClubHubDbContext _db;

    public AwardCampaignsController(ClubHubDbContext db) => _db = db;

    [HttpGet("award-campaigns")]
    public async Task<IActionResult> GetCampaigns(
        [FromQuery] int viewerUserId,
        [FromQuery] int? clubId,
        [FromQuery] string? status)
    {
        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null) return NotFound(new { message = "当前用户不存在。" });
        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能查看评优评奖活动。" });
        }

        if (clubId is not null and <= 0)
        {
            return BadRequest(new { message = "社团 ID 必须大于 0。" });
        }

        var normalizedStatus = NormalizeCampaignStatus(status);
        if (!string.IsNullOrWhiteSpace(status) && normalizedStatus is null)
        {
            return BadRequest(new { message = "活动状态只能是 open、closed 或 published。" });
        }

        var campaigns = await CampaignQuery()
            .Where(ev => clubId == null || ev.ClubId == clubId.Value)
            .Where(ev => normalizedStatus == null || ev.PublicStatus == normalizedStatus)
            .OrderByDescending(ev => ev.CreatedAt)
            .ThenByDescending(ev => ev.EvaluationId)
            .ToListAsync();

        var visibleCampaigns = campaigns
            .Where(campaign => CanViewCampaign(viewer, campaign))
            .ToList();

        var campaignIds = visibleCampaigns.Select(campaign => campaign.EvaluationId).ToList();
        var applicationCounts = await LoadApplicationCountsAsync(campaignIds);

        return Ok(visibleCampaigns
            .Select(campaign => ToCampaignDto(campaign, applicationCounts))
            .ToList());
    }

    [HttpPost("award-campaigns")]
    public async Task<IActionResult> CreateCampaign([FromBody] CreateAwardCampaignRequest? req)
    {
        if (req is null) return BadRequest(new { message = "请求体不能为空。" });

        var validation = ValidateCampaignRequest(req);
        if (validation is not null) return BadRequest(new { message = validation });

        var publisher = await LoadUserAsync(req.CurrentUserId);
        if (publisher is null) return NotFound(new { message = "当前用户不存在。" });
        if (!UsersController.IsActive(publisher.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能发布评优评奖活动。" });
        }

        var club = await _db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.ClubId == req.ClubId);
        if (club is null) return NotFound(new { message = "社团不存在。" });
        if (!string.Equals(club.ClubStatus, ClubActive, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { message = "只有运营中的社团可以发布评优评奖活动。" });
        }

        if (!CanMaintainCampaign(publisher, req.ClubId))
        {
            return StatusCode(403, new { message = "只有本社团负责人或管理员可以发布评优评奖活动。" });
        }

        var nextId = (await _db.Evaluations.MaxAsync(ev => (int?)ev.EvaluationId) ?? 0) + 1;
        var campaign = new Evaluation
        {
            EvaluationId = nextId,
            EvaluationType = EvaluationAwardCampaign,
            ClubId = req.ClubId,
            UserId = req.CurrentUserId,
            EvaluatorUserId = req.CurrentUserId,
            TermName = req.TermName.Trim(),
            AwardTitle = req.Title.Trim(),
            AwardLevel = EmptyToNull(req.AwardType),
            AwardReason = EmptyToNull(req.Description),
            ActivityScore = 0,
            TaskScore = 0,
            LearningScore = 0,
            AwardScore = 0,
            TotalScore = 0,
            PublicStatus = NormalizeCampaignStatus(req.CampaignStatus) ?? CampaignOpen,
            CreatedAt = DateTime.UtcNow
        };

        _db.Evaluations.Add(campaign);
        await _db.SaveChangesAsync();

        var created = await CampaignQuery().FirstAsync(ev => ev.EvaluationId == campaign.EvaluationId);
        return Created(
            $"/api/award-campaigns/{campaign.EvaluationId}",
            ToCampaignDto(created, new Dictionary<int, AwardApplicationCounts>()));
    }

    [HttpGet("award-campaigns/{campaignId:int}/applications")]
    public async Task<IActionResult> GetApplications(int campaignId, [FromQuery] int viewerUserId)
    {
        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null) return NotFound(new { message = "当前用户不存在。" });
        if (!UsersController.IsActive(viewer.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能查看评优评奖申报。" });
        }

        var campaign = await CampaignQuery().FirstOrDefaultAsync(ev => ev.EvaluationId == campaignId);
        if (campaign is null) return NotFound(new { message = "评优评奖活动不存在。" });
        if (!CanViewCampaign(viewer, campaign))
        {
            return StatusCode(403, new { message = "当前用户无权查看该评优评奖活动。" });
        }

        var applications = await ApplicationQuery()
            .Where(ev => ev.TermName == CampaignLink(campaignId))
            .OrderByDescending(ev => ev.CreatedAt)
            .ThenByDescending(ev => ev.EvaluationId)
            .ToListAsync();

        if (!CanReviewApplications(viewer, campaign.ClubId))
        {
            applications = applications
                .Where(application => application.UserId == viewer.UserId)
                .ToList();
        }

        return Ok(applications.Select(application => ToApplicationDto(application, campaign)).ToList());
    }

    [HttpPost("award-campaigns/{campaignId:int}/apply")]
    public async Task<IActionResult> Apply(int campaignId, [FromBody] CreateAwardApplicationRequest? req)
    {
        if (req is null) return BadRequest(new { message = "请求体不能为空。" });
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请选择当前申报人。" });
        if (string.IsNullOrWhiteSpace(req.ApplyReason))
        {
            return BadRequest(new { message = "申报理由不能为空。" });
        }

        var campaign = await CampaignQuery().FirstOrDefaultAsync(ev => ev.EvaluationId == campaignId);
        if (campaign is null) return NotFound(new { message = "评优评奖活动不存在。" });
        if (!string.Equals(campaign.PublicStatus, CampaignOpen, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { message = "只有开放申报中的评优评奖活动可以提交申报。" });
        }

        var applicant = await LoadUserAsync(req.CurrentUserId);
        if (applicant is null) return NotFound(new { message = "当前用户不存在。" });
        if (!UsersController.IsActive(applicant.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能提交评优评奖申报。" });
        }

        var membership = await LoadCurrentClubMemberAsync(campaign.ClubId, req.CurrentUserId);
        if (membership is null)
        {
            return StatusCode(403, new { message = "只有本社团在任成员可以提交评优评奖申报。" });
        }

        var hasExisting = await _db.Evaluations.AnyAsync(ev =>
            ev.EvaluationType == EvaluationAwardApplication &&
            ev.TermName == CampaignLink(campaignId) &&
            ev.UserId == req.CurrentUserId);
        if (hasExisting)
        {
            return Conflict(new { message = "你已经提交过该评优评奖活动的申报。" });
        }

        var nextId = (await _db.Evaluations.MaxAsync(ev => (int?)ev.EvaluationId) ?? 0) + 1;
        var application = new Evaluation
        {
            EvaluationId = nextId,
            EvaluationType = EvaluationAwardApplication,
            ClubId = campaign.ClubId,
            UserId = req.CurrentUserId,
            TermName = CampaignLink(campaignId),
            AwardTitle = campaign.AwardTitle,
            AwardLevel = EmptyToNull(req.AwardLevel) ?? campaign.AwardLevel,
            AwardReason = req.ApplyReason.Trim(),
            ActivityScore = 0,
            TaskScore = 0,
            LearningScore = 0,
            AwardScore = 0,
            TotalScore = 0,
            PublicStatus = ApplicationSubmitted,
            CreatedAt = DateTime.UtcNow
        };

        _db.Evaluations.Add(application);
        await _db.SaveChangesAsync();

        var created = await ApplicationQuery().FirstAsync(ev => ev.EvaluationId == application.EvaluationId);
        return Created(
            $"/api/award-campaigns/{campaignId}/applications?viewerUserId={req.CurrentUserId}",
            ToApplicationDto(created, campaign));
    }

    [HttpPost("award-applications/{applicationId:int}/review")]
    public async Task<IActionResult> ReviewApplication(int applicationId, [FromBody] ReviewAwardApplicationRequest? req)
    {
        if (req is null) return BadRequest(new { message = "请求体不能为空。" });
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请选择当前审核人。" });

        var decision = NormalizeReviewDecision(req.Decision);
        if (decision is null)
        {
            return BadRequest(new { message = "审核结果只能是 approved 或 rejected。" });
        }

        if (decision == ApplicationRejected && string.IsNullOrWhiteSpace(req.ReviewComment))
        {
            return BadRequest(new { message = "退回申报时必须填写审核意见。" });
        }

        var application = await ApplicationQuery().FirstOrDefaultAsync(ev => ev.EvaluationId == applicationId);
        if (application is null) return NotFound(new { message = "评优评奖申报不存在。" });

        var campaignId = ParseCampaignId(application.TermName);
        if (campaignId is null) return Conflict(new { message = "申报记录缺少关联活动，无法审核。" });

        var campaign = await CampaignQuery().FirstOrDefaultAsync(ev => ev.EvaluationId == campaignId.Value);
        if (campaign is null) return NotFound(new { message = "评优评奖活动不存在。" });

        var reviewer = await LoadUserAsync(req.CurrentUserId);
        if (reviewer is null) return NotFound(new { message = "当前用户不存在。" });
        if (!UsersController.IsActive(reviewer.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能审核评优评奖申报。" });
        }

        var currentStatus = NormalizeApplicationStatus(application.PublicStatus) ?? ApplicationSubmitted;
        var nextStatus = NextReviewStatus(currentStatus, decision, reviewer, campaign.ClubId);
        if (nextStatus is null)
        {
            return StatusCode(403, new { message = "当前用户无权处理该阶段的评优评奖申报。" });
        }

        application.PublicStatus = nextStatus;
        application.EvaluatorUserId = req.CurrentUserId;
        application.CommentText = EmptyToNull(req.ReviewComment);

        await _db.SaveChangesAsync();
        return Ok(ToApplicationDto(application, campaign));
    }

    [HttpPost("award-campaigns/{campaignId:int}/publish")]
    public async Task<IActionResult> PublishCampaign(int campaignId, [FromBody] PublishAwardCampaignRequest? req)
    {
        if (req is null) return BadRequest(new { message = "请求体不能为空。" });
        if (req.CurrentUserId <= 0) return BadRequest(new { message = "请选择当前操作人。" });

        var campaign = await CampaignQuery().FirstOrDefaultAsync(ev => ev.EvaluationId == campaignId);
        if (campaign is null) return NotFound(new { message = "评优评奖活动不存在。" });

        var publisher = await LoadUserAsync(req.CurrentUserId);
        if (publisher is null) return NotFound(new { message = "当前用户不存在。" });
        if (!UsersController.IsActive(publisher.AccountStatus))
        {
            return BadRequest(new { message = "当前用户账号不可用，不能公示评优评奖结果。" });
        }

        if (!CanPublishCampaign(publisher, campaign.ClubId))
        {
            return StatusCode(403, new { message = "只有本社团负责人、指导老师或管理员可以公示评优评奖结果。" });
        }

        var approvedApplications = await _db.Evaluations
            .Where(ev =>
                ev.EvaluationType == EvaluationAwardApplication &&
                ev.TermName == CampaignLink(campaignId) &&
                ev.PublicStatus == ApplicationAdvisorApproved)
            .ToListAsync();

        campaign.PublicStatus = CampaignPublished;
        campaign.EvaluatorUserId = req.CurrentUserId;
        foreach (var application in approvedApplications)
        {
            application.PublicStatus = ApplicationPublished;
            application.EvaluatorUserId = req.CurrentUserId;
        }

        await _db.SaveChangesAsync();

        var updated = await CampaignQuery().FirstAsync(ev => ev.EvaluationId == campaignId);
        var counts = await LoadApplicationCountsAsync(new[] { campaignId });
        return Ok(ToCampaignDto(updated, counts));
    }

    private IQueryable<Evaluation> CampaignQuery() =>
        _db.Evaluations
            .Include(ev => ev.Club)
            .Include(ev => ev.User)
            .Where(ev => ev.EvaluationType == EvaluationAwardCampaign);

    private IQueryable<Evaluation> ApplicationQuery() =>
        _db.Evaluations
            .Include(ev => ev.Club)
            .Include(ev => ev.User)
            .ThenInclude(user => user!.ClubMemberships)
            .Include(ev => ev.Evaluator)
            .Where(ev => ev.EvaluationType == EvaluationAwardApplication);

    private async Task<User?> LoadUserAsync(int userId) =>
        await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId);

    private async Task<ClubMember?> LoadCurrentClubMemberAsync(int clubId, int userId) =>
        await _db.ClubMembers
            .AsNoTracking()
            .Include(cm => cm.User)
            .Where(cm =>
                cm.ClubId == clubId &&
                cm.UserId == userId &&
                (cm.MemberStatus == null || cm.MemberStatus == MemberActive))
            .OrderByDescending(cm => cm.TermStart)
            .ThenByDescending(cm => cm.JoinAt)
            .FirstOrDefaultAsync();

    private async Task<Dictionary<int, AwardApplicationCounts>> LoadApplicationCountsAsync(IEnumerable<int> campaignIds)
    {
        var links = campaignIds.Select(CampaignLink).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (links.Count == 0) return new Dictionary<int, AwardApplicationCounts>();

        var applications = await _db.Evaluations
            .AsNoTracking()
            .Where(ev => ev.EvaluationType == EvaluationAwardApplication && ev.TermName != null && links.Contains(ev.TermName))
            .Select(ev => new { ev.TermName, ev.PublicStatus })
            .ToListAsync();

        return applications
            .Select(item => new { CampaignId = ParseCampaignId(item.TermName), Status = item.PublicStatus })
            .Where(item => item.CampaignId is not null)
            .GroupBy(item => item.CampaignId!.Value)
            .ToDictionary(
                group => group.Key,
                group => new AwardApplicationCounts(
                    group.Count(),
                    group.Count(item => NormalizeApplicationStatus(item.Status) == ApplicationSubmitted),
                    group.Count(item => NormalizeApplicationStatus(item.Status) is ApplicationLeaderApproved or ApplicationAdvisorApproved),
                    group.Count(item => NormalizeApplicationStatus(item.Status) == ApplicationPublished)));
    }

    private static bool CanViewCampaign(User user, Evaluation campaign) =>
        UsersController.IsPlatformAdmin(user) ||
        UsersController.IsSystemAdmin(user) ||
        UsersController.IsClubPrincipal(user, campaign.ClubId) ||
        UsersController.IsClubOfficer(user, campaign.ClubId) ||
        HasAdvisorRole(user, campaign.ClubId) ||
        user.ClubMemberships.Any(cm => cm.ClubId == campaign.ClubId && UsersController.IsActive(cm.MemberStatus));

    private static bool CanMaintainCampaign(User user, int clubId) =>
        UsersController.IsPlatformAdmin(user) ||
        UsersController.IsSystemAdmin(user) ||
        UsersController.IsClubPrincipal(user, clubId);

    private static bool CanReviewApplications(User user, int clubId) =>
        CanMaintainCampaign(user, clubId) ||
        UsersController.IsClubOfficer(user, clubId) ||
        HasAdvisorRole(user, clubId);

    private static bool CanPublishCampaign(User user, int clubId) =>
        CanMaintainCampaign(user, clubId) || HasAdvisorRole(user, clubId);

    private static bool HasAdvisorRole(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            Normalize(ur.Role.RoleCode) is "advisor" or "club_advisor" or "teacher_advisor") ||
        user.ClubMemberships.Any(cm =>
            cm.ClubId == clubId &&
            UsersController.IsActive(cm.MemberStatus) &&
            Normalize(cm.PositionName) is "advisor" or "teacher" or "指导老师" or "指導老師");

    private static string? NextReviewStatus(string currentStatus, string decision, User reviewer, int clubId)
    {
        if (currentStatus is ApplicationRejected or ApplicationPublished) return null;
        if (decision == ApplicationRejected)
        {
            return CanReviewApplications(reviewer, clubId) ? ApplicationRejected : null;
        }

        if (currentStatus == ApplicationSubmitted)
        {
            return CanMaintainCampaign(reviewer, clubId) || UsersController.IsClubOfficer(reviewer, clubId)
                ? ApplicationLeaderApproved
                : null;
        }

        if (currentStatus == ApplicationLeaderApproved)
        {
            return HasAdvisorRole(reviewer, clubId) ||
                   UsersController.IsPlatformAdmin(reviewer) ||
                   UsersController.IsSystemAdmin(reviewer)
                ? ApplicationAdvisorApproved
                : null;
        }

        return null;
    }

    private static string? ValidateCampaignRequest(CreateAwardCampaignRequest req)
    {
        if (req.CurrentUserId <= 0) return "请选择当前发布人。";
        if (req.ClubId <= 0) return "请选择发布社团。";
        if (string.IsNullOrWhiteSpace(req.Title)) return "评优评奖标题不能为空。";
        if (req.Title.Trim().Length > 100) return "评优评奖标题不能超过 100 个字符。";
        if (string.IsNullOrWhiteSpace(req.TermName)) return "评优评奖学期不能为空。";
        if (req.TermName.Trim().Length > 100) return "评优评奖学期不能超过 100 个字符。";
        if (req.AwardType is not null && req.AwardType.Length > 100) return "评奖类型不能超过 100 个字符。";
        if (req.CampaignStatus is not null && NormalizeCampaignStatus(req.CampaignStatus) is null)
        {
            return "活动状态只能是 open、closed 或 published。";
        }

        return null;
    }

    private static AwardCampaignRecordDto ToCampaignDto(
        Evaluation campaign,
        IReadOnlyDictionary<int, AwardApplicationCounts> countsByCampaign)
    {
        countsByCampaign.TryGetValue(campaign.EvaluationId, out var counts);
        counts ??= new AwardApplicationCounts(0, 0, 0, 0);
        var status = NormalizeCampaignStatus(campaign.PublicStatus) ?? CampaignOpen;

        return new AwardCampaignRecordDto(
            campaign.EvaluationId,
            campaign.ClubId,
            campaign.Club?.ClubName ?? $"社团 {campaign.ClubId}",
            campaign.AwardTitle ?? $"评优评奖活动 {campaign.EvaluationId}",
            campaign.AwardLevel,
            campaign.TermName ?? "-",
            campaign.AwardReason,
            campaign.UserId,
            DisplayUser(campaign.User),
            status,
            CampaignStatusText(status),
            campaign.CreatedAt,
            counts.Total,
            counts.Submitted,
            counts.Approved,
            counts.Published);
    }

    private static AwardApplicationRecordDto ToApplicationDto(Evaluation application, Evaluation campaign)
    {
        var status = NormalizeApplicationStatus(application.PublicStatus) ?? ApplicationSubmitted;
        var member = application.User?.ClubMemberships
            .Where(cm => cm.ClubId == campaign.ClubId)
            .OrderByDescending(cm => cm.TermStart)
            .ThenByDescending(cm => cm.JoinAt)
            .FirstOrDefault();

        return new AwardApplicationRecordDto(
            application.EvaluationId,
            campaign.EvaluationId,
            campaign.ClubId,
            campaign.Club?.ClubName ?? $"社团 {campaign.ClubId}",
            campaign.AwardTitle ?? $"评优评奖活动 {campaign.EvaluationId}",
            application.AwardLevel,
            campaign.TermName ?? "-",
            application.UserId,
            DisplayUser(application.User),
            application.User?.StudentNo,
            member?.DepartmentName,
            member?.GroupName,
            member?.PositionName,
            application.AwardReason ?? string.Empty,
            status,
            ApplicationStatusText(status),
            application.EvaluatorUserId,
            DisplayUser(application.Evaluator),
            application.CommentText,
            application.CreatedAt);
    }

    private static string CampaignLink(int campaignId) => $"campaign:{campaignId}";

    private static int? ParseCampaignId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        const string prefix = "campaign:";
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(value[prefix.Length..], out var campaignId)
            ? campaignId
            : null;
    }

    private static string? NormalizeCampaignStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        return Normalize(status) switch
        {
            CampaignOpen => CampaignOpen,
            CampaignClosed => CampaignClosed,
            CampaignPublished => CampaignPublished,
            _ => null
        };
    }

    private static string? NormalizeApplicationStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        return Normalize(status) switch
        {
            ApplicationSubmitted => ApplicationSubmitted,
            ApplicationLeaderApproved => ApplicationLeaderApproved,
            ApplicationAdvisorApproved => ApplicationAdvisorApproved,
            ApplicationRejected => ApplicationRejected,
            ApplicationPublished => ApplicationPublished,
            _ => null
        };
    }

    private static string? NormalizeReviewDecision(string? decision)
    {
        if (string.IsNullOrWhiteSpace(decision)) return null;
        return Normalize(decision) switch
        {
            "approved" => "approved",
            "rejected" => ApplicationRejected,
            _ => null
        };
    }

    private static string CampaignStatusText(string status) =>
        status switch
        {
            CampaignOpen => "申报中",
            CampaignClosed => "已关闭",
            CampaignPublished => "已公示",
            _ => "未知"
        };

    private static string ApplicationStatusText(string status) =>
        status switch
        {
            ApplicationSubmitted => "待负责人初审",
            ApplicationLeaderApproved => "待指导老师终审",
            ApplicationAdvisorApproved => "终审通过",
            ApplicationRejected => "已退回",
            ApplicationPublished => "已公示",
            _ => "未知"
        };

    private static string DisplayUser(User? user)
    {
        if (user is null) return "-";
        var name = !string.IsNullOrWhiteSpace(user.RealName) ? user.RealName : user.Username;
        return string.IsNullOrWhiteSpace(user.StudentNo) ? name : $"{name}（{user.StudentNo}）";
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}

public record AwardCampaignRecordDto(
    int CampaignId,
    int ClubId,
    string ClubName,
    string Title,
    string? AwardType,
    string TermName,
    string? Description,
    int PublisherUserId,
    string PublisherName,
    string CampaignStatus,
    string CampaignStatusText,
    DateTime? CreatedAt,
    int ApplicationCount,
    int SubmittedCount,
    int ApprovedCount,
    int PublishedCount);

public record AwardApplicationRecordDto(
    int ApplicationId,
    int CampaignId,
    int ClubId,
    string ClubName,
    string Title,
    string? AwardLevel,
    string TermName,
    int ApplicantUserId,
    string ApplicantName,
    string? StudentNo,
    string? DepartmentName,
    string? GroupName,
    string? PositionName,
    string ApplyReason,
    string ApplicationStatus,
    string ApplicationStatusText,
    int? ReviewerUserId,
    string? ReviewerName,
    string? ReviewComment,
    DateTime? CreatedAt);

public record AwardApplicationCounts(int Total, int Submitted, int Approved, int Published);

public class CreateAwardCampaignRequest
{
    public int CurrentUserId { get; set; }
    public int ClubId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? AwardType { get; set; }
    public string TermName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CampaignStatus { get; set; } = "open";
}

public class CreateAwardApplicationRequest
{
    public int CurrentUserId { get; set; }
    public string? AwardLevel { get; set; }
    public string ApplyReason { get; set; } = string.Empty;
}

public class ReviewAwardApplicationRequest
{
    public int CurrentUserId { get; set; }
    public string? Decision { get; set; }
    public string? ReviewComment { get; set; }
}

public class PublishAwardCampaignRequest
{
    public int CurrentUserId { get; set; }
}
