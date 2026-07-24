using System.Data;
using System.Reflection;
using System.Runtime.Serialization;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiBudgetAccount = Org.OpenAPITools.Models.BudgetAccount;
using ApiBudgetApplication = Org.OpenAPITools.Models.BudgetApplication;
using ApiBudgetTransaction = Org.OpenAPITools.Models.BudgetTransaction;
using ApiCancelBudgetApplicationRequest = Org.OpenAPITools.Models.CancelBudgetApplicationRequest;
using ApiCreateBudgetAccountRequest = Org.OpenAPITools.Models.CreateBudgetAccountRequest;
using ApiCreateBudgetApplicationRequest = Org.OpenAPITools.Models.CreateBudgetApplicationRequest;
using ApiError = Org.OpenAPITools.Models.ApiError;
using ApiReviewBudgetApplicationRequest = Org.OpenAPITools.Models.ReviewBudgetApplicationRequest;
using ApiUpdateBudgetAccountRequest = Org.OpenAPITools.Models.UpdateBudgetAccountRequest;

namespace ClubHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/budget")]
public class BudgetController : ControllerBase
{
    private const string BudgetViewPermission = "budget:view";
    private const string BudgetAccountManagePermission = "budget:account:manage";
    private const string BudgetApplyPermission = "budget:apply";
    private const string BudgetReviewPermission = "budget:review";
    private const string AccountStatusActive = "active";
    private const string AccountStatusClosed = "closed";
    private const string ApplicationStatusPending = "pending";
    private const string ApplicationStatusApproved = "approved";
    private const string ApplicationStatusRejected = "rejected";
    private const string ApplicationStatusCancelled = "cancelled";
    private const string TransactionTypeCommitment = "commitment";
    private const int MaxTitleLength = 255;
    private const int MaxPurposeLength = 255;
    private const int MaxDetailLength = 4000;
    private const int MaxCommentLength = 255;
    private const int MaxWriteRetries = 3;
    internal const IsolationLevel BudgetApprovalIsolationLevel = IsolationLevel.ReadCommitted;

