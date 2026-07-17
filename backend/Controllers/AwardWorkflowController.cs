using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CreateAwardApplicationRequest = Org.OpenAPITools.Models.CreateAwardApplicationRequest;
using CreateAwardPublicityBatchRequest = Org.OpenAPITools.Models.CreateAwardPublicityBatchRequest;
using CreateAwardSchemeRequest = Org.OpenAPITools.Models.CreateAwardSchemeRequest;
using ReviewAwardApplicationRequest = Org.OpenAPITools.Models.ReviewAwardApplicationRequest;
using UpdateAwardApplicationRequest = Org.OpenAPITools.Models.UpdateAwardApplicationRequest;
using UpdateAwardSchemeRequest = Org.OpenAPITools.Models.UpdateAwardSchemeRequest;

namespace ClubHub.Api.Controllers;

[ApiController]
[Route("api/clubs/{clubId:int}")]
[Authorize]
public class AwardWorkflowController : ControllerBase
{
    private const string AuditApproved = "approved";
    private const string ClubActive = "active";
    private const string MemberActive = "active";
    private const string AwardSchemeDraft = "draft";
    private const string AwardSchemeOpen = "open";
    private const string AwardSchemeReviewing = "reviewing";
    private const string AwardSchemePublicizing = "publicizing";
    private const string AwardSchemeArchived = "archived";
    private const string AwardSchemeClosed = "closed";
    private const string AwardApplicationSelf = "self";
    private const string AwardApplicationRecommendation = "recommendation";
    private const string AwardStepStudentSubmit = "student_submit";
    private const string AwardStepClubReview = "club_review";
    private const string AwardStepAdvisorReview = "advisor_review";
    private const string AwardStepSchoolReview = "school_review";
    private const string AwardStepPublicity = "publicity";
    private const string AwardStepArchived = "archived";
    private const string AwardReviewStepArchive = "archive";
    private const string AwardStatusDraft = "draft";
    private const string AwardStatusClubReview = "club_review";
    private const string AwardStatusAdvisorReview = "advisor_review";
    private const string AwardStatusSchoolReview = "school_review";
    private const string AwardStatusReturned = "returned";
    private const string AwardStatusRejected = "rejected";
    private const string AwardStatusApproved = "approved";
    private const string AwardStatusPublicizing = "publicizing";
    private const string AwardStatusArchived = "archived";
    private const string AwardStatusWithdrawn = "withdrawn";
    private const string AwardPublicNone = "none";
    private const string AwardPublicizing = "publicizing";
    private const string AwardPublicized = "publicized";
    private const string AwardPublicWithdrawn = "withdrawn";
    private const string AwardReviewSubmit = "submit";
    private const string AwardReviewApprove = "approve";
    private const string AwardReviewReject = "reject";
    private const string AwardReviewReturn = "return";
    private const string AwardReviewPublish = "publish";
    private const string AwardReviewArchive = "archive";
    private const string AwardReviewWithdraw = "withdraw";
    private const string AwardPublicityDraft = "draft";
    private const string AwardPublicityPublicizing = "publicizing";
    private const string AwardPublicityClosed = "closed";
    private const string AwardPublicityArchived = "archived";
    private const string AwardPublicityItemNormal = "normal";
    private const string AwardRuleScopeGlobal = "global";
    private const string AwardRuleScopeClub = "club";
    private const string AwardRuleStatusDraft = "draft";
    private const string AwardRuleStatusPublished = "published";
    private const string AwardRuleStatusArchived = "archived";
    private const string ClubMemberRoleCode = "CLUB_MEMBER";
    private const string ClubOfficerRoleCode = "CLUB_OFFICER";
    private const string ClubLeaderRoleCode = "CLUB_LEADER";
    private const string ClubAdvisorRoleCode = "ADVISOR";
    private const long MaxAwardUploadBytes = 50L * 1024 * 1024;

