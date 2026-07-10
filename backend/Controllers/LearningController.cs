using System.Data;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiLearningItem = Org.OpenAPITools.Models.LearningItem;
using ApiLearningRecord = Org.OpenAPITools.Models.LearningRecord;
using ApiLearningTeacherCandidate = Org.OpenAPITools.Models.LearningTeacherCandidate;
using CreateLearningItemRequest = Org.OpenAPITools.Models.CreateLearningItemRequest;
using DbLearningItem = ClubHub.Api.Data.Entities.LearningItem;
using DbLearningRecord = ClubHub.Api.Data.Entities.LearningRecord;
using EnrollLearningItemRequest = Org.OpenAPITools.Models.EnrollLearningItemRequest;
using UpdateLearningItemRequest = Org.OpenAPITools.Models.UpdateLearningItemRequest;
using UpdateLearningProgressRequest = Org.OpenAPITools.Models.UpdateLearningProgressRequest;

namespace ClubHub.Api.Controllers;

/// <summary>
/// 培训课程发布、报名和学习记录接口。
/// </summary>
[ApiController]
[Route("api/learning")]
public class LearningController : ControllerBase
{
    private const int MaxCreateRetries = 3;

    private static readonly HashSet<string> AdvisorRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "advisor",
        "club_advisor",
        "teacher_advisor"
    };

    private readonly ClubHubDbContext _db;
    private readonly ILogger<LearningController> _logger;

    public LearningController(
        ClubHubDbContext db,
        ILogger<LearningController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 返回课程表单可选择的授课教师，界面展示姓名和学工号，内部用户编号仅随选项提交。
    /// </summary>
    [HttpGet("teacher-candidates")]
    public async Task<IActionResult> GetTeacherCandidates(
        [FromQuery] int currentUserId,
        [FromQuery] int clubId)
    {
        if (currentUserId <= 0) return BadRequest("当前登录用户无效。");
        if (clubId <= 0) return BadRequest("请选择发布课程的社团。");

        var operatorUser = await LoadUserAsync(currentUserId);
        if (operatorUser is null) return NotFound("当前登录用户不存在。");
        if (!UsersController.IsActive(operatorUser.AccountStatus))
        {
            return BadRequest("当前登录账号已停用。");
        }

        var club = await _db.Clubs
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ClubId == clubId);
        if (club is null) return NotFound("发布课程的社团不存在。");
        if (!UsersController.IsActive(club.ClubStatus))
        {
            return BadRequest("只有正常运营的社团可以维护课程。");
        }

        var ownsClubCourse = await _db.LearningItems
            .AsNoTracking()
            .AnyAsync(item => item.ClubId == clubId && item.UploaderUserId == currentUserId);
        if (!CanCreateLearningItem(operatorUser, club) && !ownsClubCourse)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只有本社团课程发布者、负责人、干部或指导老师可以选择授课教师。");
        }

        var users = await _db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Where(user =>
                user.AccountStatus == null ||
                user.AccountStatus == string.Empty ||
                user.AccountStatus.ToLower() == "active" ||
                user.AccountStatus.ToLower() == "normal" ||
                user.AccountStatus.ToLower() == "enabled")
            .OrderBy(user => user.RealName)
            .ThenBy(user => user.StudentNo)
            .ThenBy(user => user.UserId)
            .ToListAsync();

        var candidates = users
            .Where(IsTeacherAccount)
            .Select(user => new ApiLearningTeacherCandidate
            {
                Id = user.UserId,
                RealName = user.RealName,
                StudentNo = user.StudentNo,
                DisplayName = BuildTeacherCandidateDisplayName(user)
            })
            .ToList();

        return Ok(candidates);
    }

    /// <summary>
    /// 按登录用户的角色、社团成员关系和课程开放范围返回课程。
    /// </summary>
    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] int currentUserId, [FromQuery] int? clubId)
    {
        if (currentUserId <= 0) return BadRequest("当前用户 ID 必须大于 0。");
        if (clubId is not null and <= 0) return BadRequest("社团 ID 必须大于 0。");

        var viewer = await LoadUserAsync(currentUserId);
        if (viewer is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(viewer.AccountStatus)) return BadRequest("当前用户账号已停用。");

        var query = _db.LearningItems
            .AsNoTracking()
            .Include(item => item.Club)
            .AsQueryable();
        if (clubId is not null) query = query.Where(item => item.ClubId == clubId.Value);

        var items = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.ItemId)
            .ToListAsync();
        if (items.Count == 0) return Ok(Array.Empty<ApiLearningItem>());

        var itemIds = items.Select(item => item.ItemId).ToArray();
        var activeEnrollmentCounts = await _db.LearningRecords
            .AsNoTracking()
            .Where(record =>
                itemIds.Contains(record.ItemId) &&
                (record.EnrollStatus == null ||
                 record.EnrollStatus != LearningWorkflow.RecordStatusCancelled))
            .GroupBy(record => record.ItemId)
            .Select(group => new { ItemId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.ItemId, entry => entry.Count);

        var currentRecords = await _db.LearningRecords
            .AsNoTracking()
            .Where(record => itemIds.Contains(record.ItemId) && record.UserId == currentUserId)
            .OrderByDescending(record => record.EnrolledAt)
            .ThenByDescending(record => record.RecordId)
            .ToListAsync();
        var currentRecordByItemId = currentRecords
            .GroupBy(record => record.ItemId)
            .ToDictionary(group => group.Key, group => group.First());

        var now = LearningWorkflow.BusinessNow();
        var result = new List<ApiLearningItem>();
        foreach (var item in items)
        {
            currentRecordByItemId.TryGetValue(item.ItemId, out var currentRecord);
            var canManage = CanManageLearningItem(viewer, item);
            if (!CanViewLearningItem(viewer, item, currentRecord, canManage)) continue;

            var activeEnrollmentCount = activeEnrollmentCounts.GetValueOrDefault(item.ItemId);
            var enrollmentDecision = GetEnrollmentDecision(
                viewer,
                item,
                currentRecord,
                activeEnrollmentCount,
                canManage,
                now);

            var itemDto = TryMapItemDto(
                item,
                activeEnrollmentCount,
                currentRecord,
                canManage,
                enrollmentDecision,
                now);
            if (itemDto is null) return CourseDataIntegrityProblem(item.ItemId);

            result.Add(itemDto);
        }

        return Ok(result);
    }

    /// <summary>
    /// 创建草稿或直接发布培训课程。
    /// </summary>
    [HttpPost("items")]
    public async Task<IActionResult> CreateItem([FromBody] CreateLearningItemRequest? request)
    {
        if (request is null) return BadRequest("课程信息不能为空。");
        if (request.CurrentUserId <= 0) return BadRequest("当前用户 ID 必须大于 0。");
        if (request.ClubId <= 0) return BadRequest("社团 ID 必须大于 0。");

        var operatorUser = await LoadUserAsync(request.CurrentUserId);
        if (operatorUser is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(operatorUser.AccountStatus)) return BadRequest("当前用户账号已停用。");

        var club = await _db.Clubs.FirstOrDefaultAsync(candidate => candidate.ClubId == request.ClubId);
        if (club is null) return NotFound("发布课程的社团不存在。");
        if (!UsersController.IsActive(club.ClubStatus)) return BadRequest("只有正常运营的社团可以发布课程。");
        if (!CanCreateLearningItem(operatorUser, club))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只有本社团负责人、干部或指导老师可以发布课程。");
        }

        var validation = await ValidateCourseInputAsync(
            request.Title,
            request.TeacherUserId,
            request.ItemType,
            request.CategoryName,
            request.EnrollmentDeadline,
            request.StartAt,
            request.EndAt,
            request.Capacity,
            ToVisibilityValue(request.Visibility));
        if (validation is not null) return validation;

        var itemStatus = ToCreateStatusValue(request.ItemStatus);
        if (itemStatus is null) return BadRequest("课程初始状态只能是草稿或报名中。");

        for (var attempt = 1; attempt <= MaxCreateRetries; attempt++)
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var item = new DbLearningItem
            {
                ItemId = await GetNextLearningItemId(),
                ClubId = request.ClubId,
                UploaderUserId = request.CurrentUserId,
                TeacherUserId = request.TeacherUserId,
                Title = request.Title.Trim(),
                ItemType = Normalize(request.ItemType),
                CategoryName = NormalizeOptionalText(request.CategoryName),
                Description = NormalizeOptionalText(request.Description),
                EnrollmentDeadline = LearningWorkflow.AsUtc(request.EnrollmentDeadline),
                StartAt = LearningWorkflow.AsUtc(request.StartAt),
                EndAt = LearningWorkflow.AsUtc(request.EndAt),
                Capacity = request.Capacity,
                Visibility = ToVisibilityValue(request.Visibility),
                DownloadPermission = "none",
                ItemStatus = itemStatus,
                CreatedAt = LearningWorkflow.BusinessNow()
            };

            _db.LearningItems.Add(item);
            try
            {
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var now = LearningWorkflow.BusinessNow();
                var decision = GetEnrollmentDecision(
                    operatorUser,
                    item,
                    null,
                    0,
                    canManage: true,
                    now);
                var itemDto = TryMapItemDto(item, 0, null, true, decision, now);
                if (itemDto is null) return CourseDataIntegrityProblem(item.ItemId);

                return CreatedAtAction(
                    nameof(GetItems),
                    new { currentUserId = request.CurrentUserId },
                    itemDto);
            }
            catch (DbUpdateException) when (attempt < MaxCreateRetries)
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
            }
        }

        return Conflict("课程编号生成冲突，请重试。");
    }

    /// <summary>
    /// 修改课程信息、开放范围和报名状态。
    /// </summary>
    [HttpPut("items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(
        int itemId,
        [FromBody] UpdateLearningItemRequest? request)
    {
        if (request is null) return BadRequest("课程信息不能为空。");
        if (itemId <= 0) return BadRequest("课程 ID 必须大于 0。");
        if (request.CurrentUserId <= 0) return BadRequest("当前用户 ID 必须大于 0。");

        var item = await _db.LearningItems
            .Include(candidate => candidate.Club)
            .FirstOrDefaultAsync(candidate => candidate.ItemId == itemId);
        if (item is null) return NotFound("课程不存在。");

        var operatorUser = await LoadUserAsync(request.CurrentUserId);
        if (operatorUser is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(operatorUser.AccountStatus)) return BadRequest("当前用户账号已停用。");
        if (!CanManageLearningItem(operatorUser, item))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只有课程发布者或本社团负责人、干部、指导老师可以修改课程。");
        }

        var validation = await ValidateCourseInputAsync(
            request.Title,
            request.TeacherUserId,
            request.ItemType,
            request.CategoryName,
            request.EnrollmentDeadline,
            request.StartAt,
            request.EndAt,
            request.Capacity,
            ToVisibilityValue(request.Visibility));
        if (validation is not null) return validation;

        var itemStatus = ToUpdateStatusValue(request.ItemStatus);
        if (itemStatus is null) return BadRequest("课程状态只能是草稿、报名中或已关闭。");

        var activeEnrollmentCount = await CountActiveEnrollmentsAsync(itemId);
        if (request.Capacity < activeEnrollmentCount)
        {
            return Conflict($"课程容量不能小于当前有效报名人数 {activeEnrollmentCount}。");
        }

        item.TeacherUserId = request.TeacherUserId;
        item.Title = request.Title.Trim();
        item.ItemType = Normalize(request.ItemType);
        item.CategoryName = NormalizeOptionalText(request.CategoryName);
        item.Description = NormalizeOptionalText(request.Description);
        item.EnrollmentDeadline = LearningWorkflow.AsUtc(request.EnrollmentDeadline);
        item.StartAt = LearningWorkflow.AsUtc(request.StartAt);
        item.EndAt = LearningWorkflow.AsUtc(request.EndAt);
        item.Capacity = request.Capacity;
        item.Visibility = ToVisibilityValue(request.Visibility);
        item.ItemStatus = itemStatus;

        await _db.SaveChangesAsync();

        var currentRecord = await GetLatestLearningRecordAsync(itemId, request.CurrentUserId);
        var now = LearningWorkflow.BusinessNow();
        var decision = GetEnrollmentDecision(
            operatorUser,
            item,
            currentRecord,
            activeEnrollmentCount,
            canManage: true,
            now);
        var itemDto = TryMapItemDto(
            item,
            activeEnrollmentCount,
            currentRecord,
            true,
            decision,
            now);
        return itemDto is null
            ? CourseDataIntegrityProblem(item.ItemId)
            : Ok(itemDto);
    }

    /// <summary>
    /// 报名课程；取消后再次报名会恢复原学习记录。
    /// </summary>
    [HttpPost("items/{itemId:int}/enrollments")]
    public async Task<IActionResult> Enroll(
        int itemId,
        [FromBody] EnrollLearningItemRequest? request)
    {
        if (request is null) return BadRequest("报名用户信息不能为空。");
        if (itemId <= 0) return BadRequest("课程 ID 必须大于 0。");
        if (request.CurrentUserId <= 0) return BadRequest("当前用户 ID 必须大于 0。");

        for (var attempt = 1; attempt <= MaxCreateRetries; attempt++)
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var item = await _db.LearningItems
                .Include(candidate => candidate.Club)
                .FirstOrDefaultAsync(candidate => candidate.ItemId == itemId);
            if (item is null) return NotFound("课程不存在。");

            var user = await LoadUserAsync(request.CurrentUserId);
            if (user is null) return NotFound("当前用户不存在。");
            if (!UsersController.IsActive(user.AccountStatus)) return BadRequest("当前用户账号已停用。");

            var existingRecord = await GetLatestLearningRecordAsync(itemId, request.CurrentUserId);
            var activeEnrollmentCount = await CountActiveEnrollmentsAsync(itemId);
            var canManage = CanManageLearningItem(user, item);
            var decision = GetEnrollmentDecision(
                user,
                item,
                existingRecord,
                activeEnrollmentCount,
                canManage,
                LearningWorkflow.BusinessNow());
            if (decision is not null)
            {
                return StatusCode(decision.StatusCode, decision.Message);
            }

            var now = LearningWorkflow.BusinessNow();
            var record = existingRecord;
            if (record is null)
            {
                record = new DbLearningRecord
                {
                    RecordId = await GetNextLearningRecordId(),
                    ItemId = itemId,
                    UserId = request.CurrentUserId
                };
                _db.LearningRecords.Add(record);
            }

            record.EnrollStatus = LearningWorkflow.RecordStatusEnrolled;
            record.EnrolledAt = now;
            record.Progress = 0;
            record.DurationSeconds = 0;
            record.LastLearnAt = null;
            record.CompletedAt = null;
            record.User = user;

            try
            {
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return CreatedAtAction(
                    nameof(GetRecords),
                    new { currentUserId = request.CurrentUserId, itemId },
                    ToRecordDto(record));
            }
            catch (DbUpdateException) when (attempt < MaxCreateRetries)
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
            }
        }

        return Conflict("课程报名发生并发冲突，请重试。");
    }

    /// <summary>
    /// 在课程开始前取消当前用户的报名。
    /// </summary>
    [HttpDelete("items/{itemId:int}/enrollments")]
    public async Task<IActionResult> CancelEnrollment(
        int itemId,
        [FromBody] EnrollLearningItemRequest? request)
    {
        if (request is null) return BadRequest("报名用户信息不能为空。");
        if (itemId <= 0) return BadRequest("课程 ID 必须大于 0。");
        if (request.CurrentUserId <= 0) return BadRequest("当前用户 ID 必须大于 0。");

        var item = await _db.LearningItems.FirstOrDefaultAsync(candidate => candidate.ItemId == itemId);
        if (item is null) return NotFound("课程不存在。");
        if (item.StartAt is null)
        {
            _logger.LogError("课程 {ItemId} 缺少开始时间，无法判断取消报名窗口。", item.ItemId);
            return CourseDataIntegrityProblem(item.ItemId);
        }
        if (LearningWorkflow.BusinessNow() >= item.StartAt.Value)
        {
            return BadRequest("课程已经开始，不能取消报名。");
        }

        var record = await _db.LearningRecords
            .Include(candidate => candidate.User)
            .Where(candidate =>
                candidate.ItemId == itemId &&
                candidate.UserId == request.CurrentUserId)
            .OrderByDescending(candidate => candidate.EnrolledAt)
            .ThenByDescending(candidate => candidate.RecordId)
            .FirstOrDefaultAsync();
        if (record is null) return NotFound("当前用户没有该课程的报名记录。");

        var status = LearningWorkflow.NormalizeRecordStatus(record.EnrollStatus);
        if (status == LearningWorkflow.RecordStatusCancelled)
        {
            return BadRequest("该课程报名已经取消。");
        }
        if (status == LearningWorkflow.RecordStatusCompleted)
        {
            return BadRequest("已完成的课程记录不能取消。");
        }

        record.EnrollStatus = LearningWorkflow.RecordStatusCancelled;
        await _db.SaveChangesAsync();
        return Ok(ToRecordDto(record));
    }

    /// <summary>
    /// 用户查看自己的学习记录，课程管理者可查看课程报名名单。
    /// </summary>
    [HttpGet("records")]
    public async Task<IActionResult> GetRecords(
        [FromQuery] int currentUserId,
        [FromQuery] int? itemId)
    {
        if (currentUserId <= 0) return BadRequest("当前用户 ID 必须大于 0。");
        if (itemId is not null and <= 0) return BadRequest("课程 ID 必须大于 0。");

        var user = await LoadUserAsync(currentUserId);
        if (user is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(user.AccountStatus)) return BadRequest("当前用户账号已停用。");

        var query = _db.LearningRecords
            .AsNoTracking()
            .Include(record => record.User)
            .AsQueryable();

        if (itemId is null)
        {
            query = query.Where(record => record.UserId == currentUserId);
        }
        else
        {
            var item = await _db.LearningItems
                .AsNoTracking()
                .Include(candidate => candidate.Club)
                .FirstOrDefaultAsync(candidate => candidate.ItemId == itemId.Value);
            if (item is null) return NotFound("课程不存在。");

            query = CanManageLearningItem(user, item)
                ? query.Where(record => record.ItemId == itemId.Value)
                : query.Where(record =>
                    record.ItemId == itemId.Value &&
                    record.UserId == currentUserId);
        }

        var records = await query
            .OrderByDescending(record => record.EnrolledAt)
            .ThenBy(record => record.RecordId)
            .ToListAsync();

        return Ok(records.Select(ToRecordDto));
    }

    /// <summary>
    /// 更新当前用户自己的学习进度。
    /// </summary>
    [HttpPut("records/{recordId:int}/progress")]
    public async Task<IActionResult> UpdateProgress(
        int recordId,
        [FromBody] UpdateLearningProgressRequest? request)
    {
        if (request is null) return BadRequest("学习进度信息不能为空。");
        if (recordId <= 0) return BadRequest("学习记录 ID 必须大于 0。");
        if (request.CurrentUserId <= 0) return BadRequest("当前用户 ID 必须大于 0。");
        if (!LearningWorkflow.IsProgressValid((decimal)request.Progress))
        {
            return BadRequest("学习进度必须在 0 到 100 之间。");
        }
        if (request.DurationSeconds is < 0) return BadRequest("学习时长不能为负数。");

        var record = await _db.LearningRecords
            .Include(candidate => candidate.Item)
            .Include(candidate => candidate.User)
            .FirstOrDefaultAsync(candidate => candidate.RecordId == recordId);
        if (record is null) return NotFound("学习记录不存在。");
        if (record.UserId != request.CurrentUserId)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只能更新自己的学习记录。");
        }

        var recordStatus = LearningWorkflow.NormalizeRecordStatus(record.EnrollStatus);
        if (recordStatus == LearningWorkflow.RecordStatusCancelled)
        {
            return BadRequest("已取消的学习记录不能更新。");
        }
        if (recordStatus == LearningWorkflow.RecordStatusCompleted)
        {
            return Conflict("已完成的学习记录不能再次修改。");
        }
        if (record.Item?.StartAt is null ||
            LearningWorkflow.BusinessNow() < record.Item.StartAt.Value)
        {
            return BadRequest("课程尚未开始，不能更新学习进度。");
        }

        var progress = (decimal)request.Progress;
        var now = LearningWorkflow.BusinessNow();
        record.Progress = progress;
        record.DurationSeconds = Math.Max(
            record.DurationSeconds ?? 0,
            request.DurationSeconds ?? 0);
        record.LastLearnAt = now;
        record.EnrollStatus = LearningWorkflow.ResolveRecordStatus(progress);
        record.CompletedAt = progress >= 100 ? record.CompletedAt ?? now : null;

        await _db.SaveChangesAsync();
        return Ok(ToRecordDto(record));
    }

    /// <summary>
    /// 校验课程必填信息、时间、容量、范围及授课教师资格。
    /// </summary>
    private async Task<IActionResult?> ValidateCourseInputAsync(
        string? title,
        int teacherUserId,
        string? itemType,
        string? categoryName,
        DateTime enrollmentDeadline,
        DateTime startAt,
        DateTime endAt,
        int capacity,
        string? visibility)
    {
        if (string.IsNullOrWhiteSpace(title)) return BadRequest("课程名称不能为空。");
        if (title.Trim().Length > 100) return BadRequest("课程名称不能超过 100 个字符。");
        if (!LearningWorkflow.IsSupportedCourseType(itemType))
        {
            return BadRequest("课程类型只能是课程、讲座或培训。");
        }
        if (NormalizeOptionalText(categoryName)?.Length > 100)
        {
            return BadRequest("课程分类不能超过 100 个字符。");
        }
        if (!LearningWorkflow.IsCourseTimeValid(enrollmentDeadline, startAt, endAt))
        {
            return BadRequest(
                "报名截止时间、课程开始时间和结束时间必填，且必须满足报名截止时间不晚于开始时间、结束时间晚于开始时间。");
        }
        if (!LearningWorkflow.IsCapacityValid(capacity))
        {
            return BadRequest("课程容量必填且必须大于 0。");
        }
        if (!LearningWorkflow.IsVisibilityValid(visibility))
        {
            return BadRequest("课程开放范围只能是本社团或全校。");
        }

        var teacher = await LoadUserAsync(teacherUserId);
        if (teacher is null ||
            !UsersController.IsActive(teacher.AccountStatus) ||
            !IsTeacherAccount(teacher))
        {
            return BadRequest("授课人必须是正常状态的教师或指导老师账号。");
        }

        return null;
    }

    /// <summary>
    /// 返回当前用户不能报名课程的原因；可报名时返回空。
    /// </summary>
    private static EnrollmentDecision? GetEnrollmentDecision(
        User user,
        DbLearningItem item,
        DbLearningRecord? currentRecord,
        int activeEnrollmentCount,
        bool canManage,
        DateTime now)
    {
        if (!LearningWorkflow.IsPublished(item))
        {
            return new EnrollmentDecision(StatusCodes.Status400BadRequest, "课程当前未开放报名。");
        }
        if (item.EndAt.HasValue && now >= item.EndAt.Value)
        {
            return new EnrollmentDecision(StatusCodes.Status400BadRequest, "课程已经结束。");
        }
        if (!LearningWorkflow.IsEnrollmentWindowOpen(item, now))
        {
            return new EnrollmentDecision(StatusCodes.Status400BadRequest, "课程报名时间已截止。");
        }

        var recordStatus = currentRecord is null
            ? null
            : LearningWorkflow.NormalizeRecordStatus(currentRecord.EnrollStatus);
        if (recordStatus is not null && recordStatus != LearningWorkflow.RecordStatusCancelled)
        {
            return new EnrollmentDecision(StatusCodes.Status409Conflict, "当前用户已经报名该课程。");
        }

        var visibility = LearningWorkflow.NormalizeVisibility(item.Visibility);
        if (visibility == LearningWorkflow.VisibilityClub &&
            !canManage &&
            !IsActiveClubMember(user, item.ClubId))
        {
            return new EnrollmentDecision(
                StatusCodes.Status403Forbidden,
                "该课程仅面向本社团有效成员开放。");
        }
        if (!LearningWorkflow.HasEnrollmentCapacity(item, activeEnrollmentCount))
        {
            return new EnrollmentDecision(StatusCodes.Status409Conflict, "课程名额已满。");
        }

        return null;
    }

    /// <summary>
    /// 判断用户是否可按管理权、历史报名或开放范围查看课程。
    /// </summary>
    private static bool CanViewLearningItem(
        User viewer,
        DbLearningItem item,
        DbLearningRecord? currentRecord,
        bool canManage)
    {
        if (canManage) return true;

        var itemStatus = LearningWorkflow.NormalizeItemStatus(item.ItemStatus);
        if (itemStatus == LearningWorkflow.ItemStatusDraft) return false;

        var recordStatus = currentRecord is null
            ? null
            : LearningWorkflow.NormalizeRecordStatus(currentRecord.EnrollStatus);
        if (recordStatus is not null && recordStatus != LearningWorkflow.RecordStatusCancelled)
        {
            return true;
        }

        return LearningWorkflow.NormalizeVisibility(item.Visibility) switch
        {
            LearningWorkflow.VisibilityPublic => true,
            LearningWorkflow.VisibilityClub => IsActiveClubMember(viewer, item.ClubId),
            _ => false
        };
    }

    /// <summary>
    /// 判断用户是否可为指定社团发布课程。
    /// </summary>
    private static bool CanCreateLearningItem(User user, Club club)
    {
        return UsersController.IsClubPrincipal(user, club.ClubId) ||
               UsersController.IsClubOfficer(user, club.ClubId) ||
               HasClubRole(user, club.ClubId, AdvisorRoleCodes) ||
               IsNamedClubAdvisor(user, club);
    }

    /// <summary>
    /// 判断用户是否可维护课程及查看报名名单。
    /// </summary>
    private static bool CanManageLearningItem(User user, DbLearningItem item)
    {
        if (item.UploaderUserId == user.UserId) return true;

        return UsersController.IsClubPrincipal(user, item.ClubId) ||
               UsersController.IsClubOfficer(user, item.ClubId) ||
               HasClubRole(user, item.ClubId, AdvisorRoleCodes) ||
               (item.Club is not null && IsNamedClubAdvisor(user, item.Club));
    }

    /// <summary>
    /// 判断教师账号是否与社团登记的指导老师姓名匹配。
    /// </summary>
    private static bool IsNamedClubAdvisor(User user, Club club)
    {
        return IsTeacherAccount(user) &&
               AdvisorNameMatchesUser(club.AdvisorName, user);
    }

    /// <summary>
    /// 判断用户是否为指定社团的有效成员。
    /// </summary>
    private static bool IsActiveClubMember(User user, int clubId)
    {
        return user.ClubMemberships.Any(membership =>
            membership.ClubId == clubId &&
            UsersController.IsActive(membership.MemberStatus));
    }

    /// <summary>
    /// 加载权限判断所需的用户角色和社团成员关系。
    /// </summary>
    private Task<User?> LoadUserAsync(int userId)
    {
        return _db.Users
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .Include(user => user.ClubMemberships)
            .FirstOrDefaultAsync(user => user.UserId == userId);
    }

    /// <summary>
    /// 统计课程当前占用名额的学习记录数量。
    /// </summary>
    private Task<int> CountActiveEnrollmentsAsync(int itemId)
    {
        return _db.LearningRecords.CountAsync(record =>
            record.ItemId == itemId &&
            (record.EnrollStatus == null ||
             record.EnrollStatus != LearningWorkflow.RecordStatusCancelled));
    }

    /// <summary>
    /// 获取用户在指定课程下最近的一条学习记录。
    /// </summary>
    private Task<DbLearningRecord?> GetLatestLearningRecordAsync(int itemId, int userId)
    {
        return _db.LearningRecords
            .Include(record => record.User)
            .Where(record => record.ItemId == itemId && record.UserId == userId)
            .OrderByDescending(record => record.EnrolledAt)
            .ThenByDescending(record => record.RecordId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 在可串行化事务中计算下一个课程编号。
    /// </summary>
    private async Task<int> GetNextLearningItemId()
    {
        // 当前表尚未配置序列，使用可串行化事务和重试保护 max(id) + 1。
        var maxId = await _db.LearningItems.MaxAsync(item => (int?)item.ItemId) ?? 0;
        return maxId + 1;
    }

    /// <summary>
    /// 在可串行化事务中计算下一个学习记录编号。
    /// </summary>
    private async Task<int> GetNextLearningRecordId()
    {
        // 与 LEARNING_ITEMS 保持一致，后续数据库统一引入序列时再替换。
        var maxId = await _db.LearningRecords.MaxAsync(record => (int?)record.RecordId) ?? 0;
        return maxId + 1;
    }

    /// <summary>
    /// 判断用户是否在指定社团拥有目标角色之一。
    /// </summary>
    private static bool HasClubRole(
        User user,
        int clubId,
        IReadOnlySet<string> roleCodes)
    {
        return user.UserRoles.Any(userRole =>
            userRole.ClubId == clubId &&
            userRole.Role is not null &&
            roleCodes.Contains(Normalize(userRole.Role.RoleCode)));
    }

    /// <summary>
    /// 根据工号或角色判断账号是否属于教师。
    /// </summary>
    private static bool IsTeacherAccount(User user)
    {
        return IsStaffNumber(user.StudentNo) ||
               user.UserRoles.Any(userRole =>
                   userRole.Role is not null &&
                   IsTeacherRole(userRole.Role));
    }

    /// <summary>
    /// 判断角色定义是否表示教师或指导老师。
    /// </summary>
    private static bool IsTeacherRole(Role role)
    {
        var code = Normalize(role.RoleCode);
        return code is "teacher" or "advisor" or "club_advisor" or "teacher_advisor" ||
               (role.RoleName ?? string.Empty).Contains("教师", StringComparison.Ordinal) ||
               (role.RoleName ?? string.Empty).Contains("老师", StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断学工号是否符合当前五位教师工号约定。
    /// </summary>
    private static bool IsStaffNumber(string? userNumber)
    {
        return !string.IsNullOrWhiteSpace(userNumber) &&
               userNumber.Trim().Length == 5 &&
               userNumber.Trim().All(char.IsDigit);
    }

    /// <summary>
    /// 判断社团登记的指导老师文本是否包含用户真实姓名。
    /// </summary>
    private static bool AdvisorNameMatchesUser(string? advisorName, User user)
    {
        return !string.IsNullOrWhiteSpace(advisorName) &&
               !string.IsNullOrWhiteSpace(user.RealName) &&
               advisorName.Trim().Contains(user.RealName.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 将创建请求枚举映射为数据库课程状态。
    /// </summary>
    private static string? ToCreateStatusValue(CreateLearningItemRequest.ItemStatusEnum status)
    {
        return status switch
        {
            CreateLearningItemRequest.ItemStatusEnum.DraftEnum =>
                LearningWorkflow.ItemStatusDraft,
            CreateLearningItemRequest.ItemStatusEnum.PublishedEnum =>
                LearningWorkflow.ItemStatusPublished,
            _ => null
        };
    }

    /// <summary>
    /// 将更新请求枚举映射为数据库课程状态。
    /// </summary>
    private static string? ToUpdateStatusValue(UpdateLearningItemRequest.ItemStatusEnum status)
    {
        return status switch
        {
            UpdateLearningItemRequest.ItemStatusEnum.DraftEnum =>
                LearningWorkflow.ItemStatusDraft,
            UpdateLearningItemRequest.ItemStatusEnum.PublishedEnum =>
                LearningWorkflow.ItemStatusPublished,
            UpdateLearningItemRequest.ItemStatusEnum.ClosedEnum =>
                LearningWorkflow.ItemStatusClosed,
            _ => null
        };
    }

    /// <summary>
    /// 将完整课程实体映射为 API 模型；数据缺失时记录错误并返回空。
    /// </summary>
    private ApiLearningItem? TryMapItemDto(
        DbLearningItem item,
        int activeEnrollmentCount,
        DbLearningRecord? currentRecord,
        bool canManage,
        EnrollmentDecision? enrollmentDecision,
        DateTime now)
    {
        if (item.StartAt is null || item.EndAt is null || item.Capacity is null)
        {
            _logger.LogError(
                "课程 {ItemId} 数据不完整：StartAtMissing={StartAtMissing}, EndAtMissing={EndAtMissing}, CapacityMissing={CapacityMissing}。",
                item.ItemId,
                item.StartAt is null,
                item.EndAt is null,
                item.Capacity is null);
            return null;
        }

        var currentRecordStatus = currentRecord is null
            ? LearningWorkflow.RecordStatusNone
            : LearningWorkflow.NormalizeRecordStatus(currentRecord.EnrollStatus);
        var canCancelEnrollment =
            (currentRecordStatus is LearningWorkflow.RecordStatusEnrolled or
                LearningWorkflow.RecordStatusLearning) &&
            now < item.StartAt.Value;

        return new ApiLearningItem
        {
            Id = item.ItemId,
            ClubId = item.ClubId,
            UploaderUserId = item.UploaderUserId,
            TeacherUserId = item.TeacherUserId,
            Title = item.Title,
            ItemType = item.ItemType,
            CategoryName = item.CategoryName,
            Description = item.Description,
            EnrollmentDeadline = LearningWorkflow.AsUtc(item.EnrollmentDeadline),
            StartAt = LearningWorkflow.AsUtc(item.StartAt.Value),
            EndAt = LearningWorkflow.AsUtc(item.EndAt.Value),
            Capacity = item.Capacity.Value,
            Visibility = ToItemVisibilityEnum(item.Visibility),
            ItemStatus = ToItemStatusEnum(
                LearningWorkflow.ResolveEffectiveItemStatus(item, now)),
            CurrentEnrollments = activeEnrollmentCount,
            CurrentUserRecordStatus = ToCurrentUserRecordStatusEnum(currentRecordStatus),
            CanManage = canManage,
            CanEnroll = enrollmentDecision is null,
            CanCancelEnrollment = canCancelEnrollment,
            EnrollmentUnavailableReason = enrollmentDecision?.Message
        };
    }

    /// <summary>
    /// 将学习记录实体映射为 API 模型，并统一输出 UTC 时间。
    /// </summary>
    private static ApiLearningRecord ToRecordDto(DbLearningRecord record)
    {
        return new ApiLearningRecord
        {
            Id = record.RecordId,
            ItemId = record.ItemId,
            UserId = record.UserId,
            UserDisplayName = BuildUserDisplayName(record.User, record.UserId),
            UserNumber = record.User?.StudentNo,
            EnrollStatus = ToRecordStatusEnum(record.EnrollStatus),
            EnrolledAt = LearningWorkflow.AsUtc(record.EnrolledAt),
            Progress = record.Progress is null ? null : (double)record.Progress.Value,
            DurationSeconds = record.DurationSeconds,
            LastLearnAt = LearningWorkflow.AsUtc(record.LastLearnAt),
            CompletedAt = LearningWorkflow.AsUtc(record.CompletedAt)
        };
    }

    /// <summary>
    /// 返回不暴露数据库细节的课程数据完整性错误。
    /// </summary>
    private ObjectResult CourseDataIntegrityProblem(int itemId)
    {
        return Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "课程数据异常",
            detail: $"课程 {itemId} 缺少必填信息，请联系管理员检查数据。");
    }

    /// <summary>
    /// 将创建请求的开放范围枚举转换为数据库值。
    /// </summary>
    private static string ToVisibilityValue(CreateLearningItemRequest.VisibilityEnum visibility)
    {
        return visibility switch
        {
            CreateLearningItemRequest.VisibilityEnum.PublicEnum =>
                LearningWorkflow.VisibilityPublic,
            CreateLearningItemRequest.VisibilityEnum.ClubEnum =>
                LearningWorkflow.VisibilityClub,
            _ => string.Empty
        };
    }

    /// <summary>
    /// 将更新请求的开放范围枚举转换为数据库值。
    /// </summary>
    private static string ToVisibilityValue(UpdateLearningItemRequest.VisibilityEnum visibility)
    {
        return visibility switch
        {
            UpdateLearningItemRequest.VisibilityEnum.PublicEnum =>
                LearningWorkflow.VisibilityPublic,
            UpdateLearningItemRequest.VisibilityEnum.ClubEnum =>
                LearningWorkflow.VisibilityClub,
            _ => string.Empty
        };
    }

    /// <summary>
    /// 将数据库开放范围转换为课程响应枚举。
    /// </summary>
    private static ApiLearningItem.VisibilityEnum ToItemVisibilityEnum(string? visibility)
    {
        return LearningWorkflow.NormalizeVisibility(visibility) ==
               LearningWorkflow.VisibilityPublic
            ? ApiLearningItem.VisibilityEnum.PublicEnum
            : ApiLearningItem.VisibilityEnum.ClubEnum;
    }

    /// <summary>
    /// 将当前用户的学习状态转换为课程响应枚举。
    /// </summary>
    private static ApiLearningItem.CurrentUserRecordStatusEnum ToCurrentUserRecordStatusEnum(
        string? status)
    {
        return LearningWorkflow.NormalizeRecordStatus(status) switch
        {
            LearningWorkflow.RecordStatusEnrolled =>
                ApiLearningItem.CurrentUserRecordStatusEnum.EnrolledEnum,
            LearningWorkflow.RecordStatusLearning =>
                ApiLearningItem.CurrentUserRecordStatusEnum.LearningEnum,
            LearningWorkflow.RecordStatusCompleted =>
                ApiLearningItem.CurrentUserRecordStatusEnum.CompletedEnum,
            LearningWorkflow.RecordStatusCancelled =>
                ApiLearningItem.CurrentUserRecordStatusEnum.CancelledEnum,
            _ => ApiLearningItem.CurrentUserRecordStatusEnum.NoneEnum
        };
    }

    /// <summary>
    /// 将数据库学习状态转换为学习记录响应枚举。
    /// </summary>
    private static ApiLearningRecord.EnrollStatusEnum ToRecordStatusEnum(string? status)
    {
        return LearningWorkflow.NormalizeRecordStatus(status) switch
        {
            LearningWorkflow.RecordStatusLearning =>
                ApiLearningRecord.EnrollStatusEnum.LearningEnum,
            LearningWorkflow.RecordStatusCompleted =>
                ApiLearningRecord.EnrollStatusEnum.CompletedEnum,
            LearningWorkflow.RecordStatusCancelled =>
                ApiLearningRecord.EnrollStatusEnum.CancelledEnum,
            _ => ApiLearningRecord.EnrollStatusEnum.EnrolledEnum
        };
    }

    /// <summary>
    /// 将数据库课程状态映射为 OpenAPI 枚举。
    /// </summary>
    private static ApiLearningItem.ItemStatusEnum ToItemStatusEnum(string status)
    {
        return status switch
        {
            LearningWorkflow.ItemStatusPublished =>
                ApiLearningItem.ItemStatusEnum.PublishedEnum,
            LearningWorkflow.ItemStatusClosed =>
                ApiLearningItem.ItemStatusEnum.ClosedEnum,
            LearningWorkflow.ItemStatusFinished =>
                ApiLearningItem.ItemStatusEnum.FinishedEnum,
            _ => ApiLearningItem.ItemStatusEnum.DraftEnum
        };
    }

    /// <summary>
    /// 按真实姓名、用户名和编号顺序生成用户展示名。
    /// </summary>
    private static string BuildUserDisplayName(User? user, int userId)
    {
        if (!string.IsNullOrWhiteSpace(user?.RealName)) return user.RealName.Trim();
        if (!string.IsNullOrWhiteSpace(user?.Username)) return user.Username.Trim();
        return $"用户 {userId}";
    }

    /// <summary>
    /// 生成包含姓名和学工号的授课教师候选展示文本。
    /// </summary>
    private static string BuildTeacherCandidateDisplayName(User user)
    {
        var name = BuildUserDisplayName(user, user.UserId);
        var staffNumber = user.StudentNo?.Trim();
        return string.IsNullOrWhiteSpace(staffNumber) ? name : $"{name}（{staffNumber}）";
    }

    /// <summary>
    /// 清理可选文本并将空白值转换为空。
    /// </summary>
    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 将业务代码值清理为小写文本。
    /// </summary>
    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private sealed record EnrollmentDecision(int StatusCode, string Message);
}