    private static readonly HashSet<string> AllowedAccountStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        AccountStatusActive,
        AccountStatusClosed
    };

    private static readonly HashSet<string> AllowedApplicationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "activity_budget",
        "purchase",
        "reimbursement"
    };

    private static readonly HashSet<string> AllowedApplicationStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationStatusPending,
        ApplicationStatusApproved,
        ApplicationStatusRejected,
        ApplicationStatusCancelled
    };

    private static readonly HashSet<string> AllowedTransactionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        TransactionTypeCommitment,
        "expense",
        "refund",
        "adjustment"
    };

    private readonly ClubHubDbContext _db;
    private readonly AuthService _authService;

    public BudgetController(ClubHubDbContext db, AuthService authService)
    {
        _db = db;
        _authService = authService;
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> GetAccounts([FromQuery] int? clubId, [FromQuery] string? fiscalYear)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var access = await GetBudgetAccessAsync(
            currentUserId.Value,
            BudgetViewPermission,
            BudgetApplyPermission,
            BudgetReviewPermission,
            BudgetAccountManagePermission);
        if (access.Error is not null) return access.Error;
        if (!access.CanManageAll && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("budget_view_forbidden", "当前用户没有经费查看权限。"));
        }

        var query = AccountQuery().AsNoTracking();
        if (!access.CanManageAll)
        {
            query = query.Where(account => access.ClubIds.Contains(account.ClubId));
        }

        if (clubId is not null)
        {
            if (!access.CanManageAll && !access.ClubIds.Contains(clubId.Value))
            {
                return StatusCode(403, Error("budget_club_forbidden", "当前用户没有该社团的经费查看权限。"));
            }

            query = query.Where(account => account.ClubId == clubId.Value);
        }

        var normalizedFiscalYear = NormalizeText(fiscalYear);
        if (!string.IsNullOrWhiteSpace(normalizedFiscalYear))
        {
            query = query.Where(account => account.FiscalYear == normalizedFiscalYear);
        }

        var accounts = await query
            .OrderByDescending(account => account.FiscalYear)
            .ThenBy(account => account.ClubId)
            .ThenBy(account => account.AccountId)
            .ToListAsync();

        return Ok(await ToAccountDtosAsync(accounts));
    }

    [HttpPost("accounts")]
    public async Task<IActionResult> CreateAccount([FromBody] ApiCreateBudgetAccountRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var validation = ValidateAccountInput(req.FiscalYear, req.AccountName, Convert.ToDecimal(req.InitialAmount));
        if (validation is not null) return validation;

        var permission = await RequireClubPermissionAsync(
            currentUserId.Value,
            BudgetAccountManagePermission,
            req.ClubId,
            "当前用户没有维护该社团经费账户的权限。");
        if (permission is not null) return permission;

        return await ExecuteSerializableWriteAsync(
            async () =>
            {
                var club = await _db.Clubs.FindAsync(req.ClubId);
                if (club is null) return NotFound(Error("club_not_found", "社团不存在。"));

                var fiscalYear = NormalizeText(req.FiscalYear);
                if (await _db.BudgetAccounts.AnyAsync(account =>
                    account.ClubId == req.ClubId &&
                    account.FiscalYear == fiscalYear))
                {
                    return Conflict(Error("budget_account_exists", "该社团当前年度已存在经费账户。"));
                }

                var now = DateTime.UtcNow;
                var account = new BudgetAccount
                {
                    ClubId = req.ClubId,
                    FiscalYear = fiscalYear,
                    AccountName = NormalizeText(req.AccountName),
                    InitialAmount = Convert.ToDecimal(req.InitialAmount),
                    AccountStatus = AccountStatusActive,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Club = club
                };

                _db.BudgetAccounts.Add(account);
                await _db.SaveChangesAsync();

                return CreatedAtAction(nameof(GetAccounts), new { clubId = account.ClubId }, (await ToAccountDtosAsync([account])).Single());
            },
            "budget_account_write_conflict",
            "经费账户正在被其他操作修改，请稍后重试。");
    }

    [HttpPut("accounts/{accountId:int}")]
    public async Task<IActionResult> UpdateAccount(int accountId, [FromBody] ApiUpdateBudgetAccountRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var status = EnumMemberValue(req.Status);
        if (!AllowedAccountStatuses.Contains(status))
        {
            return BadRequest(Error("budget_account_status_invalid", "经费账户状态不合法。"));
        }

        var validation = ValidateAccountUpdateInput(req.AccountName, Convert.ToDecimal(req.InitialAmount));
        if (validation is not null) return validation;

        return await ExecuteSerializableWriteAsync(
            async () =>
            {
                var account = await LockBudgetAccountAsync(accountId);
                if (account is null) return NotFound(Error("budget_account_not_found", "经费账户不存在。"));
                await _db.Entry(account).Reference(item => item.Club).LoadAsync();

                var permission = await RequireClubPermissionAsync(
                    currentUserId.Value,
                    BudgetAccountManagePermission,
                    account.ClubId,
                    "当前用户没有维护该社团经费账户的权限。");
                if (permission is not null) return permission;

                var initialAmount = Convert.ToDecimal(req.InitialAmount);
                var committedAmount = await GetCommittedAmountAsync(account.AccountId);
                if (initialAmount < committedAmount)
                {
                    return BadRequest(Error(
                        "budget_account_amount_too_low",
                        "年度额度不能低于已审批占用金额。"));
                }

                account.AccountName = NormalizeText(req.AccountName);
                account.InitialAmount = initialAmount;
                account.AccountStatus = status;
                account.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();
                return Ok((await ToAccountDtosAsync([account])).Single());
            },
            "budget_account_write_conflict",
            "经费账户正在被其他操作修改，请稍后重试。");
    }

    [HttpGet("applications")]
    public async Task<IActionResult> GetApplications(
        [FromQuery] int? clubId,
        [FromQuery] int? accountId,
        [FromQuery] string? status,
        [FromQuery] string? type)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        if (!TryNormalizeFilter(status, AllowedApplicationStatuses, out var normalizedStatus))
        {
            return BadRequest(Error("budget_application_status_invalid", "经费申请状态参数不合法。"));
        }

        if (!TryNormalizeFilter(type, AllowedApplicationTypes, out var normalizedType))
        {
            return BadRequest(Error("budget_application_type_invalid", "经费申请类型参数不合法。"));
        }

        var access = await GetBudgetAccessAsync(
            currentUserId.Value,
            BudgetViewPermission,
            BudgetApplyPermission,
            BudgetReviewPermission,
            BudgetAccountManagePermission);
        if (access.Error is not null) return access.Error;
        if (!access.CanManageAll && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("budget_view_forbidden", "当前用户没有经费查看权限。"));
        }

        var query = ApplicationQuery().AsNoTracking();
        if (!access.CanManageAll)
        {
            query = query.Where(application => access.ClubIds.Contains(application.ClubId));
        }

        if (clubId is not null)
        {
            if (!access.CanManageAll && !access.ClubIds.Contains(clubId.Value))
            {
                return StatusCode(403, Error("budget_club_forbidden", "当前用户没有该社团的经费查看权限。"));
            }

            query = query.Where(application => application.ClubId == clubId.Value);
        }

        if (accountId is not null) query = query.Where(application => application.AccountId == accountId.Value);
        if (normalizedStatus is not null) query = query.Where(application => application.ApplicationStatus == normalizedStatus);
        if (normalizedType is not null) query = query.Where(application => application.ApplicationType == normalizedType);

        var applications = await query
            .OrderByDescending(application => application.SubmittedAt)
            .ThenByDescending(application => application.ApplicationId)
            .ToListAsync();

        return Ok(applications.Select(ToDto));
    }

    [HttpPost("applications")]
    public async Task<IActionResult> CreateApplication([FromBody] ApiCreateBudgetApplicationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var applicationType = EnumMemberValue(req.Type);
        if (!AllowedApplicationTypes.Contains(applicationType))
        {
            return BadRequest(Error("budget_application_type_invalid", "经费申请类型不合法。"));
        }

        var validation = ValidateApplicationInput(
            NormalizeText(req.Title),
            Convert.ToDecimal(req.Amount),
            NormalizeText(req.Purpose),
            NullIfBlank(req.Detail));
        if (validation is not null) return validation;

        return await ExecuteSerializableWriteAsync(
            async () =>
            {
                var account = await AccountQuery().FirstOrDefaultAsync(item => item.AccountId == req.AccountId);
                if (account is null) return NotFound(Error("budget_account_not_found", "经费账户不存在。"));

                var permission = await RequireClubPermissionAsync(
                    currentUserId.Value,
                    BudgetApplyPermission,
                    account.ClubId,
                    "当前用户没有该社团的经费申请权限。");
                if (permission is not null) return permission;

                if (!string.Equals(account.AccountStatus, AccountStatusActive, StringComparison.OrdinalIgnoreCase))
                {
                    return Conflict(Error("budget_account_closed", "经费账户已关闭，不能提交新的经费申请。"));
                }

                Activity? activity = null;
                if (req.ActivityId is not null)
                {
                    activity = await _db.Activities
                        .AsNoTracking()
                        .FirstOrDefaultAsync(item => item.ActivityId == req.ActivityId.Value);
                    if (activity is null) return NotFound(Error("activity_not_found", "关联活动不存在。"));
                    if (activity.ClubId != account.ClubId)
                    {
                        return BadRequest(Error("budget_activity_club_mismatch", "关联活动必须属于经费账户对应社团。"));
                    }
                }

                var amount = Convert.ToDecimal(req.Amount);
                var remainingAmount = await GetRemainingAmountAsync(account.AccountId, account.InitialAmount);
                if (amount > remainingAmount)
                {
                    return Conflict(Error("budget_insufficient_balance", "经费账户余额不足，不能提交该金额的申请。"));
                }

                var now = DateTime.UtcNow;
                var application = new BudgetApplication
                {
                    AccountId = account.AccountId,
                    ClubId = account.ClubId,
                    ActivityId = req.ActivityId,
                    ApplicantUserId = currentUserId.Value,
                    ApplicationType = applicationType,
                    Title = NormalizeText(req.Title),
                    Amount = amount,
                    Purpose = NormalizeText(req.Purpose),
                    Detail = NullIfBlank(req.Detail),
                    ApplicationStatus = ApplicationStatusPending,
                    SubmittedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _db.BudgetApplications.Add(application);
                await _db.SaveChangesAsync();

                var created = await ApplicationQuery()
                    .AsNoTracking()
                    .FirstAsync(item => item.ApplicationId == application.ApplicationId);
                return CreatedAtAction(nameof(GetApplications), new { clubId = created.ClubId }, ToDto(created));
            },
            "budget_application_write_conflict",
            "经费申请正在被其他操作修改，请稍后重试。");
    }

    [HttpPost("applications/{applicationId:int}/review")]
    public async Task<IActionResult> ReviewApplication(int applicationId, [FromBody] ApiReviewBudgetApplicationRequest req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var comment = NullIfBlank(req.Comment);
        if (comment?.Length > MaxCommentLength)
        {
            return BadRequest(Error("budget_review_comment_too_long", $"审批意见不能超过 {MaxCommentLength} 个字符。"));
        }

        // Oracle SERIALIZABLE keeps a transaction snapshot even after waiting on FOR UPDATE.
        // Budget approvals use READ COMMITTED so the balance SUM sees prior committed approvals.
        return await ExecuteWriteAsync(
            BudgetApprovalIsolationLevel,
            async () =>
            {
                var application = await LockBudgetApplicationAsync(applicationId);
                if (application is null) return NotFound(Error("budget_application_not_found", "经费申请不存在。"));

                var permission = await RequireClubPermissionAsync(
                    currentUserId.Value,
                    BudgetReviewPermission,
                    application.ClubId,
                    "当前用户没有该社团的经费审核权限。");
                if (permission is not null) return permission;

                if (!string.Equals(application.ApplicationStatus, ApplicationStatusPending, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(Error("budget_application_status_invalid", "只有待审核的经费申请才能审核。"));
                }

                var now = DateTime.UtcNow;
                application.ReviewerUserId = currentUserId.Value;
                application.ReviewComment = comment;
                application.ReviewedAt = now;
                application.UpdatedAt = now;
                application.ApplicationStatus = req.Approved ? ApplicationStatusApproved : ApplicationStatusRejected;

                _db.BudgetReviewRecords.Add(new BudgetReviewRecord
                {
                    ApplicationId = application.ApplicationId,
                    ReviewerUserId = currentUserId.Value,
                    Approved = req.Approved ? 1 : 0,
                    CommentText = comment,
                    ReviewedAt = now
                });

                if (req.Approved)
                {
                    var account = await LockBudgetAccountAsync(application.ClubId, application.AccountId);
                    if (account is null)
                    {
                        return NotFound(Error("budget_account_not_found", "经费账户不存在。"));
                    }

                    if (!string.Equals(account.AccountStatus, AccountStatusActive, StringComparison.OrdinalIgnoreCase))
                    {
                        return Conflict(Error("budget_account_closed", "经费账户已关闭，不能审批通过新的经费申请。"));
                    }

                    var remainingAmount = await GetRemainingAmountAsync(account.AccountId, account.InitialAmount);
                    if (application.Amount > remainingAmount)
                    {
                        return Conflict(Error("budget_insufficient_balance", "经费账户余额不足，不能审批通过该申请。"));
                    }

                    _db.BudgetTransactions.Add(new BudgetTransaction
                    {
                        AccountId = account.AccountId,
                        ApplicationId = application.ApplicationId,
                        ClubId = application.ClubId,
                        TransactionType = TransactionTypeCommitment,
                        Amount = -application.Amount,
                        Description = $"经费申请通过：{application.Title}",
                        OccurredAt = now,
                        CreatedAt = now
                    });
                }

                await _db.SaveChangesAsync();

                var reviewed = await ApplicationQuery()
                    .AsNoTracking()
                    .FirstAsync(item => item.ApplicationId == application.ApplicationId);
                return Ok(ToDto(reviewed));
            },
            "budget_review_write_conflict",
            "经费申请正在被其他操作审核，请刷新后重试。");
    }

    [HttpPost("applications/{applicationId:int}/cancel")]
    public async Task<IActionResult> CancelApplication(
        int applicationId,
        [FromBody] ApiCancelBudgetApplicationRequest? req)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var comment = NullIfBlank(req?.Comment);
        if (comment?.Length > MaxCommentLength)
        {
            return BadRequest(Error("budget_cancel_comment_too_long", $"撤销说明不能超过 {MaxCommentLength} 个字符。"));
        }

        return await ExecuteSerializableWriteAsync(
            async () =>
            {
                var application = await ApplicationQuery()
                    .FirstOrDefaultAsync(item => item.ApplicationId == applicationId);
                if (application is null) return NotFound(Error("budget_application_not_found", "经费申请不存在。"));

                if (!string.Equals(application.ApplicationStatus, ApplicationStatusPending, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(Error("budget_application_status_invalid", "只有待审核的经费申请才能撤销。"));
                }

                if (application.ApplicantUserId != currentUserId.Value)
                {
                    var permission = await RequireClubPermissionAsync(
                        currentUserId.Value,
                        BudgetApplyPermission,
                        application.ClubId,
                        "当前用户没有撤销该社团经费申请的权限。");
                    if (permission is not null) return permission;
                }

                application.ApplicationStatus = ApplicationStatusCancelled;
                application.ReviewComment = comment;
                application.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                var cancelled = await ApplicationQuery()
                    .AsNoTracking()
                    .FirstAsync(item => item.ApplicationId == application.ApplicationId);
                return Ok(ToDto(cancelled));
            },
            "budget_cancel_write_conflict",
            "经费申请正在被其他操作修改，请刷新后重试。");
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions([FromQuery] int? clubId, [FromQuery] int? accountId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var access = await GetBudgetAccessAsync(
            currentUserId.Value,
            BudgetViewPermission,
            BudgetApplyPermission,
            BudgetReviewPermission,
            BudgetAccountManagePermission);
        if (access.Error is not null) return access.Error;
        if (!access.CanManageAll && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("budget_view_forbidden", "当前用户没有经费查看权限。"));
        }

        var query = TransactionQuery().AsNoTracking();
        if (!access.CanManageAll)
        {
            query = query.Where(transaction => access.ClubIds.Contains(transaction.ClubId));
        }

        if (clubId is not null)
        {
            if (!access.CanManageAll && !access.ClubIds.Contains(clubId.Value))
            {
                return StatusCode(403, Error("budget_club_forbidden", "当前用户没有该社团的经费查看权限。"));
            }

            query = query.Where(transaction => transaction.ClubId == clubId.Value);
        }

        if (accountId is not null) query = query.Where(transaction => transaction.AccountId == accountId.Value);

        var transactions = await query
            .OrderByDescending(transaction => transaction.OccurredAt)
            .ThenByDescending(transaction => transaction.TransactionId)
            .ToListAsync();

        return Ok(transactions.Select(ToDto));
    }

    private IQueryable<BudgetAccount> AccountQuery() => _db.BudgetAccounts.Include(account => account.Club);

    private IQueryable<BudgetApplication> ApplicationQuery() => _db.BudgetApplications
        .Include(application => application.Account)
        .Include(application => application.Club)
        .Include(application => application.Activity)
        .Include(application => application.ApplicantUser)
        .Include(application => application.ReviewerUser);

    private IQueryable<BudgetTransaction> TransactionQuery() => _db.BudgetTransactions
        .Include(transaction => transaction.Club)
        .Include(transaction => transaction.Application);

    /// <summary>
    /// Review decisions must serialize on the application row before status is checked.
    /// </summary>
    private Task<BudgetApplication?> LockBudgetApplicationAsync(int applicationId) =>
        _db.BudgetApplications
            .FromSqlInterpolated($"""
                SELECT *
                FROM BUDGET_APPLICATIONS
                WHERE APPLICATION_ID = {applicationId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync();

    /// <summary>
    /// 审核通过和调整账户额度都会改变同一账户的可用余额判断，必须先锁账户行再汇总流水。
    /// </summary>
    private Task<BudgetAccount?> LockBudgetAccountAsync(int accountId) =>
        _db.BudgetAccounts
            .FromSqlInterpolated($"""
                SELECT *
                FROM BUDGET_ACCOUNTS
                WHERE ACCOUNT_ID = {accountId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync();

    /// <summary>
    /// 以社团和账户双键加锁，避免跨社团数据污染时误锁到不属于申请的账户。
    /// </summary>
    private Task<BudgetAccount?> LockBudgetAccountAsync(int clubId, int accountId) =>
        _db.BudgetAccounts
            .FromSqlInterpolated($"""
                SELECT *
                FROM BUDGET_ACCOUNTS
                WHERE CLUB_ID = {clubId}
                  AND ACCOUNT_ID = {accountId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync();

    private async Task<IActionResult?> RequireClubPermissionAsync(
        int userId,
        string permissionCode,
        int clubId,
        string forbiddenMessage)
    {
        var permission = await _authService.CheckPermissionAsync(userId, permissionCode, clubId);
        if (!permission.Succeeded)
        {
            return StatusCode(permission.StatusCode, Error(
                "budget_permission_check_failed",
                permission.ErrorMessage ?? "经费权限校验失败。"));
        }

        if (permission.Value?.Allowed != true)
        {
            return StatusCode(403, Error("budget_permission_forbidden", forbiddenMessage));
        }

        return null;
    }

    private async Task<(IActionResult? Error, bool CanManageAll, IReadOnlyList<int> ClubIds)> GetBudgetAccessAsync(
        int userId,
        params string[] permissionCodes)
    {
        var canManageAll = false;
        var clubIds = new HashSet<int>();

        foreach (var permissionCode in permissionCodes)
        {
            var globalPermission = await _authService.CheckPermissionAsync(userId, permissionCode, null);
            if (!globalPermission.Succeeded)
            {
                return (
                    StatusCode(globalPermission.StatusCode, Error(
                        "budget_permission_check_failed",
                        globalPermission.ErrorMessage ?? "经费权限校验失败。")),
                    false,
                    []);
            }

            canManageAll = canManageAll || globalPermission.Value?.Allowed == true;

            var clubPermission = await _authService.GetPermissionClubIdsAsync(userId, permissionCode);
            if (!clubPermission.Succeeded)
            {
                return (
                    StatusCode(clubPermission.StatusCode, Error(
                        "budget_permission_check_failed",
                        clubPermission.ErrorMessage ?? "经费权限校验失败。")),
                    false,
                    []);
            }

            foreach (var clubId in clubPermission.Value ?? [])
            {
                clubIds.Add(clubId);
            }
        }

        return (null, canManageAll, clubIds.OrderBy(id => id).ToList());
    }

    private async Task<List<ApiBudgetAccount>> ToAccountDtosAsync(IReadOnlyCollection<BudgetAccount> accounts)
    {
        if (accounts.Count == 0) return [];

        var accountIds = accounts.Select(account => account.AccountId).ToList();
        var transactionSums = await _db.BudgetTransactions
            .AsNoTracking()
            .Where(transaction => accountIds.Contains(transaction.AccountId))
            .GroupBy(transaction => transaction.AccountId)
            .Select(group => new
            {
                AccountId = group.Key,
                Amount = group.Sum(transaction => transaction.Amount)
            })
            .ToDictionaryAsync(item => item.AccountId, item => item.Amount);

        return accounts.Select(account =>
        {
            var transactionAmount = transactionSums.GetValueOrDefault(account.AccountId);
            var remainingAmount = account.InitialAmount + transactionAmount;
            return new ApiBudgetAccount
            {
                Id = account.AccountId,
                ClubId = account.ClubId,
                ClubName = account.Club?.ClubName ?? "",
                FiscalYear = account.FiscalYear,
                AccountName = account.AccountName,
                InitialAmount = Convert.ToDouble(account.InitialAmount),
                CommittedAmount = Convert.ToDouble(Math.Max(0, -transactionAmount)),
                RemainingAmount = Convert.ToDouble(remainingAmount),
                Status = EnumFromMember<ApiBudgetAccount.StatusEnum>(NormalizeAccountStatus(account.AccountStatus)),
                CreatedAt = StoredTimeToUtc(account.CreatedAt),
                UpdatedAt = StoredTimeToUtc(account.UpdatedAt)
            };
        }).ToList();
    }

    private static ApiBudgetApplication ToDto(BudgetApplication application) => new()
    {
        Id = application.ApplicationId,
        AccountId = application.AccountId,
        ClubId = application.ClubId,
        ClubName = application.Club?.ClubName ?? "",
        ActivityId = application.ActivityId,
        ActivityTitle = application.Activity?.Title,
        ApplicantUserId = application.ApplicantUserId,
        ApplicantName = DisplayName(application.ApplicantUser),
        Type = EnumFromMember<ApiBudgetApplication.TypeEnum>(NormalizeApplicationType(application.ApplicationType)),
        Title = application.Title,
        Amount = Convert.ToDouble(application.Amount),
        Purpose = application.Purpose,
        Detail = application.Detail,
        Status = EnumFromMember<ApiBudgetApplication.StatusEnum>(NormalizeApplicationStatus(application.ApplicationStatus)),
        SubmittedAt = StoredTimeToUtc(application.SubmittedAt),
        ReviewedAt = application.ReviewedAt is null ? null : StoredTimeToUtc(application.ReviewedAt.Value),
        ReviewerUserId = application.ReviewerUserId,
        ReviewerName = DisplayName(application.ReviewerUser),
        ReviewComment = application.ReviewComment
    };

    private static ApiBudgetTransaction ToDto(BudgetTransaction transaction) => new()
    {
        Id = transaction.TransactionId,
        AccountId = transaction.AccountId,
        ApplicationId = transaction.ApplicationId,
        ClubId = transaction.ClubId,
        ClubName = transaction.Club?.ClubName ?? "",
        Type = EnumFromMember<ApiBudgetTransaction.TypeEnum>(NormalizeTransactionType(transaction.TransactionType)),
        Amount = Convert.ToDouble(transaction.Amount),
        Description = transaction.Description,
        OccurredAt = StoredTimeToUtc(transaction.OccurredAt),
        CreatedAt = StoredTimeToUtc(transaction.CreatedAt)
    };

    private async Task<decimal> GetRemainingAmountAsync(int accountId, decimal initialAmount)
    {
        var transactionAmount = await _db.BudgetTransactions
            .Where(transaction => transaction.AccountId == accountId)
            .SumAsync(transaction => (decimal?)transaction.Amount) ?? 0;
        return initialAmount + transactionAmount;
    }

    private async Task<decimal> GetCommittedAmountAsync(int accountId)
    {
        var transactionAmount = await _db.BudgetTransactions
            .Where(transaction => transaction.AccountId == accountId)
            .SumAsync(transaction => (decimal?)transaction.Amount) ?? 0;
        return Math.Max(0, -transactionAmount);
    }

    private async Task<IActionResult> ExecuteSerializableWriteAsync(
        Func<Task<IActionResult>> action,
        string conflictCode,
        string conflictMessage) =>
        await ExecuteWriteAsync(IsolationLevel.Serializable, action, conflictCode, conflictMessage);

    private async Task<IActionResult> ExecuteWriteAsync(
        IsolationLevel isolationLevel,
        Func<Task<IActionResult>> action,
        string conflictCode,
        string conflictMessage)
    {
        for (var attempt = 1; attempt <= MaxWriteRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(isolationLevel);
            try
            {
                var result = await action();
                if (IsSuccessResult(result))
                {
                    await transaction.CommitAsync();
                }
                else
                {
                    await transaction.RollbackAsync();
                }

                _db.ChangeTracker.Clear();
                return result;
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException &&
                ProjectMembershipService.IsRetryableWriteConflict(ex))
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();

                if (attempt == MaxWriteRetries)
                {
                    return Conflict(Error(conflictCode, conflictMessage));
                }
            }
        }

        return Conflict(Error(conflictCode, conflictMessage));
    }

    private static IActionResult? ValidateAccountInput(string fiscalYear, string accountName, decimal initialAmount)
    {
        if (string.IsNullOrWhiteSpace(NormalizeText(fiscalYear)))
        {
            return new BadRequestObjectResult(Error("budget_fiscal_year_required", "经费年度不能为空。"));
        }

        if (NormalizeText(fiscalYear).Length > 20)
        {
            return new BadRequestObjectResult(Error("budget_fiscal_year_too_long", "经费年度不能超过 20 个字符。"));
        }

        if (string.IsNullOrWhiteSpace(NormalizeText(accountName)))
        {
            return new BadRequestObjectResult(Error("budget_account_name_required", "经费账户名称不能为空。"));
        }

        if (NormalizeText(accountName).Length > MaxTitleLength)
        {
            return new BadRequestObjectResult(Error("budget_account_name_too_long", $"经费账户名称不能超过 {MaxTitleLength} 个字符。"));
        }

        if (initialAmount < 0)
        {
            return new BadRequestObjectResult(Error("budget_account_amount_invalid", "年度额度不能小于 0。"));
        }

        return null;
    }

    private static IActionResult? ValidateAccountUpdateInput(string accountName, decimal initialAmount)
    {
        if (string.IsNullOrWhiteSpace(NormalizeText(accountName)))
        {
            return new BadRequestObjectResult(Error("budget_account_name_required", "经费账户名称不能为空。"));
        }

        if (NormalizeText(accountName).Length > MaxTitleLength)
        {
            return new BadRequestObjectResult(Error("budget_account_name_too_long", $"经费账户名称不能超过 {MaxTitleLength} 个字符。"));
        }

        if (initialAmount < 0)
        {
            return new BadRequestObjectResult(Error("budget_account_amount_invalid", "年度额度不能小于 0。"));
        }

        return null;
    }

    private static IActionResult? ValidateApplicationInput(
        string title,
        decimal amount,
        string purpose,
        string? detail)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new BadRequestObjectResult(Error("budget_application_title_required", "经费申请标题不能为空。"));
        }

        if (title.Length > MaxTitleLength)
        {
            return new BadRequestObjectResult(Error("budget_application_title_too_long", $"经费申请标题不能超过 {MaxTitleLength} 个字符。"));
        }

        if (amount < 0.01m)
        {
            return new BadRequestObjectResult(Error("budget_application_amount_invalid", "经费申请金额必须大于 0。"));
        }

        if (string.IsNullOrWhiteSpace(purpose))
        {
            return new BadRequestObjectResult(Error("budget_application_purpose_required", "经费用途不能为空。"));
        }

        if (purpose.Length > MaxPurposeLength)
        {
            return new BadRequestObjectResult(Error("budget_application_purpose_too_long", $"经费用途不能超过 {MaxPurposeLength} 个字符。"));
        }

        if (detail?.Length > MaxDetailLength)
        {
            return new BadRequestObjectResult(Error("budget_application_detail_too_long", $"经费明细不能超过 {MaxDetailLength} 个字符。"));
        }

        return null;
    }

    private static bool TryNormalizeFilter(string? value, HashSet<string> allowed, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        return normalized is null || allowed.Contains(normalized);
    }

    private static bool IsSuccessResult(IActionResult result)
    {
        var statusCode = result switch
        {
            ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => StatusCodes.Status200OK
        };

        return statusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices;
    }

    private static string NormalizeAccountStatus(string? status) =>
        AllowedAccountStatuses.Contains(NormalizeStatus(status)) ? NormalizeStatus(status) : AccountStatusActive;

    private static string NormalizeApplicationType(string? type) =>
        AllowedApplicationTypes.Contains(NormalizeStatus(type)) ? NormalizeStatus(type) : "purchase";

    private static string NormalizeApplicationStatus(string? status) =>
        AllowedApplicationStatuses.Contains(NormalizeStatus(status)) ? NormalizeStatus(status) : ApplicationStatusPending;

    private static string NormalizeTransactionType(string? type) =>
        AllowedTransactionTypes.Contains(NormalizeStatus(type)) ? NormalizeStatus(type) : "adjustment";

    private static string NormalizeStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();

    private static string NormalizeText(string? value) => (value ?? string.Empty).Trim();

    private static string? NullIfBlank(string? value)
    {
        var normalized = NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string DisplayName(User? user)
    {
        if (user is null) return "";
        return string.IsNullOrWhiteSpace(user.RealName) ? user.Username : user.RealName;
    }

    private static DateTime StoredTimeToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static string EnumMemberValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        return typeof(TEnum)
            .GetMember(name)
            .FirstOrDefault()?
            .GetCustomAttribute<EnumMemberAttribute>()?
            .Value ?? name;
    }

    private static TEnum EnumFromMember<TEnum>(string value)
        where TEnum : struct, Enum
    {
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var memberValue = field.GetCustomAttribute<EnumMemberAttribute>()?.Value;
            if (string.Equals(memberValue, value, StringComparison.OrdinalIgnoreCase))
            {
                return (TEnum)field.GetValue(null)!;
            }
        }

        return Enum.Parse<TEnum>(value, ignoreCase: true);
    }

    private static ApiError Error(string code, string message, string? detail = null) => new()
    {
        Code = code,
        Message = message,
        Detail = detail
    };
}