    private static readonly HashSet<string> ClubParticipantRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ClubMemberRoleCode,
        ClubOfficerRoleCode,
        ClubLeaderRoleCode,
        ClubAdvisorRoleCode
    };

    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();

    private readonly ClubHubDbContext _db;
    private readonly IAwardObjectStorage _awardStorage;
    private readonly ILogger<AwardWorkflowController> _logger;

    public AwardWorkflowController(
        ClubHubDbContext db,
        IAwardObjectStorage awardStorage,
        ILogger<AwardWorkflowController> logger)
    {
        _db = db;
        _awardStorage = awardStorage;
        _logger = logger;
    }

    [HttpGet("award-rule-documents")]
    public async Task<IActionResult> GetAwardRuleDocuments(
        int clubId,
        [FromQuery] string? status = null,
        [FromQuery] string? keyword = null)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var normalizedStatus = NormalizeAwardRuleStatus(status);
        if (!string.IsNullOrWhiteSpace(status) && normalizedStatus is null)
            return BadRequest(new { message = "评定细则状态不合法。" });

        var canMaintain = CanMaintainAwardWorkflow(access.Viewer!, access.Club!);
        var query = AwardRuleDocumentQuery()
            .Where(d => d.RuleScope == AwardRuleScopeGlobal || d.ClubId == clubId)
            .Where(d => normalizedStatus == null || d.RuleStatus == normalizedStatus);

        if (!canMaintain)
        {
            query = query.Where(d => d.RuleStatus == AwardRuleStatusPublished);
        }

        var normalizedKeyword = EmptyToNull(keyword)?.ToUpperInvariant();
        if (normalizedKeyword is not null)
        {
            query = query.Where(d =>
                d.RuleTitle.ToUpper().Contains(normalizedKeyword) ||
                d.AcademicYear.ToUpper().Contains(normalizedKeyword) ||
                (d.TermName != null && d.TermName.ToUpper().Contains(normalizedKeyword)) ||
                (d.IssuerName != null && d.IssuerName.ToUpper().Contains(normalizedKeyword)) ||
                (d.Summary != null && d.Summary.ToUpper().Contains(normalizedKeyword)));
        }

        var documents = await query
            .OrderBy(d => d.RuleScope == AwardRuleScopeGlobal ? 0 : 1)
            .ThenByDescending(d => d.PublishedAt ?? d.UpdatedAt)
            .ThenBy(d => d.RuleTitle)
            .ToListAsync();

        return Ok(documents.Select(ToAwardRuleDocumentRecordDto).ToList());
    }

    [HttpPost("award-rule-documents")]
    public async Task<IActionResult> CreateAwardRuleDocument(
        int clubId,
        [FromBody] AwardRuleDocumentRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var requestedRuleScope = NormalizeAwardRuleScope(req.RuleScope);
        if (!string.IsNullOrWhiteSpace(req.RuleScope) && requestedRuleScope is null)
            return BadRequest(new { message = "评定细则范围不合法。" });
        var ruleScope = requestedRuleScope ?? AwardRuleScopeClub;

        var permissionError = EnsureCanMaintainAwardRuleDocument(access.Viewer!, access.Club!, ruleScope);
        if (permissionError is not null) return permissionError;
        if (ruleScope == AwardRuleScopeClub && !IsMaintainableClub(access.Club!))
            return Conflict(new { message = "只有正在运营的社团可以维护社团评定细则。" });

        var validationError = ValidateAwardRuleDocumentRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var ruleTitle = req.RuleTitle!.Trim();
        var academicYear = req.AcademicYear!.Trim();
        var termName = EmptyToNull(req.TermName);
        var versionNo = EmptyToNull(req.VersionNo) ?? "1.0";
        var targetClubId = ruleScope == AwardRuleScopeClub ? clubId : (int?)null;
        if (await HasDuplicateAwardRuleDocumentAsync(ruleScope, targetClubId, ruleTitle, academicYear, termName, versionNo, null))
            return Conflict(new { message = "同一范围、学年学期和版本下已存在同名评定细则。" });

        var now = BusinessNow();
        var document = new AwardRuleDocument
        {
            RuleScope = ruleScope,
            ClubId = targetClubId,
            RuleTitle = ruleTitle,
            AcademicYear = academicYear,
            TermName = termName,
            IssuerName = EmptyToNull(req.IssuerName),
            Summary = EmptyToNull(req.Summary),
            ContentText = EmptyToNull(req.ContentText),
            MaterialUrl = EmptyToNull(req.MaterialUrl),
            MaterialName = EmptyToNull(req.MaterialName),
            VersionNo = versionNo,
            RuleStatus = NormalizeAwardRuleStatus(req.RuleStatus) ?? AwardRuleStatusDraft,
            EffectiveStartAt = req.EffectiveStartAt,
            EffectiveEndAt = req.EffectiveEndAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (document.RuleStatus == AwardRuleStatusPublished)
        {
            document.PublishedByUserId = currentUserId.Value;
            document.PublishedAt = now;
        }

        _db.AwardRuleDocuments.Add(document);
        await _db.SaveChangesAsync();

        var created = await AwardRuleDocumentQuery()
            .FirstAsync(d => d.RuleDocumentId == document.RuleDocumentId);
        return CreatedAtAction(nameof(GetAwardRuleDocuments), new { clubId }, ToAwardRuleDocumentRecordDto(created));
    }

    [HttpPatch("award-rule-documents/{ruleDocumentId:int}")]
    public async Task<IActionResult> UpdateAwardRuleDocument(
        int clubId,
        int ruleDocumentId,
        [FromBody] AwardRuleDocumentRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var document = await _db.AwardRuleDocuments.FirstOrDefaultAsync(d =>
            d.RuleDocumentId == ruleDocumentId &&
            (d.RuleScope == AwardRuleScopeGlobal || d.ClubId == clubId));
        if (document is null) return NotFound(new { message = "评定细则不存在。" });

        var requestedRuleScope = NormalizeAwardRuleScope(req.RuleScope);
        if (!string.IsNullOrWhiteSpace(req.RuleScope) && requestedRuleScope is null)
            return BadRequest(new { message = "评定细则范围不合法。" });
        var ruleScope = requestedRuleScope ?? document.RuleScope;

        var permissionError = EnsureCanMaintainAwardRuleDocument(access.Viewer!, access.Club!, ruleScope);
        if (permissionError is not null) return permissionError;
        if (ruleScope == AwardRuleScopeClub && !IsMaintainableClub(access.Club!))
            return Conflict(new { message = "只有正在运营的社团可以维护社团评定细则。" });

        var validationError = ValidateAwardRuleDocumentRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var ruleTitle = req.RuleTitle!.Trim();
        var academicYear = req.AcademicYear!.Trim();
        var termName = EmptyToNull(req.TermName);
        var versionNo = EmptyToNull(req.VersionNo) ?? "1.0";
        var targetClubId = ruleScope == AwardRuleScopeClub ? clubId : (int?)null;
        if (await HasDuplicateAwardRuleDocumentAsync(ruleScope, targetClubId, ruleTitle, academicYear, termName, versionNo, ruleDocumentId))
            return Conflict(new { message = "同一范围、学年学期和版本下已存在同名评定细则。" });

        var now = BusinessNow();
        document.RuleScope = ruleScope;
        document.ClubId = targetClubId;
        document.RuleTitle = ruleTitle;
        document.AcademicYear = academicYear;
        document.TermName = termName;
        document.IssuerName = EmptyToNull(req.IssuerName);
        document.Summary = EmptyToNull(req.Summary);
        document.ContentText = EmptyToNull(req.ContentText);
        document.MaterialUrl = EmptyToNull(req.MaterialUrl);
        document.MaterialName = EmptyToNull(req.MaterialName);
        document.VersionNo = versionNo;
        document.RuleStatus = NormalizeAwardRuleStatus(req.RuleStatus) ?? document.RuleStatus;
        document.EffectiveStartAt = req.EffectiveStartAt;
        document.EffectiveEndAt = req.EffectiveEndAt;
        document.UpdatedAt = now;

        if (document.RuleStatus == AwardRuleStatusPublished && document.PublishedAt is null)
        {
            document.PublishedByUserId = currentUserId.Value;
            document.PublishedAt = now;
        }
        else if (document.RuleStatus == AwardRuleStatusDraft)
        {
            document.PublishedByUserId = null;
            document.PublishedAt = null;
        }

        await _db.SaveChangesAsync();

        var updated = await AwardRuleDocumentQuery()
            .FirstAsync(d => d.RuleDocumentId == ruleDocumentId);
        return Ok(ToAwardRuleDocumentRecordDto(updated));
    }

    [HttpPost("award-rule-documents/{ruleDocumentId:int}/publish")]
    public async Task<IActionResult> PublishAwardRuleDocument(int clubId, int ruleDocumentId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var document = await _db.AwardRuleDocuments.FirstOrDefaultAsync(d =>
            d.RuleDocumentId == ruleDocumentId &&
            (d.RuleScope == AwardRuleScopeGlobal || d.ClubId == clubId));
        if (document is null) return NotFound(new { message = "评定细则不存在。" });

        var permissionError = EnsureCanMaintainAwardRuleDocument(access.Viewer!, access.Club!, document.RuleScope);
        if (permissionError is not null) return permissionError;

        var now = BusinessNow();
        document.RuleStatus = AwardRuleStatusPublished;
        document.PublishedByUserId = currentUserId.Value;
        document.PublishedAt = now;
        document.UpdatedAt = now;
        await _db.SaveChangesAsync();

        var published = await AwardRuleDocumentQuery()
            .FirstAsync(d => d.RuleDocumentId == ruleDocumentId);
        return Ok(ToAwardRuleDocumentRecordDto(published));
    }

    [HttpPost("award-rule-documents/{ruleDocumentId:int}/file")]
    [RequestSizeLimit(MaxAwardUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadAwardRuleDocumentFile(
        int clubId,
        int ruleDocumentId,
        [FromForm] IFormFile? file)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var document = await _db.AwardRuleDocuments
            .Include(d => d.Club)
            .Include(d => d.PublishedByUser)
            .FirstOrDefaultAsync(d =>
                d.RuleDocumentId == ruleDocumentId &&
                (d.RuleScope == AwardRuleScopeGlobal || d.ClubId == clubId));
        if (document is null) return NotFound(new { message = "评定细则不存在。" });

        var permissionError = EnsureCanMaintainAwardRuleDocument(access.Viewer!, access.Club!, document.RuleScope);
        if (permissionError is not null) return permissionError;

        var validationError = ValidateAwardUploadFile(file, out var originalFileName, out var extension);
        if (validationError is not null) return BadRequest(new { message = validationError });

        string? storageReference = null;
        var previousReference = document.MaterialUrl;
        try
        {
            await using var stream = file!.OpenReadStream();
            storageReference = await _awardStorage.UploadAsync(
                clubId,
                ruleDocumentId,
                extension,
                stream,
                file.Length,
                file.ContentType,
                originalFileName,
                HttpContext.RequestAborted);

            document.MaterialUrl = storageReference;
            document.MaterialName = originalFileName;
            document.UpdatedAt = BusinessNow();
            await _db.SaveChangesAsync();
            await TryRemoveAwardObjectAsync(previousReference, $"评定细则 {ruleDocumentId}");

            return Ok(ToAwardRuleDocumentRecordDto(document));
        }
        catch (Exception exception) when (IsObjectStorageFailure(exception))
        {
            await TryRemoveAwardObjectAsync(storageReference, $"评定细则 {ruleDocumentId}");
            _logger.LogError(exception, "评定细则 {RuleDocumentId} 附件上传到 OSS 失败。", ruleDocumentId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "文件存储暂不可用",
                detail: "无法连接 OSS 或 ECS RAM 角色凭据不可用，请稍后重试。");
        }
        catch
        {
            await TryRemoveAwardObjectAsync(storageReference, $"评定细则 {ruleDocumentId}");
            throw;
        }
    }

    [HttpGet("award-rule-documents/{ruleDocumentId:int}/file")]
    public async Task<IActionResult> GetAwardRuleDocumentFile(int clubId, int ruleDocumentId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var document = await _db.AwardRuleDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                d.RuleDocumentId == ruleDocumentId &&
                (d.RuleScope == AwardRuleScopeGlobal || d.ClubId == clubId));
        if (document is null) return NotFound(new { message = "评定细则不存在。" });

        var canMaintain = CanMaintainAwardWorkflow(access.Viewer!, access.Club!);
        if (!canMaintain && document.RuleStatus != AwardRuleStatusPublished)
            return StatusCode(403, new { message = "评定细则发布前不可下载附件。" });

        return await ReturnAwardManagedFileAsync(
            document.MaterialUrl,
            document.MaterialName ?? document.RuleTitle,
            "评定细则附件",
            ruleDocumentId);
    }

    [HttpGet("award-schemes")]
    public async Task<IActionResult> GetAwardSchemes(
        int clubId,
        [FromQuery] string? status = null,
        [FromQuery] string? keyword = null)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var normalizedStatus = NormalizeAwardSchemeStatus(status);
        if (!string.IsNullOrWhiteSpace(status) && normalizedStatus is null)
            return BadRequest(new { message = "奖项状态不合法。" });

        var canMaintain = CanMaintainAwardWorkflow(access.Viewer!, access.Club!);
        var query = AwardSchemeQuery()
            .Where(s => s.ClubId == clubId)
            .Where(s => normalizedStatus == null || s.SchemeStatus == normalizedStatus);

        if (!canMaintain)
        {
            query = query.Where(s =>
                s.SchemeStatus == AwardSchemeOpen ||
                s.SchemeStatus == AwardSchemePublicizing ||
                s.SchemeStatus == AwardSchemeArchived ||
                s.SchemeStatus == AwardSchemeClosed);
        }

        var normalizedKeyword = EmptyToNull(keyword)?.ToUpperInvariant();
        if (normalizedKeyword is not null)
        {
            query = query.Where(s =>
                s.AwardName.ToUpper().Contains(normalizedKeyword) ||
                (s.SponsorUnit != null && s.SponsorUnit.ToUpper().Contains(normalizedKeyword)) ||
                s.Levels.Any(level => level.LevelName.ToUpper().Contains(normalizedKeyword)));
        }

        var schemes = await query
            .OrderByDescending(s => s.ApplicationStartAt ?? s.CreatedAt)
            .ThenBy(s => s.AwardName)
            .ToListAsync();

        return Ok(schemes.Select(ToAwardSchemeRecordDto).ToList());
    }

    [HttpPost("award-schemes")]
    public async Task<IActionResult> CreateAwardScheme(
        int clubId,
        [FromBody] CreateAwardSchemeRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanMaintainAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var validationError = ValidateAwardSchemeRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var awardName = req.AwardName.Trim();
        var academicYear = req.AcademicYear.Trim();
        var termName = EmptyToNull(req.TermName);
        if (await HasDuplicateAwardSchemeAsync(clubId, awardName, academicYear, termName, null))
            return Conflict(new { message = "同一社团、学年、学期下已存在同名奖项。" });

        var now = BusinessNow();
        var scheme = new AwardScheme
        {
            ClubId = clubId,
            AwardName = awardName,
            AwardCategory = ToAwardCategory(req.AwardCategory),
            AcademicYear = academicYear,
            TermName = termName,
            SponsorUnit = EmptyToNull(req.SponsorUnit),
            RewardLevel = EmptyToNull(req.RewardLevel),
            FundingSource = EmptyToNull(req.FundingSource),
            IsRanked = req.IsRanked.GetValueOrDefault(true) ? 1 : 0,
            IsFixedAmount = req.IsFixedAmount.GetValueOrDefault(true) ? 1 : 0,
            Description = EmptyToNull(req.Description),
            MaterialDescription = EmptyToNull(req.MaterialDescription),
            ApplicationStartAt = req.ApplicationStartAt,
            ApplicationEndAt = req.ApplicationEndAt,
            PublicityStartAt = req.PublicityStartAt,
            PublicityEndAt = req.PublicityEndAt,
            SchemeStatus = ToAwardSchemeStatus(req.SchemeStatus),
            CreatedByUserId = currentUserId.Value,
            CreatedAt = now,
            UpdatedAt = now
        };

        AddAwardLevels(scheme, req.Levels, now);
        _db.AwardSchemes.Add(scheme);
        await _db.SaveChangesAsync();

        var created = await AwardSchemeQuery()
            .FirstAsync(s => s.AwardSchemeId == scheme.AwardSchemeId);
        return CreatedAtAction(nameof(GetAwardSchemes), new { clubId }, ToAwardSchemeRecordDto(created));
    }

    [HttpPatch("award-schemes/{awardSchemeId:int}")]
    public async Task<IActionResult> UpdateAwardScheme(
        int clubId,
        int awardSchemeId,
        [FromBody] UpdateAwardSchemeRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanMaintainAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var validationError = ValidateAwardSchemeRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var scheme = await _db.AwardSchemes
            .Include(s => s.Levels)
            .FirstOrDefaultAsync(s => s.ClubId == clubId && s.AwardSchemeId == awardSchemeId);
        if (scheme is null) return NotFound(new { message = "奖项配置不存在。" });
        if (scheme.SchemeStatus == AwardSchemeArchived)
            return Conflict(new { message = "已归档奖项不允许修改。" });

        var awardName = req.AwardName.Trim();
        var academicYear = req.AcademicYear.Trim();
        var termName = EmptyToNull(req.TermName);
        if (await HasDuplicateAwardSchemeAsync(clubId, awardName, academicYear, termName, awardSchemeId))
            return Conflict(new { message = "同一社团、学年、学期下已存在同名奖项。" });

        var now = BusinessNow();
        scheme.AwardName = awardName;
        scheme.AwardCategory = ToAwardCategory(req.AwardCategory);
        scheme.AcademicYear = academicYear;
        scheme.TermName = termName;
        scheme.SponsorUnit = EmptyToNull(req.SponsorUnit);
        scheme.RewardLevel = EmptyToNull(req.RewardLevel);
        scheme.FundingSource = EmptyToNull(req.FundingSource);
        scheme.IsRanked = req.IsRanked.GetValueOrDefault(true) ? 1 : 0;
        scheme.IsFixedAmount = req.IsFixedAmount.GetValueOrDefault(true) ? 1 : 0;
        scheme.Description = EmptyToNull(req.Description);
        scheme.MaterialDescription = EmptyToNull(req.MaterialDescription);
        scheme.ApplicationStartAt = req.ApplicationStartAt;
        scheme.ApplicationEndAt = req.ApplicationEndAt;
        scheme.PublicityStartAt = req.PublicityStartAt;
        scheme.PublicityEndAt = req.PublicityEndAt;
        scheme.SchemeStatus = ToAwardSchemeStatus(req.SchemeStatus);
        scheme.UpdatedAt = now;

        var syncError = SyncAwardLevels(scheme, req.Levels, now);
        if (syncError is not null) return BadRequest(new { message = syncError });

        await _db.SaveChangesAsync();

        var updated = await AwardSchemeQuery()
            .FirstAsync(s => s.AwardSchemeId == awardSchemeId);
        return Ok(ToAwardSchemeRecordDto(updated));
    }

    [HttpGet("award-applications")]
    public async Task<IActionResult> GetAwardApplications(
        int clubId,
        [FromQuery] int? awardSchemeId = null,
        [FromQuery] int? applicantUserId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? currentStep = null)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var normalizedStatus = NormalizeAwardApplicationStatus(status);
        if (!string.IsNullOrWhiteSpace(status) && normalizedStatus is null)
            return BadRequest(new { message = "申请状态不合法。" });

        var normalizedStep = NormalizeAwardStep(currentStep);
        if (!string.IsNullOrWhiteSpace(currentStep) && normalizedStep is null)
            return BadRequest(new { message = "审核步骤不合法。" });

        var canMaintain = CanMaintainAwardWorkflow(access.Viewer!, access.Club!);
        if (!canMaintain && applicantUserId is not null && applicantUserId.Value != currentUserId.Value)
            return StatusCode(403, new { message = "普通成员只能查看自己的评奖评优申请。" });

        var targetApplicantId = canMaintain ? applicantUserId : currentUserId.Value;
        var query = AwardApplicationQuery()
            .Where(a => a.ClubId == clubId)
            .Where(a => awardSchemeId == null || a.AwardSchemeId == awardSchemeId.Value)
            .Where(a => targetApplicantId == null || a.ApplicantUserId == targetApplicantId.Value)
            .Where(a => normalizedStatus == null || a.ApplicationStatus == normalizedStatus)
            .Where(a => normalizedStep == null || a.CurrentStep == normalizedStep);

        var applications = await query
            .OrderByDescending(a => a.SubmittedAt ?? a.CreatedAt)
            .ThenByDescending(a => a.AwardApplicationId)
            .ToListAsync();

        return Ok(applications.Select(ToAwardApplicationRecordDto).ToList());
    }

    [HttpPost("award-applications")]
    public async Task<IActionResult> CreateAwardApplication(
        int clubId,
        [FromBody] CreateAwardApplicationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;
        if (!IsMaintainableClub(access.Club!))
            return Conflict(new { message = "只有已通过审核且正在运营的社团可以提交评奖评优申请。" });

        var validationError = ValidateAwardApplicationRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var applicationType = ToAwardApplicationType(req.ApplicationType);
        var canMaintain = CanMaintainAwardWorkflow(access.Viewer!, access.Club!);
        if (applicationType == AwardApplicationSelf && req.ApplicantUserId != currentUserId.Value)
            return StatusCode(403, new { message = "为他人发起评奖评优请使用负责人推荐。" });
        if (applicationType == AwardApplicationRecommendation && !canMaintain)
            return StatusCode(403, new { message = "只有本社团负责人、指导老师或系统管理员可以发起推荐。" });

        var scheme = await _db.AwardSchemes
            .Include(s => s.Levels)
            .FirstOrDefaultAsync(s => s.ClubId == clubId && s.AwardSchemeId == req.AwardSchemeId);
        if (scheme is null) return NotFound(new { message = "奖项配置不存在。" });

        var level = scheme.Levels.FirstOrDefault(l =>
            l.AwardLevelId == req.AwardLevelId &&
            l.LevelStatus == MemberActive);
        if (level is null) return NotFound(new { message = "奖项等级不存在或已停用。" });

        if (!canMaintain && !IsAwardSchemeOpenForApplication(scheme))
            return Conflict(new { message = "该奖项当前未开放申请。" });

        var member = await LoadCurrentClubMemberAsync(clubId, req.ApplicantUserId);
        if (member is null) return Conflict(new { message = "申请人不是本社团当前有效成员。" });

        var hasDuplicate = await _db.AwardApplications.AnyAsync(a =>
            a.AwardSchemeId == req.AwardSchemeId &&
            a.ApplicantUserId == req.ApplicantUserId);
        if (hasDuplicate)
            return Conflict(new { message = "该成员已存在同一奖项申请，请在原申请上修改或重新提交。" });

        var now = BusinessNow();
        var submitNow = req.SubmitNow.GetValueOrDefault(false);
        var application = new AwardApplication
        {
            ClubId = clubId,
            AwardSchemeId = req.AwardSchemeId,
            AwardLevelId = req.AwardLevelId,
            ApplicantUserId = req.ApplicantUserId,
            RecommenderUserId = applicationType == AwardApplicationRecommendation ? currentUserId.Value : null,
            SubmitterUserId = currentUserId.Value,
            ApplicationType = applicationType,
            ApplicationReason = req.ApplicationReason.Trim(),
            MaterialUrl = EmptyToNull(req.MaterialUrl),
            CurrentStep = submitNow ? AwardStepClubReview : AwardStepStudentSubmit,
            ApplicationStatus = submitNow ? AwardStatusClubReview : AwardStatusDraft,
            PublicStatus = AwardPublicNone,
            ReviewRound = 1,
            CreatedAt = now,
            UpdatedAt = now,
            SubmittedAt = submitNow ? now : null
        };

        if (submitNow)
        {
            application.ReviewRecords.Add(NewAwardReviewRecord(
                application,
                currentUserId.Value,
                AwardStepStudentSubmit,
                AwardReviewSubmit,
                AwardStatusDraft,
                AwardStatusClubReview,
                "提交申请",
                now));
            scheme.SchemeStatus = AwardSchemeReviewing;
            scheme.UpdatedAt = now;
        }

        _db.AwardApplications.Add(application);
        await _db.SaveChangesAsync();

        var created = await AwardApplicationQuery()
            .FirstAsync(a => a.AwardApplicationId == application.AwardApplicationId);
        return CreatedAtAction(nameof(GetAwardApplications), new { clubId }, ToAwardApplicationRecordDto(created));
    }

    [HttpPatch("award-applications/{awardApplicationId:int}")]
    public async Task<IActionResult> UpdateAwardApplication(
        int clubId,
        int awardApplicationId,
        [FromBody] UpdateAwardApplicationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var application = await _db.AwardApplications
            .Include(a => a.Scheme)
                .ThenInclude(s => s!.Levels)
            .FirstOrDefaultAsync(a => a.ClubId == clubId && a.AwardApplicationId == awardApplicationId);
        if (application is null) return NotFound(new { message = "评奖评优申请不存在。" });

        var accessError = await EnsureCanEditAwardApplicationAsync(application, currentUserId.Value);
        if (accessError is not null) return accessError;

        if (application.ApplicationStatus != AwardStatusDraft &&
            application.ApplicationStatus != AwardStatusReturned)
        {
            return Conflict(new { message = "只有草稿或退回状态的申请可以修改。" });
        }

        var validationError = ValidateAwardApplicationRequest(req);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var level = application.Scheme?.Levels.FirstOrDefault(l =>
            l.AwardLevelId == req.AwardLevelId &&
            l.LevelStatus == MemberActive);
        if (level is null) return NotFound(new { message = "奖项等级不存在或已停用。" });

        application.AwardLevelId = req.AwardLevelId;
        application.ApplicationReason = req.ApplicationReason.Trim();
        application.MaterialUrl = EmptyToNull(req.MaterialUrl);
        application.UpdatedAt = BusinessNow();
        await _db.SaveChangesAsync();

        var updated = await AwardApplicationQuery()
            .FirstAsync(a => a.AwardApplicationId == awardApplicationId);
        return Ok(ToAwardApplicationRecordDto(updated));
    }

    [HttpPost("award-applications/{awardApplicationId:int}/attachments")]
    [RequestSizeLimit(MaxAwardUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadAwardApplicationAttachment(
        int clubId,
        int awardApplicationId,
        [FromForm] IFormFile? file,
        [FromForm] string? attachmentType = null)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var application = await _db.AwardApplications
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.ClubId == clubId && a.AwardApplicationId == awardApplicationId);
        if (application is null) return NotFound(new { message = "评奖评优申请不存在。" });

        var accessError = await EnsureCanEditAwardApplicationAsync(application, currentUserId.Value);
        if (accessError is not null) return accessError;

        if (application.ApplicationStatus != AwardStatusDraft &&
            application.ApplicationStatus != AwardStatusReturned)
        {
            return Conflict(new { message = "只有草稿或退回状态的申请可以补充材料。" });
        }

        var validationError = ValidateAwardUploadFile(file, out var originalFileName, out var extension);
        if (validationError is not null) return BadRequest(new { message = validationError });
        var normalizedAttachmentType = EmptyToNull(attachmentType);
        if (normalizedAttachmentType?.Length > 100) return BadRequest(new { message = "材料类型不能超过 100 个字符。" });

        string? storageReference = null;
        try
        {
            await using var stream = file!.OpenReadStream();
            storageReference = await _awardStorage.UploadAsync(
                clubId,
                awardApplicationId,
                extension,
                stream,
                file.Length,
                file.ContentType,
                originalFileName,
                HttpContext.RequestAborted);

            var now = BusinessNow();
            application.Attachments.Add(new AwardAttachment
            {
                AttachmentName = originalFileName,
                AttachmentUrl = storageReference,
                AttachmentType = normalizedAttachmentType,
                UploadedByUserId = currentUserId.Value,
                UploadedAt = now
            });
            application.MaterialUrl ??= storageReference;
            application.UpdatedAt = now;
            await _db.SaveChangesAsync();

            var updated = await AwardApplicationQuery()
                .FirstAsync(a => a.AwardApplicationId == awardApplicationId);
            return Ok(ToAwardApplicationRecordDto(updated));
        }
        catch (Exception exception) when (IsObjectStorageFailure(exception))
        {
            await TryRemoveAwardObjectAsync(storageReference, $"评优申请 {awardApplicationId}");
            _logger.LogError(exception, "评优申请 {AwardApplicationId} 材料上传到 OSS 失败。", awardApplicationId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "文件存储暂不可用",
                detail: "无法连接 OSS 或 ECS RAM 角色凭据不可用，请稍后重试。");
        }
        catch
        {
            await TryRemoveAwardObjectAsync(storageReference, $"评优申请 {awardApplicationId}");
            throw;
        }
    }

    [HttpGet("award-applications/{awardApplicationId:int}/attachments/{attachmentId:int}/file")]
    public async Task<IActionResult> GetAwardApplicationAttachmentFile(
        int clubId,
        int awardApplicationId,
        int attachmentId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var application = await _db.AwardApplications
            .AsNoTracking()
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.ClubId == clubId && a.AwardApplicationId == awardApplicationId);
        if (application is null) return NotFound(new { message = "评奖评优申请不存在。" });

        var attachment = application.Attachments.FirstOrDefault(a => a.AttachmentId == attachmentId);
        if (attachment is null) return NotFound(new { message = "申请材料不存在。" });

        var canMaintain = CanMaintainAwardWorkflow(access.Viewer!, access.Club!);
        var isInvolvedUser =
            application.ApplicantUserId == currentUserId.Value ||
            application.SubmitterUserId == currentUserId.Value ||
            application.RecommenderUserId == currentUserId.Value;
        var isPublicResult =
            application.PublicStatus is AwardPublicizing or AwardPublicized ||
            application.ApplicationStatus is AwardStatusPublicizing or AwardStatusArchived;
        if (!canMaintain && !isInvolvedUser && !isPublicResult)
            return StatusCode(403, new { message = "当前用户没有下载该申请材料的权限。" });

        return await ReturnAwardManagedFileAsync(
            attachment.AttachmentUrl,
            attachment.AttachmentName,
            "评优申请材料",
            attachmentId);
    }

    [HttpPost("award-applications/{awardApplicationId:int}/submit")]
    public async Task<IActionResult> SubmitAwardApplication(int clubId, int awardApplicationId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var application = await _db.AwardApplications
            .Include(a => a.Scheme)
            .FirstOrDefaultAsync(a => a.ClubId == clubId && a.AwardApplicationId == awardApplicationId);
        if (application is null) return NotFound(new { message = "评奖评优申请不存在。" });

        var accessError = await EnsureCanEditAwardApplicationAsync(application, currentUserId.Value);
        if (accessError is not null) return accessError;

        if (application.ApplicationStatus != AwardStatusDraft &&
            application.ApplicationStatus != AwardStatusReturned)
        {
            return Conflict(new { message = "只有草稿或退回状态的申请可以提交。" });
        }

        if (string.IsNullOrWhiteSpace(application.ApplicationReason))
            return BadRequest(new { message = "申请理由不能为空。" });

        var now = BusinessNow();
        var fromStatus = application.ApplicationStatus;
        if (application.ApplicationStatus == AwardStatusReturned) application.ReviewRound += 1;

        application.ApplicationStatus = AwardStatusClubReview;
        application.CurrentStep = AwardStepClubReview;
        application.SubmittedAt = now;
        application.UpdatedAt = now;
        if (application.Scheme is not null)
        {
            application.Scheme.SchemeStatus = AwardSchemeReviewing;
            application.Scheme.UpdatedAt = now;
        }

        _db.AwardReviewRecords.Add(NewAwardReviewRecord(
            application,
            currentUserId.Value,
            AwardStepStudentSubmit,
            AwardReviewSubmit,
            fromStatus,
            AwardStatusClubReview,
            "提交申请",
            now));
        await _db.SaveChangesAsync();

        var submitted = await AwardApplicationQuery()
            .FirstAsync(a => a.AwardApplicationId == awardApplicationId);
        return Ok(ToAwardApplicationRecordDto(submitted));
    }

    [HttpPost("award-applications/{awardApplicationId:int}/review")]
    public async Task<IActionResult> ReviewAwardApplication(
        int clubId,
        int awardApplicationId,
        [FromBody] ReviewAwardApplicationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var application = await _db.AwardApplications
            .Include(a => a.Scheme)
            .Include(a => a.Level)
            .FirstOrDefaultAsync(a => a.ClubId == clubId && a.AwardApplicationId == awardApplicationId);
        if (application is null) return NotFound(new { message = "评奖评优申请不存在。" });

        var viewer = await LoadUserAsync(currentUserId.Value);
        if (viewer is null) return NotFound(new { message = "当前用户不存在。" });
        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null) return NotFound(new { message = "社团不存在。" });
        if (!UsersController.IsActive(viewer.AccountStatus))
            return BadRequest(new { message = "当前用户账号不可用，不能审核评奖评优申请。" });

        var reviewResult = ToAwardReviewResult(req.ReviewResult);
        var permissionError = ValidateAwardReviewPermission(viewer, club, application, reviewResult);
        if (permissionError is not null) return permissionError;

        var now = BusinessNow();
        var transition = ResolveAwardReviewTransition(application, reviewResult);
        if (transition.Error is not null) return transition.Error;
        if (reviewResult == AwardReviewArchive)
        {
            var publicityError =
                await EnsureAwardApplicationPublicityEndedAsync(clubId, awardApplicationId, now);
            if (publicityError is not null) return publicityError;
        }

        var fromStatus = application.ApplicationStatus;
        var fromStep = application.CurrentStep;
        application.ApplicationStatus = transition.Status!;
        application.CurrentStep = transition.Step!;
        application.PublicStatus = transition.PublicStatus ?? application.PublicStatus;
        application.UpdatedAt = now;
        if (reviewResult == AwardReviewApprove && transition.Status == AwardStatusApproved)
        {
            application.FinalAwardScore = ClampAwardScore(req.FinalAwardScore ?? application.Level?.AwardScore ?? 0);
            application.FinalAmount = req.FinalAmount ?? application.Level?.Amount;
            application.ApprovedAt = now;
        }
        else if (req.FinalAwardScore is not null)
        {
            application.FinalAwardScore = ClampAwardScore(req.FinalAwardScore.Value);
        }

        if (reviewResult == AwardReviewPublish)
        {
            application.PublicizedAt = now;
        }
        else if (reviewResult == AwardReviewArchive)
        {
            application.PublicizedAt ??= now;
            application.ArchivedAt = now;
        }

        _db.AwardReviewRecords.Add(NewAwardReviewRecord(
            application,
            currentUserId.Value,
            ReviewStepForRecord(fromStep, reviewResult),
            reviewResult,
            fromStatus,
            application.ApplicationStatus,
            EmptyToNull(req.ReviewComment),
            now));
        await _db.SaveChangesAsync();

        var reviewed = await AwardApplicationQuery()
            .FirstAsync(a => a.AwardApplicationId == awardApplicationId);
        return Ok(ToAwardApplicationRecordDto(reviewed));
    }

    [HttpGet("award-publicity")]
    public async Task<IActionResult> GetAwardPublicityBatches(
        int clubId,
        [FromQuery] string? status = null)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanViewAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var normalizedStatus = NormalizeAwardPublicityStatus(status);
        if (!string.IsNullOrWhiteSpace(status) && normalizedStatus is null)
            return BadRequest(new { message = "公示状态不合法。" });

        var canMaintain = CanMaintainAwardWorkflow(access.Viewer!, access.Club!);
        var query = AwardPublicityQuery()
            .Where(b => b.ClubId == clubId)
            .Where(b => normalizedStatus == null || b.PublicityStatus == normalizedStatus);
        if (!canMaintain)
        {
            query = query.Where(b =>
                b.PublicityStatus == AwardPublicityPublicizing ||
                b.PublicityStatus == AwardPublicityClosed ||
                b.PublicityStatus == AwardPublicityArchived);
        }

        var batches = await query
            .OrderByDescending(b => b.PublicityStartAt ?? b.CreatedAt)
            .ThenByDescending(b => b.PublicityBatchId)
            .ToListAsync();
        return Ok(batches.Select(ToAwardPublicityBatchRecordDto).ToList());
    }

    [HttpPost("award-publicity")]
    public async Task<IActionResult> CreateAwardPublicityBatch(
        int clubId,
        [FromBody] CreateAwardPublicityBatchRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanMaintainAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "公示标题不能为空。" });

        var applicationIds = req.AwardApplicationIds?.Distinct().ToList() ?? new List<int>();
        if (applicationIds.Count == 0)
            return BadRequest(new { message = "请至少选择一条已通过的评奖评优申请。" });

        var applications = await _db.AwardApplications
            .Include(a => a.Scheme)
            .Where(a => a.ClubId == clubId && applicationIds.Contains(a.AwardApplicationId))
            .ToListAsync();
        if (applications.Count != applicationIds.Count)
            return NotFound(new { message = "部分评奖评优申请不存在。" });
        if (applications.Any(a => a.ApplicationStatus != AwardStatusApproved))
            return Conflict(new { message = "只有已通过终审的申请可以进入公示批次。" });

        var now = BusinessNow();
        var batch = new AwardPublicityBatch
        {
            ClubId = clubId,
            Title = req.Title.Trim(),
            Description = EmptyToNull(req.Description),
            PublicityStartAt = req.PublicityStartAt,
            PublicityEndAt = req.PublicityEndAt,
            PublicityStatus = AwardPublicityDraft,
            PublisherUserId = currentUserId.Value,
            CreatedAt = now,
            UpdatedAt = now
        };

        var order = 1;
        foreach (var application in applications.OrderBy(a => a.AwardApplicationId))
        {
            batch.Items.Add(new AwardPublicityItem
            {
                ClubId = clubId,
                AwardApplicationId = application.AwardApplicationId,
                DisplayOrder = order++,
                PublicityResult = AwardPublicityItemNormal,
                CreatedAt = now
            });
        }

        _db.AwardPublicityBatches.Add(batch);
        await _db.SaveChangesAsync();

        var created = await AwardPublicityQuery()
            .FirstAsync(b => b.PublicityBatchId == batch.PublicityBatchId);
        return CreatedAtAction(nameof(GetAwardPublicityBatches), new { clubId }, ToAwardPublicityBatchRecordDto(created));
    }

    [HttpPost("award-publicity/{publicityBatchId:int}/publish")]
    public async Task<IActionResult> PublishAwardPublicityBatch(int clubId, int publicityBatchId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanMaintainAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var batch = await PublicityBatchForUpdateQuery()
            .FirstOrDefaultAsync(b => b.ClubId == clubId && b.PublicityBatchId == publicityBatchId);
        if (batch is null) return NotFound(new { message = "公示批次不存在。" });
        if (batch.PublicityStatus != AwardPublicityDraft)
            return Conflict(new { message = "只有草稿公示批次可以发布。" });

        var now = BusinessNow();
        batch.PublicityStatus = AwardPublicityPublicizing;
        batch.PublisherUserId = currentUserId.Value;
        batch.UpdatedAt = now;
        foreach (var item in batch.Items)
        {
            var application = item.Application;
            if (application is null) continue;
            application.ApplicationStatus = AwardStatusPublicizing;
            application.CurrentStep = AwardStepPublicity;
            application.PublicStatus = AwardPublicizing;
            application.UpdatedAt = now;
            if (application.Scheme is not null)
            {
                application.Scheme.SchemeStatus = AwardSchemePublicizing;
                application.Scheme.UpdatedAt = now;
            }

            _db.AwardReviewRecords.Add(NewAwardReviewRecord(
                application,
                currentUserId.Value,
                AwardStepPublicity,
                AwardReviewPublish,
                AwardStatusApproved,
                AwardStatusPublicizing,
                "发布公示",
                now));
        }

        await _db.SaveChangesAsync();
        var published = await AwardPublicityQuery()
            .FirstAsync(b => b.PublicityBatchId == publicityBatchId);
        return Ok(ToAwardPublicityBatchRecordDto(published));
    }

    [HttpPost("award-publicity/{publicityBatchId:int}/archive")]
    public async Task<IActionResult> ArchiveAwardPublicityBatch(int clubId, int publicityBatchId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null)
            return Unauthorized(new { message = "登录状态已失效，请重新登录。" });

        var access = await EnsureCanMaintainAwardWorkflowAsync(clubId, currentUserId.Value);
        if (access.Result is not null) return access.Result;

        var batch = await PublicityBatchForUpdateQuery()
            .FirstOrDefaultAsync(b => b.ClubId == clubId && b.PublicityBatchId == publicityBatchId);
        if (batch is null) return NotFound(new { message = "公示批次不存在。" });
        if (batch.PublicityStatus != AwardPublicityPublicizing &&
            batch.PublicityStatus != AwardPublicityClosed)
        {
            return Conflict(new { message = "只有公示中或已结束的批次可以归档。" });
        }

        var now = BusinessNow();
        if (batch.PublicityEndAt is null)
            return Conflict(new { message = "公示结束时间未设置，不能归档。" });
        if (batch.PublicityEndAt > now)
            return Conflict(new { message = "公示期尚未结束，不能提前归档。" });

        batch.PublicityStatus = AwardPublicityArchived;
        batch.UpdatedAt = now;
        foreach (var item in batch.Items)
        {
            var application = item.Application;
            if (application is null) continue;
            application.ApplicationStatus = AwardStatusArchived;
            application.CurrentStep = AwardStepArchived;
            application.PublicStatus = AwardPublicized;
            application.PublicizedAt ??= now;
            application.ArchivedAt = now;
            application.UpdatedAt = now;
            if (application.Scheme is not null)
            {
                application.Scheme.SchemeStatus = AwardSchemeArchived;
                application.Scheme.UpdatedAt = now;
            }

            _db.AwardReviewRecords.Add(NewAwardReviewRecord(
                application,
                currentUserId.Value,
                AwardReviewStepArchive,
                AwardReviewArchive,
                AwardStatusPublicizing,
                AwardStatusArchived,
                "公示归档",
                now));
        }

        await _db.SaveChangesAsync();
        var archived = await AwardPublicityQuery()
            .FirstAsync(b => b.PublicityBatchId == publicityBatchId);
        return Ok(ToAwardPublicityBatchRecordDto(archived));
    }

    private IQueryable<AwardScheme> AwardSchemeQuery() =>
        _db.AwardSchemes
            .AsNoTracking()
            .Include(s => s.Club)
            .Include(s => s.CreatedByUser)
            .Include(s => s.Levels);

    private IQueryable<AwardApplication> AwardApplicationQuery() =>
        _db.AwardApplications
            .AsNoTracking()
            .Include(a => a.Club)
            .Include(a => a.Scheme)
            .Include(a => a.Level)
            .Include(a => a.Applicant)
            .Include(a => a.Recommender)
            .Include(a => a.Submitter)
            .Include(a => a.ReviewRecords)
                .ThenInclude(r => r.Reviewer)
            .Include(a => a.Attachments)
                .ThenInclude(a => a.UploadedByUser);

    private IQueryable<AwardPublicityBatch> AwardPublicityQuery() =>
        _db.AwardPublicityBatches
            .AsNoTracking()
            .Include(b => b.Club)
            .Include(b => b.Publisher)
            .Include(b => b.Items)
                .ThenInclude(i => i.Application)
                    .ThenInclude(a => a!.Applicant)
            .Include(b => b.Items)
                .ThenInclude(i => i.Application)
                    .ThenInclude(a => a!.Scheme)
            .Include(b => b.Items)
                .ThenInclude(i => i.Application)
                    .ThenInclude(a => a!.Level);

    private IQueryable<AwardRuleDocument> AwardRuleDocumentQuery() =>
        _db.AwardRuleDocuments
            .AsNoTracking()
            .Include(d => d.Club)
            .Include(d => d.PublishedByUser);

    private IQueryable<AwardPublicityBatch> PublicityBatchForUpdateQuery() =>
        _db.AwardPublicityBatches
            .Include(b => b.Items)
                .ThenInclude(i => i.Application)
                    .ThenInclude(a => a!.Scheme);

    private async Task<(IActionResult? Result, Club? Club, User? Viewer)> EnsureCanViewAwardWorkflowAsync(
        int clubId,
        int viewerUserId)
    {
        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null) return (NotFound(new { message = "当前用户不存在。" }), null, null);
        if (!UsersController.IsActive(viewer.AccountStatus))
            return (BadRequest(new { message = "当前用户账号不可用，不能查看评奖评优。" }), null, viewer);

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null) return (NotFound(new { message = "社团不存在。" }), null, viewer);

        var canView =
            CanMaintainAwardWorkflow(viewer, club) ||
            HasClubParticipantRole(viewer, clubId) ||
            viewer.ClubMemberships.Any(cm => cm.ClubId == clubId && IsCurrentMemberTerm(cm));
        if (!canView)
            return (StatusCode(403, new { message = "只有本社团成员、负责人、指导老师或系统管理员可以查看评奖评优。" }), club, viewer);

        return (null, club, viewer);
    }

    private async Task<(IActionResult? Result, Club? Club, User? Viewer)> EnsureCanMaintainAwardWorkflowAsync(
        int clubId,
        int viewerUserId)
    {
        var viewer = await LoadUserAsync(viewerUserId);
        if (viewer is null) return (NotFound(new { message = "当前用户不存在。" }), null, null);
        if (!UsersController.IsActive(viewer.AccountStatus))
            return (BadRequest(new { message = "当前用户账号不可用，不能维护评奖评优。" }), null, viewer);

        var club = await _db.Clubs.FirstOrDefaultAsync(c => c.ClubId == clubId);
        if (club is null) return (NotFound(new { message = "社团不存在。" }), null, viewer);
        if (!IsMaintainableClub(club))
            return (Conflict(new { message = "只有已通过审核且正在运营的社团可以维护评奖评优。" }), club, viewer);
        if (!CanMaintainAwardWorkflow(viewer, club))
            return (StatusCode(403, new { message = "只有本社团负责人、指导老师或系统管理员可以维护评奖评优。" }), club, viewer);

        return (null, club, viewer);
    }

    private async Task<IActionResult?> EnsureCanEditAwardApplicationAsync(
        AwardApplication application,
        int viewerUserId)
    {
        var access = await EnsureCanViewAwardWorkflowAsync(application.ClubId, viewerUserId);
        if (access.Result is not null) return access.Result;

        if (CanMaintainAwardWorkflow(access.Viewer!, access.Club!) ||
            application.ApplicantUserId == viewerUserId ||
            application.SubmitterUserId == viewerUserId ||
            application.RecommenderUserId == viewerUserId)
        {
            return null;
        }

        return StatusCode(403, new { message = "只能维护自己的申请或自己发起的推荐。" });
    }

    private bool CanMaintainAwardWorkflow(User viewer, Club club) =>
        UsersController.IsPlatformAdmin(viewer) ||
        UsersController.IsSystemAdmin(viewer) ||
        IsClubPrincipal(viewer, club) ||
        UsersController.IsClubAdvisor(viewer, club.ClubId);

    private IActionResult? EnsureCanMaintainAwardRuleDocument(User viewer, Club club, string ruleScope)
    {
        if (ruleScope == AwardRuleScopeGlobal)
        {
            return UsersController.IsPlatformAdmin(viewer) || UsersController.IsSystemAdmin(viewer)
                ? null
                : StatusCode(403, new { message = "只有平台或系统管理员可以维护学校级评定细则。" });
        }

        return CanMaintainAwardWorkflow(viewer, club)
            ? null
            : StatusCode(403, new { message = "只有本社团负责人、指导老师或管理员可以维护社团评定细则。" });
    }

    private static bool IsClubPrincipal(User viewer, Club club) =>
        club.PresidentUserId == viewer.UserId || UsersController.IsClubPrincipal(viewer, club.ClubId);

    private static bool HasClubParticipantRole(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            ur.Role is not null &&
            ClubParticipantRoleCodes.Contains((ur.Role.RoleCode ?? string.Empty).Trim()));

    private async Task<User?> LoadUserAsync(int userId) =>
        await _db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.ClubMemberships)
            .FirstOrDefaultAsync(u => u.UserId == userId);

    private async Task<ClubMember?> LoadCurrentClubMemberAsync(int clubId, int userId)
    {
        var today = BusinessToday();
        return await _db.ClubMembers
            .Where(cm =>
                cm.ClubId == clubId &&
                cm.UserId == userId &&
                (cm.MemberStatus == null || cm.MemberStatus == MemberActive) &&
                (cm.TermStart == null || cm.TermStart <= today) &&
                (cm.TermEnd == null || cm.TermEnd >= today))
            .OrderByDescending(cm => cm.TermStart)
            .ThenByDescending(cm => cm.JoinAt)
            .FirstOrDefaultAsync();
    }

    private static bool IsCurrentMemberTerm(ClubMember member)
    {
        var today = BusinessToday();
        return (member.MemberStatus == null || UsersController.IsActive(member.MemberStatus)) &&
               (member.TermStart == null || member.TermStart <= today) &&
               (member.TermEnd == null || member.TermEnd >= today);
    }

    private static bool IsMaintainableClub(Club club) =>
        string.Equals(club.AuditStatus, AuditApproved, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(club.ClubStatus, ClubActive, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> HasDuplicateAwardRuleDocumentAsync(
        string ruleScope,
        int? clubId,
        string ruleTitle,
        string academicYear,
        string? termName,
        string versionNo,
        int? ignoredRuleDocumentId)
    {
        var normalizedTitle = ruleTitle.ToUpperInvariant();
        var normalizedAcademicYear = academicYear.ToUpperInvariant();
        var normalizedTermName = (termName ?? string.Empty).ToUpperInvariant();
        var normalizedVersionNo = versionNo.ToUpperInvariant();
        return await _db.AwardRuleDocuments.AnyAsync(d =>
            d.RuleScope == ruleScope &&
            d.ClubId == clubId &&
            (ignoredRuleDocumentId == null || d.RuleDocumentId != ignoredRuleDocumentId.Value) &&
            d.RuleTitle.ToUpper() == normalizedTitle &&
            d.AcademicYear.ToUpper() == normalizedAcademicYear &&
            (d.TermName ?? string.Empty).ToUpper() == normalizedTermName &&
            d.VersionNo.ToUpper() == normalizedVersionNo);
    }

    private async Task<bool> HasDuplicateAwardSchemeAsync(
        int clubId,
        string awardName,
        string academicYear,
        string? termName,
        int? ignoredAwardSchemeId)
    {
        var normalizedName = awardName.ToUpperInvariant();
        var normalizedAcademicYear = academicYear.ToUpperInvariant();
        var normalizedTermName = (termName ?? string.Empty).ToUpperInvariant();
        return await _db.AwardSchemes.AnyAsync(s =>
            s.ClubId == clubId &&
            (ignoredAwardSchemeId == null || s.AwardSchemeId != ignoredAwardSchemeId.Value) &&
            s.AwardName.ToUpper() == normalizedName &&
            s.AcademicYear.ToUpper() == normalizedAcademicYear &&
            (s.TermName ?? string.Empty).ToUpper() == normalizedTermName);
    }

    private static string? ValidateAwardRuleDocumentRequest(AwardRuleDocumentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RuleTitle)) return "评定细则标题不能为空。";
        if (req.RuleTitle.Trim().Length > 255) return "评定细则标题不能超过 255 个字符。";
        if (string.IsNullOrWhiteSpace(req.AcademicYear)) return "适用学年不能为空。";
        if (req.AcademicYear.Trim().Length > 50) return "适用学年不能超过 50 个字符。";
        if (!string.IsNullOrWhiteSpace(req.TermName) && req.TermName.Trim().Length > 80)
            return "适用学期不能超过 80 个字符。";
        if (!string.IsNullOrWhiteSpace(req.IssuerName) && req.IssuerName.Trim().Length > 255)
            return "制定单位不能超过 255 个字符。";
        if (!string.IsNullOrWhiteSpace(req.MaterialUrl) && req.MaterialUrl.Trim().Length > 1000)
            return "材料文件引用不能超过 1000 个字符。";
        if (!string.IsNullOrWhiteSpace(req.MaterialName) && req.MaterialName.Trim().Length > 255)
            return "材料名称不能超过 255 个字符。";
        if (!string.IsNullOrWhiteSpace(req.VersionNo) && req.VersionNo.Trim().Length > 50)
            return "版本号不能超过 50 个字符。";
        if (!string.IsNullOrWhiteSpace(req.RuleStatus) && NormalizeAwardRuleStatus(req.RuleStatus) is null)
            return "评定细则状态不合法。";
        if (req.EffectiveStartAt is not null &&
            req.EffectiveEndAt is not null &&
            req.EffectiveStartAt > req.EffectiveEndAt)
        {
            return "生效开始时间不能晚于结束时间。";
        }

        return null;
    }

    private static string? ValidateAwardSchemeRequest(CreateAwardSchemeRequest req) =>
        ValidateAwardSchemeRequestCore(
            req.AwardName,
            req.AcademicYear,
            req.ApplicationStartAt,
            req.ApplicationEndAt,
            req.PublicityStartAt,
            req.PublicityEndAt,
            req.Levels);

    private static string? ValidateAwardSchemeRequest(UpdateAwardSchemeRequest req) =>
        ValidateAwardSchemeRequestCore(
            req.AwardName,
            req.AcademicYear,
            req.ApplicationStartAt,
            req.ApplicationEndAt,
            req.PublicityStartAt,
            req.PublicityEndAt,
            req.Levels);

    private static string? ValidateAwardSchemeRequestCore(
        string awardName,
        string academicYear,
        DateTime? applicationStartAt,
        DateTime? applicationEndAt,
        DateTime? publicityStartAt,
        DateTime? publicityEndAt,
        IReadOnlyCollection<Org.OpenAPITools.Models.AwardLevelInput>? levels)
    {
        if (string.IsNullOrWhiteSpace(awardName)) return "奖项名称不能为空。";
        if (string.IsNullOrWhiteSpace(academicYear)) return "评定学年不能为空。";
        if (applicationStartAt is not null && applicationEndAt is not null && applicationStartAt > applicationEndAt)
            return "申请开始时间不能晚于申请结束时间。";
        if (publicityStartAt is not null && publicityEndAt is not null && publicityStartAt > publicityEndAt)
            return "公示开始时间不能晚于公示结束时间。";
        if (levels is null || levels.Count == 0) return "至少需要配置一个奖项等级。";

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in levels)
        {
            if (string.IsNullOrWhiteSpace(level.LevelName)) return "等级名称不能为空。";
            if (!names.Add(level.LevelName.Trim())) return "同一奖项下不能配置重复等级名称。";
            if (level.AwardScore < 0 || level.AwardScore > 100) return "等级奖项分必须在 0 到 100 之间。";
            if (level.Amount < 0) return "奖励金额不能小于 0。";
            if (level.Quota < 0) return "名额不能小于 0。";
        }

        return null;
    }

    private static string? ValidateAwardApplicationRequest(CreateAwardApplicationRequest req)
    {
        if (req.AwardSchemeId <= 0) return "请选择奖项。";
        if (req.AwardLevelId <= 0) return "请选择奖项等级。";
        if (req.ApplicantUserId <= 0) return "请选择申请成员。";
        if (string.IsNullOrWhiteSpace(req.ApplicationReason)) return "申请理由不能为空。";
        return null;
    }

    private static string? ValidateAwardApplicationRequest(UpdateAwardApplicationRequest req)
    {
        if (req.AwardLevelId <= 0) return "请选择奖项等级。";
        if (string.IsNullOrWhiteSpace(req.ApplicationReason)) return "申请理由不能为空。";
        return null;
    }

    private static string? ValidateAwardUploadFile(
        IFormFile? file,
        out string originalFileName,
        out string extension)
    {
        originalFileName = string.Empty;
        extension = string.Empty;
        if (file is null || file.Length <= 0) return "请选择需要上传的文件。";
        if (file.Length > MaxAwardUploadBytes) return "单个文件不能超过 50 MB。";

        originalFileName = Path.GetFileName(file.FileName).Trim();
        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Length > 255)
            return "文件名不能为空且不能超过 255 个字符。";

        extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "文件扩展名包含无效字符。";
        if (IsBlockedUploadExtension(extension)) return "不允许上传可执行文件或脚本文件。";

        return null;
    }

    private async Task<IActionResult> ReturnAwardManagedFileAsync(
        string? fileReference,
        string downloadName,
        string fileKind,
        int sourceId)
    {
        var normalizedReference = EmptyToNull(fileReference);
        if (normalizedReference is null) return NotFound(new { message = $"{fileKind}不存在。" });

        if (!_awardStorage.IsStorageReference(normalizedReference))
        {
            if (IsSafeLegacyFileUrl(normalizedReference)) return Redirect(normalizedReference);
            return NotFound(new { message = $"{fileKind}文件引用无效。" });
        }

        try
        {
            var metadata = await _awardStorage.GetMetadataAsync(
                normalizedReference,
                HttpContext.RequestAborted);
            var storedObject = await _awardStorage.OpenReadAsync(
                normalizedReference,
                HttpContext.RequestAborted);
            if (metadata.ContentLength is > 0)
            {
                Response.ContentLength = metadata.ContentLength;
            }
            if (!string.IsNullOrWhiteSpace(metadata.ContentDisposition) &&
                metadata.ContentDisposition.IndexOfAny(['\r', '\n']) < 0)
            {
                Response.Headers.ContentDisposition = metadata.ContentDisposition;
                return File(
                    storedObject.Content,
                    metadata.ContentType ?? "application/octet-stream");
            }

            return File(
                storedObject.Content,
                metadata.ContentType ?? "application/octet-stream",
                downloadName);
        }
        catch (Exception exception) when (IsObjectStorageFailure(exception))
        {
            _logger.LogError(exception, "{FileKind} {SourceId} 无法从 OSS 读取。", fileKind, sourceId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "文件存储暂不可用",
                detail: "无法从 OSS 读取文件，请稍后重试。");
        }
    }

    private async Task TryRemoveAwardObjectAsync(string? storageReference, string context)
    {
        if (!_awardStorage.IsStorageReference(storageReference)) return;
        try
        {
            await _awardStorage.RemoveAsync(storageReference!, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "{Context} 的旧文件清理失败。", context);
        }
    }

    private static bool IsBlockedUploadExtension(string extension) =>
        extension is ".exe" or ".dll" or ".com" or ".bat" or ".cmd" or ".ps1" or
            ".sh" or ".msi" or ".scr" or ".js" or ".vbs";

    private static bool IsObjectStorageFailure(Exception exception) =>
        exception is AwardObjectStorageException;

    private static bool IsSafeLegacyFileUrl(string value)
    {
        if (value.IndexOfAny(['\r', '\n']) >= 0) return false;
        if (value.StartsWith("/", StringComparison.Ordinal))
            return !value.StartsWith("//", StringComparison.Ordinal);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static void AddAwardLevels(
        AwardScheme scheme,
        IReadOnlyCollection<Org.OpenAPITools.Models.AwardLevelInput> levels,
        DateTime now)
    {
        var order = 1;
        foreach (var input in levels)
        {
            scheme.Levels.Add(new AwardLevel
            {
                LevelName = input.LevelName.Trim(),
                AwardScore = ClampAwardScore(input.AwardScore),
                Amount = input.Amount,
                Quota = input.Quota,
                DisplayOrder = input.DisplayOrder ?? order,
                LevelStatus = ToAwardLevelStatus(input.LevelStatus),
                CreatedAt = now,
                UpdatedAt = now
            });
            order++;
        }
    }

    private static string? SyncAwardLevels(
        AwardScheme scheme,
        IReadOnlyCollection<Org.OpenAPITools.Models.AwardLevelInput> levels,
        DateTime now)
    {
        var existing = scheme.Levels.ToDictionary(level => level.AwardLevelId);
        var seen = new HashSet<int>();
        var order = 1;
        foreach (var input in levels)
        {
            if (input.AwardLevelId is null)
            {
                scheme.Levels.Add(new AwardLevel
                {
                    AwardSchemeId = scheme.AwardSchemeId,
                    LevelName = input.LevelName.Trim(),
                    AwardScore = ClampAwardScore(input.AwardScore),
                    Amount = input.Amount,
                    Quota = input.Quota,
                    DisplayOrder = input.DisplayOrder ?? order,
                    LevelStatus = ToAwardLevelStatus(input.LevelStatus),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else if (existing.TryGetValue(input.AwardLevelId.Value, out var level))
            {
                seen.Add(level.AwardLevelId);
                level.LevelName = input.LevelName.Trim();
                level.AwardScore = ClampAwardScore(input.AwardScore);
                level.Amount = input.Amount;
                level.Quota = input.Quota;
                level.DisplayOrder = input.DisplayOrder ?? order;
                level.LevelStatus = ToAwardLevelStatus(input.LevelStatus);
                level.UpdatedAt = now;
            }
            else
            {
                return "奖项等级不属于当前奖项配置。";
            }

            order++;
        }

        foreach (var level in scheme.Levels.Where(level => level.AwardLevelId > 0 && !seen.Contains(level.AwardLevelId)))
        {
            level.LevelStatus = "inactive";
            level.UpdatedAt = now;
        }

        return null;
    }

    private static bool IsAwardSchemeOpenForApplication(AwardScheme scheme)
    {
        if (scheme.SchemeStatus != AwardSchemeOpen) return false;
        var now = BusinessNow();
        return (scheme.ApplicationStartAt is null || scheme.ApplicationStartAt <= now) &&
               (scheme.ApplicationEndAt is null || scheme.ApplicationEndAt >= now);
    }

    private async Task<IActionResult?> EnsureAwardApplicationPublicityEndedAsync(
        int clubId,
        int awardApplicationId,
        DateTime now)
    {
        var batch = await _db.AwardPublicityBatches
            .AsNoTracking()
            .Where(b => b.ClubId == clubId &&
                        b.Items.Any(i => i.AwardApplicationId == awardApplicationId))
            .OrderByDescending(b => b.PublicityEndAt)
            .ThenByDescending(b => b.PublicityBatchId)
            .FirstOrDefaultAsync();
        if (batch is null)
            return Conflict(new { message = "请先创建并发布公示批次后再归档。" });
        if (batch.PublicityStatus == AwardPublicityDraft)
            return Conflict(new { message = "公示批次尚未发布，不能归档。" });
        if (batch.PublicityEndAt is null)
            return Conflict(new { message = "公示结束时间未设置，不能归档。" });
        if (batch.PublicityEndAt > now)
            return Conflict(new { message = "公示期尚未结束，不能提前归档。" });

        return null;
    }

    private IActionResult? ValidateAwardReviewPermission(
        User viewer,
        Club club,
        AwardApplication application,
        string reviewResult)
    {
        if (reviewResult is AwardReviewPublish or AwardReviewArchive or AwardReviewWithdraw)
        {
            return CanMaintainAwardWorkflow(viewer, club)
                ? null
                : StatusCode(403, new { message = "只有本社团负责人、指导老师或系统管理员可以处理公示和归档。" });
        }

        if (application.CurrentStep == AwardStepClubReview)
        {
            return UsersController.IsSystemAdmin(viewer) || IsClubPrincipal(viewer, club)
                ? null
                : StatusCode(403, new { message = "负责人初审只能由本社团负责人或系统管理员处理。" });
        }

        if (application.CurrentStep == AwardStepAdvisorReview)
        {
            return UsersController.IsSystemAdmin(viewer) || UsersController.IsClubAdvisor(viewer, club.ClubId)
                ? null
                : StatusCode(403, new { message = "指导老师审核只能由本社团指导老师或系统管理员处理。" });
        }

        if (application.CurrentStep == AwardStepSchoolReview)
        {
            return UsersController.IsPlatformAdmin(viewer) || UsersController.IsSystemAdmin(viewer)
                ? null
                : StatusCode(403, new { message = "校级终审只能由平台或系统管理员处理。" });
        }

        return StatusCode(403, new { message = "当前步骤不支持审核操作。" });
    }

    private (string? Status, string? Step, string? PublicStatus, IActionResult? Error)
        ResolveAwardReviewTransition(AwardApplication application, string reviewResult)
    {
        if (reviewResult == AwardReviewReturn)
            return (AwardStatusReturned, AwardStepStudentSubmit, AwardPublicNone, null);
        if (reviewResult == AwardReviewReject)
            return (AwardStatusRejected, application.CurrentStep, AwardPublicNone, null);
        if (reviewResult == AwardReviewWithdraw)
            return (AwardStatusWithdrawn, application.CurrentStep, AwardPublicWithdrawn, null);

        if (reviewResult == AwardReviewPublish)
        {
            return application.ApplicationStatus == AwardStatusApproved
                ? (AwardStatusPublicizing, AwardStepPublicity, AwardPublicizing, null)
                : (null, null, null, Conflict(new { message = "只有已通过终审的申请可以发布公示。" }));
        }

        if (reviewResult == AwardReviewArchive)
        {
            return application.ApplicationStatus == AwardStatusPublicizing
                ? (AwardStatusArchived, AwardStepArchived, AwardPublicized, null)
                : (null, null, null, Conflict(new { message = "只有公示中且公示期已结束的申请可以归档。" }));
        }

        if (reviewResult != AwardReviewApprove)
            return (null, null, null, BadRequest(new { message = "审核动作不合法。" }));

        return application.CurrentStep switch
        {
            AwardStepClubReview => (AwardStatusAdvisorReview, AwardStepAdvisorReview, null, null),
            AwardStepAdvisorReview => (AwardStatusSchoolReview, AwardStepSchoolReview, null, null),
            AwardStepSchoolReview => (AwardStatusApproved, AwardStepPublicity, AwardPublicNone, null),
            _ => (null, null, null, Conflict(new { message = "当前申请状态不允许通过。" }))
        };
    }

    private static AwardReviewRecord NewAwardReviewRecord(
        AwardApplication application,
        int reviewerUserId,
        string reviewStep,
        string reviewResult,
        string? fromStatus,
        string? toStatus,
        string? comment,
        DateTime reviewedAt) =>
        new()
        {
            AwardApplicationId = application.AwardApplicationId,
            ReviewRound = application.ReviewRound,
            ReviewStep = reviewStep,
            ReviewResult = reviewResult,
            ReviewerUserId = reviewerUserId,
            ReviewComment = comment,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ReviewedAt = reviewedAt
        };

    private static AwardRuleDocumentRecordDto ToAwardRuleDocumentRecordDto(AwardRuleDocument document) => new(
        document.RuleDocumentId,
        document.ClubId,
        document.Club?.ClubName,
        document.RuleTitle,
        document.RuleScope,
        AwardRuleScopeText(document.RuleScope),
        document.AcademicYear,
        document.TermName,
        document.IssuerName,
        document.Summary,
        document.ContentText,
        document.MaterialUrl,
        document.MaterialName,
        document.VersionNo,
        document.RuleStatus,
        AwardRuleStatusText(document.RuleStatus),
        document.EffectiveStartAt,
        document.EffectiveEndAt,
        document.PublishedByUserId,
        DisplayUser(document.PublishedByUser),
        document.PublishedAt,
        document.CreatedAt,
        document.UpdatedAt);

    private static AwardSchemeRecordDto ToAwardSchemeRecordDto(AwardScheme scheme) => new(
        scheme.AwardSchemeId,
        scheme.ClubId,
        scheme.Club?.ClubName ?? $"社团 {scheme.ClubId}",
        scheme.AwardName,
        scheme.AwardCategory,
        scheme.AcademicYear,
        scheme.TermName,
        scheme.SponsorUnit,
        scheme.RewardLevel,
        scheme.FundingSource,
        scheme.IsRanked == 1,
        scheme.IsFixedAmount == 1,
        scheme.Description,
        scheme.MaterialDescription,
        scheme.ApplicationStartAt,
        scheme.ApplicationEndAt,
        scheme.PublicityStartAt,
        scheme.PublicityEndAt,
        scheme.SchemeStatus,
        AwardSchemeStatusText(scheme.SchemeStatus),
        scheme.CreatedByUserId,
        DisplayUser(scheme.CreatedByUser),
        scheme.CreatedAt,
        scheme.UpdatedAt,
        scheme.Levels
            .OrderBy(level => level.DisplayOrder)
            .ThenBy(level => level.LevelName)
            .Select(ToAwardLevelRecordDto)
            .ToArray());

    private static AwardLevelRecordDto ToAwardLevelRecordDto(AwardLevel level) => new(
        level.AwardLevelId,
        level.AwardSchemeId,
        level.LevelName,
        level.AwardScore,
        level.Amount,
        level.Quota,
        level.DisplayOrder,
        level.LevelStatus);

    private static AwardApplicationRecordDto ToAwardApplicationRecordDto(AwardApplication application) => new(
        application.AwardApplicationId,
        application.ClubId,
        application.Club?.ClubName ?? $"社团 {application.ClubId}",
        application.AwardSchemeId,
        application.Scheme?.AwardName ?? $"奖项 {application.AwardSchemeId}",
        application.Scheme?.AwardCategory ?? "honor",
        application.Scheme?.AcademicYear ?? string.Empty,
        application.Scheme?.TermName,
        application.AwardLevelId,
        application.Level?.LevelName ?? $"等级 {application.AwardLevelId}",
        application.ApplicantUserId,
        DisplayUser(application.Applicant) ?? $"用户 {application.ApplicantUserId}",
        application.Applicant?.StudentNo,
        application.RecommenderUserId,
        DisplayUser(application.Recommender),
        application.SubmitterUserId,
        DisplayUser(application.Submitter),
        application.ApplicationType,
        application.ApplicationReason,
        application.MaterialUrl,
        application.CurrentStep,
        AwardStepText(application.CurrentStep),
        application.ApplicationStatus,
        AwardApplicationStatusText(application.ApplicationStatus),
        application.PublicStatus,
        application.ReviewRound,
        application.FinalAwardScore,
        application.FinalAmount,
        application.SubmittedAt,
        application.ApprovedAt,
        application.PublicizedAt,
        application.ArchivedAt,
        application.CreatedAt,
        application.UpdatedAt,
        application.ReviewRecords
            .OrderBy(record => record.ReviewedAt)
            .Select(ToAwardReviewRecordDto)
            .ToArray(),
        application.Attachments
            .OrderBy(attachment => attachment.UploadedAt)
            .Select(ToAwardAttachmentRecordDto)
            .ToArray());

    private static AwardReviewRecordDto ToAwardReviewRecordDto(AwardReviewRecord record) => new(
        record.ReviewId,
        record.AwardApplicationId,
        record.ReviewRound,
        record.ReviewStep,
        record.ReviewResult,
        record.ReviewerUserId,
        DisplayUser(record.Reviewer),
        record.ReviewComment,
        record.FromStatus,
        record.ToStatus,
        record.ReviewedAt);

    private static AwardAttachmentRecordDto ToAwardAttachmentRecordDto(AwardAttachment attachment) => new(
        attachment.AttachmentId,
        attachment.AwardApplicationId,
        attachment.AttachmentName,
        attachment.AttachmentUrl,
        attachment.AttachmentType,
        attachment.UploadedByUserId,
        DisplayUser(attachment.UploadedByUser),
        attachment.UploadedAt);

    private static AwardPublicityBatchRecordDto ToAwardPublicityBatchRecordDto(AwardPublicityBatch batch) => new(
        batch.PublicityBatchId,
        batch.ClubId,
        batch.Club?.ClubName ?? $"社团 {batch.ClubId}",
        batch.Title,
        batch.Description,
        batch.PublicityStartAt,
        batch.PublicityEndAt,
        batch.PublicityStatus,
        AwardPublicityStatusText(batch.PublicityStatus),
        batch.PublisherUserId,
        DisplayUser(batch.Publisher),
        batch.CreatedAt,
        batch.UpdatedAt,
        batch.Items
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.PublicityItemId)
            .Select(ToAwardPublicityItemRecordDto)
            .ToArray());

    private static AwardPublicityItemRecordDto ToAwardPublicityItemRecordDto(AwardPublicityItem item)
    {
        var application = item.Application;
        return new AwardPublicityItemRecordDto(
            item.PublicityItemId,
            item.PublicityBatchId,
            item.AwardApplicationId,
            application?.ApplicantUserId ?? 0,
            DisplayUser(application?.Applicant) ?? "未知成员",
            application?.Scheme?.AwardName ?? "未知奖项",
            application?.Level?.LevelName ?? "未知等级",
            application?.FinalAwardScore,
            application?.FinalAmount,
            item.DisplayOrder,
            item.PublicityResult,
            item.CreatedAt);
    }

    private static string ToAwardCategory(CreateAwardSchemeRequest.AwardCategoryEnum value) => value switch
    {
        CreateAwardSchemeRequest.AwardCategoryEnum.ScholarshipEnum => "scholarship",
        CreateAwardSchemeRequest.AwardCategoryEnum.CompetitionEnum => "competition",
        CreateAwardSchemeRequest.AwardCategoryEnum.ServiceEnum => "service",
        CreateAwardSchemeRequest.AwardCategoryEnum.OtherEnum => "other",
        _ => "honor"
    };

    private static string ToAwardCategory(UpdateAwardSchemeRequest.AwardCategoryEnum value) => value switch
    {
        UpdateAwardSchemeRequest.AwardCategoryEnum.ScholarshipEnum => "scholarship",
        UpdateAwardSchemeRequest.AwardCategoryEnum.CompetitionEnum => "competition",
        UpdateAwardSchemeRequest.AwardCategoryEnum.ServiceEnum => "service",
        UpdateAwardSchemeRequest.AwardCategoryEnum.OtherEnum => "other",
        _ => "honor"
    };

    private static string ToAwardSchemeStatus(CreateAwardSchemeRequest.SchemeStatusEnum value) => value switch
    {
        CreateAwardSchemeRequest.SchemeStatusEnum.OpenEnum => AwardSchemeOpen,
        CreateAwardSchemeRequest.SchemeStatusEnum.ReviewingEnum => AwardSchemeReviewing,
        CreateAwardSchemeRequest.SchemeStatusEnum.PublicizingEnum => AwardSchemePublicizing,
        CreateAwardSchemeRequest.SchemeStatusEnum.ArchivedEnum => AwardSchemeArchived,
        CreateAwardSchemeRequest.SchemeStatusEnum.ClosedEnum => AwardSchemeClosed,
        _ => AwardSchemeDraft
    };

    private static string ToAwardSchemeStatus(UpdateAwardSchemeRequest.SchemeStatusEnum value) => value switch
    {
        UpdateAwardSchemeRequest.SchemeStatusEnum.OpenEnum => AwardSchemeOpen,
        UpdateAwardSchemeRequest.SchemeStatusEnum.ReviewingEnum => AwardSchemeReviewing,
        UpdateAwardSchemeRequest.SchemeStatusEnum.PublicizingEnum => AwardSchemePublicizing,
        UpdateAwardSchemeRequest.SchemeStatusEnum.ArchivedEnum => AwardSchemeArchived,
        UpdateAwardSchemeRequest.SchemeStatusEnum.ClosedEnum => AwardSchemeClosed,
        _ => AwardSchemeDraft
    };

    private static string ToAwardLevelStatus(Org.OpenAPITools.Models.AwardLevelInput.LevelStatusEnum value) =>
        value == Org.OpenAPITools.Models.AwardLevelInput.LevelStatusEnum.InactiveEnum ? "inactive" : "active";

    private static string ToAwardApplicationType(CreateAwardApplicationRequest.ApplicationTypeEnum value) =>
        value == CreateAwardApplicationRequest.ApplicationTypeEnum.RecommendationEnum
            ? AwardApplicationRecommendation
            : AwardApplicationSelf;

    private static string ToAwardReviewResult(ReviewAwardApplicationRequest.ReviewResultEnum value) => value switch
    {
        ReviewAwardApplicationRequest.ReviewResultEnum.RejectEnum => AwardReviewReject,
        ReviewAwardApplicationRequest.ReviewResultEnum.ReturnEnum => AwardReviewReturn,
        ReviewAwardApplicationRequest.ReviewResultEnum.PublishEnum => AwardReviewPublish,
        ReviewAwardApplicationRequest.ReviewResultEnum.ArchiveEnum => AwardReviewArchive,
        ReviewAwardApplicationRequest.ReviewResultEnum.WithdrawEnum => AwardReviewWithdraw,
        _ => AwardReviewApprove
    };

    private static string? NormalizeAwardSchemeStatus(string? value) =>
        NormalizeToKnown(value, AwardSchemeDraft, AwardSchemeOpen, AwardSchemeReviewing, AwardSchemePublicizing, AwardSchemeArchived, AwardSchemeClosed);

    private static string? NormalizeAwardApplicationStatus(string? value) =>
        NormalizeToKnown(value, AwardStatusDraft, AwardStatusClubReview, AwardStatusAdvisorReview, AwardStatusSchoolReview, AwardStatusReturned, AwardStatusRejected, AwardStatusApproved, AwardStatusPublicizing, "publicized", AwardStatusArchived, AwardStatusWithdrawn);

    private static string? NormalizeAwardStep(string? value) =>
        NormalizeToKnown(value, AwardStepStudentSubmit, AwardStepClubReview, AwardStepAdvisorReview, AwardStepSchoolReview, AwardStepPublicity, AwardStepArchived);

    private static string? NormalizeAwardPublicityStatus(string? value) =>
        NormalizeToKnown(value, AwardPublicityDraft, AwardPublicityPublicizing, AwardPublicityClosed, AwardPublicityArchived);

    private static string? NormalizeAwardRuleScope(string? value) =>
        NormalizeToKnown(value, AwardRuleScopeGlobal, AwardRuleScopeClub);

    private static string? NormalizeAwardRuleStatus(string? value) =>
        NormalizeToKnown(value, AwardRuleStatusDraft, AwardRuleStatusPublished, AwardRuleStatusArchived);

    private static string? NormalizeToKnown(string? value, params string[] known)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        return known.Contains(normalized) ? normalized : null;
    }

    private static string ReviewStepForRecord(string currentStep, string reviewResult) =>
        reviewResult == AwardReviewArchive ? AwardReviewStepArchive : currentStep;

    private static decimal ClampAwardScore(decimal value) => Math.Min(Math.Max(value, 0m), 100m);

    private static DateTime BusinessToday() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone).Date;

    private static DateTime BusinessNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone);

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? DisplayUser(User? user)
    {
        if (user is null) return null;
        var name = !string.IsNullOrWhiteSpace(user.RealName) ? user.RealName : user.Username;
        var studentNo = string.IsNullOrWhiteSpace(user.StudentNo) ? string.Empty : $"（{user.StudentNo}）";
        return string.IsNullOrWhiteSpace(name) ? $"用户 {user.UserId}" : $"{name}{studentNo}";
    }

    private static string AwardRuleScopeText(string? scope) => scope switch
    {
        AwardRuleScopeGlobal => "学校细则",
        AwardRuleScopeClub => "社团细则",
        _ => "未知范围"
    };

    private static string AwardRuleStatusText(string? status) => status switch
    {
        AwardRuleStatusDraft => "草稿",
        AwardRuleStatusPublished => "已发布",
        AwardRuleStatusArchived => "已归档",
        _ => "未知状态"
    };

    private static string AwardSchemeStatusText(string? status) => status switch
    {
        AwardSchemeDraft => "草稿",
        AwardSchemeOpen => "开放申请",
        AwardSchemeReviewing => "审核中",
        AwardSchemePublicizing => "公示中",
        AwardSchemeArchived => "已归档",
        AwardSchemeClosed => "已关闭",
        _ => "未知状态"
    };

    private static string AwardStepText(string? step) => step switch
    {
        AwardStepStudentSubmit => "学生申请",
        AwardStepClubReview => "待负责人初审",
        AwardStepAdvisorReview => "待指导老师审核",
        AwardStepSchoolReview => "待校级终审",
        AwardStepPublicity => "待公示",
        AwardStepArchived => "已归档",
        _ => "未知步骤"
    };

    private static string AwardApplicationStatusText(string? status) => status switch
    {
        AwardStatusDraft => "草稿",
        AwardStatusClubReview or AwardStatusAdvisorReview or AwardStatusSchoolReview => "审核中",
        AwardStatusReturned => "已退回",
        AwardStatusRejected => "未通过",
        AwardStatusApproved => "已通过",
        AwardStatusPublicizing => "公示中",
        "publicized" => "已公示",
        AwardStatusArchived => "已归档",
        AwardStatusWithdrawn => "已撤回",
        _ => "未知状态"
    };

    private static string AwardPublicityStatusText(string? status) => status switch
    {
        AwardPublicityDraft => "草稿",
        AwardPublicityPublicizing => "公示中",
        AwardPublicityClosed => "已结束",
        AwardPublicityArchived => "已归档",
        _ => "未知状态"
    };
}

