using ClubHub.Api.Controllers;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using CreateRecruitmentRequest = Org.OpenAPITools.Models.CreateRecruitmentRequest;
using RecruitmentApplicationDto = Org.OpenAPITools.Models.RecruitmentApplication;
using RecruitmentDto = Org.OpenAPITools.Models.Recruitment;
using ReviewRecruitmentApplicationRequest = Org.OpenAPITools.Models.ReviewRecruitmentApplicationRequest;
using ReviewRecruitmentRequest = Org.OpenAPITools.Models.ReviewRecruitmentRequest;

namespace ClubHub.Api.Services;

public static class RecruitmentWorkflow
{
    public const string ApplicationPending = "pending";
    public const string ApplicationAccepted = "accepted";
    public const string ApplicationRejected = "rejected";
    public const string ReviewApproved = "approved";
    public const string ReviewRejected = "rejected";
    public const string ClubApproved = "approved";
    public const string ClubActive = "active";
    public const string MemberActive = "active";
    public const string ClubMemberRoleCode = "CLUB_MEMBER";
    public const int MaxStudentClubMemberships = 3;

    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();
    private static readonly HashSet<string> RecruitmentManagerRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "club_officer",
        "club_leader"
    };
    private static readonly HashSet<string> ClubMembershipRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ClubMemberRoleCode,
        "CLUB_OFFICER",
        "CLUB_LEADER",
        "club_president"
    };

    public static RecruitmentDto ToRecruitmentDto(Recruitment recruitment, User viewer, DateTime now)
    {
        var currentApplication = recruitment.Applications
            .Where(a => a.UserId == viewer.UserId)
            .OrderByDescending(a => a.SubmittedAt)
            .ThenByDescending(a => a.ApplicationId)
            .FirstOrDefault();

        var status = EffectiveRecruitmentStatus(recruitment, now);
        var applicationStatus = NormalizeApplicationStatus(currentApplication?.ApplicationStatus);
        var currentUserIsMember = CurrentUserIsMemberOfClub(viewer, recruitment.ClubId);
        var isOwnProposal = IsOwnRecruitmentProposal(viewer, recruitment);

        return new RecruitmentDto
        {
            Id = recruitment.RecruitId,
            ClubId = recruitment.ClubId,
            ClubName = recruitment.Club?.ClubName ?? $"社团 {recruitment.ClubId}",
            Title = recruitment.Title,
            Description = recruitment.Description,
            StartAt = recruitment.StartAt,
            EndAt = recruitment.EndAt,
            Quota = recruitment.Quota,
            Requirements = recruitment.Requirements,
            RecruitStatus = ToRecruitmentStatusEnum(status),
            RecruitStatusText = RecruitmentStatusText(status),
            CreatedAt = recruitment.CreatedAt,
            ApplicationCount = recruitment.Applications.Count,
            AcceptedCount = recruitment.Applications.Count(a => a.ApplicationStatus == ApplicationAccepted),
            CurrentUserApplicationId = currentApplication?.ApplicationId,
            CurrentUserApplicationStatus = applicationStatus,
            CurrentUserApplicationStatusText = applicationStatus is null ? null : ApplicationStatusText(applicationStatus),
            CurrentUserIsMember = currentUserIsMember,
            IsOwnProposal = isOwnProposal,
            CanManage = CanManageRecruitment(viewer, recruitment.ClubId),
            CanEdit = CanEditRecruitment(viewer, recruitment),
            CanDelete = CanDeleteDraftRecruitment(viewer, recruitment),
            CanReview = CanReviewRecruitment(viewer, recruitment)
        };
    }

    public static RecruitmentApplicationDto ToApplicationDto(RecruitmentApplication application)
    {
        var status = NormalizeApplicationStatus(application.ApplicationStatus) ?? ApplicationPending;

        return new RecruitmentApplicationDto
        {
            Id = application.ApplicationId,
            RecruitId = application.RecruitId,
            RecruitTitle = application.Recruitment?.Title ?? $"招募 {application.RecruitId}",
            ClubId = application.Recruitment?.ClubId ?? 0,
            ClubName = application.Recruitment?.Club?.ClubName ?? "未知社团",
            UserId = application.UserId,
            ApplicantName = DisplayUser(application.User) ?? $"用户 {application.UserId}",
            StudentNo = application.User?.StudentNo,
            ApplicationReason = application.ApplicationReason ?? string.Empty,
            InterviewScore = application.InterviewScore is null ? null : Convert.ToDouble(application.InterviewScore.Value),
            ApplicationStatus = ToApplicationStatusEnum(status),
            ApplicationStatusText = ApplicationStatusText(status),
            ReviewerUserId = application.ReviewerUserId,
            ReviewerName = DisplayUser(application.Reviewer),
            SubmittedAt = application.SubmittedAt,
            ReviewedAt = application.ReviewedAt
        };
    }

    public static string? ValidateCreateRecruitmentRequest(CreateRecruitmentRequest req)
    {
        if (req.CurrentUserId <= 0) return "请选择当前操作用户。";
        if (req.ClubId <= 0) return "请选择发布招募的社团。";
        return ValidateRecruitmentState(req.Title, req.StartAt, req.EndAt, req.Quota, req.Requirements);
    }

    public static string? ValidateRecruitmentState(
        string? title,
        DateTime? startAt,
        DateTime? endAt,
        int? quota,
        string? requirements)
    {
        if (string.IsNullOrWhiteSpace(title)) return "招募标题不能为空。";
        if (startAt is null || startAt.Value == default) return "招募开始时间不能为空。";
        if (endAt is null || endAt.Value == default) return "招募结束时间不能为空。";
        if (endAt.Value <= startAt.Value) return "招募结束时间必须晚于开始时间。";
        if (quota is null or <= 0) return "招募人数必须大于 0。";
        if (string.IsNullOrWhiteSpace(requirements)) return "招募要求不能为空。";
        return null;
    }

    public static bool CanViewRecruitment(User viewer, Recruitment recruitment)
    {
        var status = NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) ?? RecruitmentStatuses.Draft;
        if (status == RecruitmentStatuses.Draft) return CanManageRecruitment(viewer, recruitment.ClubId);
        if (status == RecruitmentStatuses.PendingReview)
        {
            return CanManageRecruitment(viewer, recruitment.ClubId) || CanReviewRecruitment(viewer, recruitment);
        }

        return true;
    }

    public static bool CanManageRecruitment(User user, int clubId) =>
        UsersController.IsSystemAdmin(user) || HasClubRecruitmentManagerRole(user, clubId);

    public static bool HasClubRecruitmentManagerRole(User user, int clubId) =>
        user.UserRoles.Any(ur =>
            ur.ClubId == clubId &&
            IsRecruitmentManagerRole(ur.Role));

    public static bool IsOwnRecruitmentProposal(User user, Recruitment recruitment) =>
        HasClubRecruitmentManagerRole(user, recruitment.ClubId);

    public static bool CanEditRecruitment(User user, Recruitment recruitment) =>
        NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) == RecruitmentStatuses.Draft &&
        CanManageRecruitment(user, recruitment.ClubId);

    public static bool CanDeleteDraftRecruitment(User user, Recruitment recruitment) =>
        CanEditRecruitment(user, recruitment);

    public static bool CanReviewRecruitment(User user, Recruitment recruitment) =>
        (UsersController.IsPlatformAdmin(user) || UsersController.IsSystemAdmin(user)) &&
        !HasClubRecruitmentManagerRole(user, recruitment.ClubId);

    public static bool IsRecruitmentManagerRole(Role? role) =>
        role is not null && RecruitmentManagerRoleCodes.Contains(Normalize(role.RoleCode));

    public static bool IsClubMembershipRole(Role? role) =>
        role is not null && ClubMembershipRoleCodes.Contains(Normalize(role.RoleCode));

    public static bool CurrentUserIsMemberOfClub(User viewer, int clubId) =>
        viewer.ClubMemberships.Any(m => m.ClubId == clubId && IsCurrentMemberRecord(m)) ||
        viewer.UserRoles.Any(ur => ur.ClubId == clubId && IsClubMembershipRole(ur.Role));

    public static bool IsCurrentMemberRecord(ClubMember member)
    {
        var today = BusinessToday();
        return (member.MemberStatus == null || member.MemberStatus == MemberActive) &&
               (member.TermStart == null || member.TermStart.Value.Date <= today) &&
               (member.TermEnd == null || member.TermEnd.Value.Date >= today);
    }

    public static bool IsMaintainableClub(Club club) =>
        club.AuditStatus == ClubApproved && club.ClubStatus == ClubActive;

    public static string EffectiveRecruitmentStatus(Recruitment recruitment, DateTime now)
    {
        var storageStatus = NormalizeRecruitmentStorageStatus(recruitment.RecruitStatus) ?? RecruitmentStatuses.Draft;
        if (storageStatus == RecruitmentStatuses.Draft) return RecruitmentStatuses.Draft;
        if (storageStatus == RecruitmentStatuses.PendingReview) return RecruitmentStatuses.PendingReview;
        if (storageStatus == RecruitmentStatuses.Closed) return RecruitmentStatuses.Ended;

        if (recruitment.EndAt is not null && recruitment.EndAt.Value < now) return RecruitmentStatuses.Ended;
        if (recruitment.StartAt is not null && recruitment.StartAt.Value > now) return RecruitmentStatuses.NotStarted;
        return RecruitmentStatuses.Accepting;
    }

    public static string? NormalizeRecruitmentStorageStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        return Normalize(status) switch
        {
            RecruitmentStatuses.Draft or "草稿" => RecruitmentStatuses.Draft,
            RecruitmentStatuses.PendingReview or "pending" or "reviewing" or "审核中" or "待审核" => RecruitmentStatuses.PendingReview,
            RecruitmentStatuses.Published or "open" or "approved" or "报名中" or "申请中" or "发布" or "已通过" => RecruitmentStatuses.Published,
            RecruitmentStatuses.Closed or RecruitmentStatuses.Ended or "finished" or "结束" or "已结束" => RecruitmentStatuses.Closed,
            _ => null
        };
    }

    public static string? NormalizeRecruitmentWorkflowStatus(CreateRecruitmentRequest.RecruitStatusEnum status) =>
        status switch
        {
            CreateRecruitmentRequest.RecruitStatusEnum.DraftEnum => RecruitmentStatuses.Draft,
            CreateRecruitmentRequest.RecruitStatusEnum.PendingReviewEnum => RecruitmentStatuses.PendingReview,
            _ => null
        };

    public static string? NormalizeRecruitmentWorkflowStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        return Normalize(status) switch
        {
            RecruitmentStatuses.Draft or "草稿" => RecruitmentStatuses.Draft,
            RecruitmentStatuses.PendingReview or "pending" or "reviewing" or "审核中" or "待审核" => RecruitmentStatuses.PendingReview,
            _ => null
        };
    }

    public static string? NormalizeRecruitmentStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        return Normalize(status) switch
        {
            RecruitmentStatuses.Draft or "草稿" => RecruitmentStatuses.Draft,
            RecruitmentStatuses.PendingReview or "pending" or "reviewing" or "审核中" or "待审核" => RecruitmentStatuses.PendingReview,
            RecruitmentStatuses.NotStarted or "notstarted" or "未开始" => RecruitmentStatuses.NotStarted,
            RecruitmentStatuses.Accepting or RecruitmentStatuses.Published or "open" or "申请中" or "报名中" => RecruitmentStatuses.Accepting,
            RecruitmentStatuses.Ended or RecruitmentStatuses.Closed or "finished" or "结束" or "已结束" => RecruitmentStatuses.Ended,
            _ => null
        };
    }

    public static string? NormalizeRecruitmentReviewDecision(ReviewRecruitmentRequest.DecisionEnum decision) =>
        decision switch
        {
            ReviewRecruitmentRequest.DecisionEnum.ApprovedEnum => ReviewApproved,
            ReviewRecruitmentRequest.DecisionEnum.RejectedEnum => ReviewRejected,
            _ => null
        };

    public static string? NormalizeRecruitmentReviewDecision(string? decision)
    {
        if (string.IsNullOrWhiteSpace(decision)) return null;

        return Normalize(decision) switch
        {
            ReviewApproved or "accepted" or "approve" or "通过" or "审核通过" => ReviewApproved,
            ReviewRejected or "reject" or "驳回" or "审核驳回" => ReviewRejected,
            _ => null
        };
    }

    public static string? NormalizeApplicationStatus(ReviewRecruitmentApplicationRequest.DecisionEnum decision) =>
        decision switch
        {
            ReviewRecruitmentApplicationRequest.DecisionEnum.AcceptedEnum => ApplicationAccepted,
            ReviewRecruitmentApplicationRequest.DecisionEnum.RejectedEnum => ApplicationRejected,
            _ => null
        };

    public static string? NormalizeApplicationStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        return Normalize(status) switch
        {
            ApplicationPending or "待筛选" or "待审核" => ApplicationPending,
            ApplicationAccepted or "approved" or "录取" or "已录取" => ApplicationAccepted,
            ApplicationRejected or "拒绝" or "未录取" => ApplicationRejected,
            _ => null
        };
    }

    public static string RecruitmentStatusText(string status) => status switch
    {
        RecruitmentStatuses.Draft => "草稿",
        RecruitmentStatuses.PendingReview => "审核中",
        RecruitmentStatuses.NotStarted => "未开始",
        RecruitmentStatuses.Accepting => "申请中",
        RecruitmentStatuses.Ended => "已结束",
        _ => "未知"
    };

    public static RecruitmentDto.RecruitStatusEnum ToRecruitmentStatusEnum(string status) => status switch
    {
        RecruitmentStatuses.Draft => RecruitmentDto.RecruitStatusEnum.DraftEnum,
        RecruitmentStatuses.PendingReview => RecruitmentDto.RecruitStatusEnum.PendingReviewEnum,
        RecruitmentStatuses.NotStarted => RecruitmentDto.RecruitStatusEnum.NotStartedEnum,
        RecruitmentStatuses.Accepting => RecruitmentDto.RecruitStatusEnum.AcceptingEnum,
        RecruitmentStatuses.Ended => RecruitmentDto.RecruitStatusEnum.EndedEnum,
        _ => RecruitmentDto.RecruitStatusEnum.DraftEnum
    };

    public static RecruitmentApplicationDto.ApplicationStatusEnum ToApplicationStatusEnum(string status) => status switch
    {
        ApplicationPending => RecruitmentApplicationDto.ApplicationStatusEnum.PendingEnum,
        ApplicationAccepted => RecruitmentApplicationDto.ApplicationStatusEnum.AcceptedEnum,
        ApplicationRejected => RecruitmentApplicationDto.ApplicationStatusEnum.RejectedEnum,
        _ => RecruitmentApplicationDto.ApplicationStatusEnum.PendingEnum
    };

    public static string ApplicationStatusText(string status) => status switch
    {
        ApplicationPending => "待筛选",
        ApplicationAccepted => "已录取",
        ApplicationRejected => "未录取",
        _ => "未知"
    };

    public static DateTime BusinessNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone);

    public static DateTime BusinessToday() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone).Date;

    public static DateTime BusinessDate(DateTime utcDateTime)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, BusinessTimeZone).Date;
    }

    public static string? DisplayUser(User? user)
    {
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(user.RealName)) return user.RealName;
        if (!string.IsNullOrWhiteSpace(user.Username)) return user.Username;
        return $"用户 {user.UserId}";
    }

    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    public static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
}