public record AwardLevelRecordDto(
    int AwardLevelId,
    int AwardSchemeId,
    string LevelName,
    decimal AwardScore,
    decimal? Amount,
    int? Quota,
    int DisplayOrder,
    string LevelStatus);

public record AwardRuleDocumentRequest(
    string? RuleTitle,
    string? RuleScope,
    string? AcademicYear,
    string? TermName,
    string? IssuerName,
    string? Summary,
    string? ContentText,
    string? MaterialUrl,
    string? MaterialName,
    string? VersionNo,
    string? RuleStatus,
    DateTime? EffectiveStartAt,
    DateTime? EffectiveEndAt);

public record AwardRuleDocumentRecordDto(
    int RuleDocumentId,
    int? ClubId,
    string? ClubName,
    string RuleTitle,
    string RuleScope,
    string RuleScopeText,
    string AcademicYear,
    string? TermName,
    string? IssuerName,
    string? Summary,
    string? ContentText,
    string? MaterialUrl,
    string? MaterialName,
    string VersionNo,
    string RuleStatus,
    string RuleStatusText,
    DateTime? EffectiveStartAt,
    DateTime? EffectiveEndAt,
    int? PublishedByUserId,
    string? PublishedByName,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record AwardSchemeRecordDto(
    int AwardSchemeId,
    int ClubId,
    string ClubName,
    string AwardName,
    string AwardCategory,
    string AcademicYear,
    string? TermName,
    string? SponsorUnit,
    string? RewardLevel,
    string? FundingSource,
    bool IsRanked,
    bool IsFixedAmount,
    string? Description,
    string? MaterialDescription,
    DateTime? ApplicationStartAt,
    DateTime? ApplicationEndAt,
    DateTime? PublicityStartAt,
    DateTime? PublicityEndAt,
    string SchemeStatus,
    string SchemeStatusText,
    int? CreatedByUserId,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyCollection<AwardLevelRecordDto> Levels);

public record AwardReviewRecordDto(
    int ReviewId,
    int AwardApplicationId,
    int ReviewRound,
    string ReviewStep,
    string ReviewResult,
    int? ReviewerUserId,
    string? ReviewerName,
    string? ReviewComment,
    string? FromStatus,
    string? ToStatus,
    DateTime ReviewedAt);

public record AwardAttachmentRecordDto(
    int AttachmentId,
    int AwardApplicationId,
    string AttachmentName,
    string AttachmentUrl,
    string? AttachmentType,
    int UploadedByUserId,
    string? UploadedByName,
    DateTime UploadedAt);

public record AwardApplicationRecordDto(
    int AwardApplicationId,
    int ClubId,
    string ClubName,
    int AwardSchemeId,
    string AwardName,
    string AwardCategory,
    string AcademicYear,
    string? TermName,
    int AwardLevelId,
    string LevelName,
    int ApplicantUserId,
    string ApplicantName,
    string? ApplicantStudentNo,
    int? RecommenderUserId,
    string? RecommenderName,
    int SubmitterUserId,
    string? SubmitterName,
    string ApplicationType,
    string? ApplicationReason,
    string? MaterialUrl,
    string CurrentStep,
    string CurrentStepText,
    string ApplicationStatus,
    string ApplicationStatusText,
    string PublicStatus,
    int ReviewRound,
    decimal? FinalAwardScore,
    decimal? FinalAmount,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    DateTime? PublicizedAt,
    DateTime? ArchivedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyCollection<AwardReviewRecordDto> ReviewRecords,
    IReadOnlyCollection<AwardAttachmentRecordDto> Attachments);

public record AwardPublicityItemRecordDto(
    int PublicityItemId,
    int PublicityBatchId,
    int AwardApplicationId,
    int ApplicantUserId,
    string ApplicantName,
    string AwardName,
    string LevelName,
    decimal? FinalAwardScore,
    decimal? FinalAmount,
    int DisplayOrder,
    string PublicityResult,
    DateTime CreatedAt);

public record AwardPublicityBatchRecordDto(
    int PublicityBatchId,
    int ClubId,
    string ClubName,
    string Title,
    string? Description,
    DateTime? PublicityStartAt,
    DateTime? PublicityEndAt,
    string PublicityStatus,
    string PublicityStatusText,
    int? PublisherUserId,
    string? PublisherName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyCollection<AwardPublicityItemRecordDto> Items);
