using System.Data;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using ApiLearningDownloadResult = Org.OpenAPITools.Models.LearningDownloadResult;
using ApiLearningItem = Org.OpenAPITools.Models.LearningItem;
using ApiLearningItemStatistics = Org.OpenAPITools.Models.LearningItemStatistics;
using ApiLearningRecord = Org.OpenAPITools.Models.LearningRecord;
using ApiLearningInstructorCandidate = Org.OpenAPITools.Models.LearningTeacherCandidate;
using CreateLearningItemRequest = Org.OpenAPITools.Models.CreateLearningItemRequest;
using DbLearningItem = ClubHub.Api.Data.Entities.LearningItem;
using DbLearningRecord = ClubHub.Api.Data.Entities.LearningRecord;
using UpdateLearningItemRequest = Org.OpenAPITools.Models.UpdateLearningItemRequest;
using UpdateLearningProgressRequest = Org.OpenAPITools.Models.UpdateLearningProgressRequest;

namespace ClubHub.Api.Controllers;

/// <summary>
/// 培训课程、学习资源、权限下载和学习统计接口。
/// </summary>
[ApiController]
[Authorize]
[Route("api/learning")]
public class LearningController : ControllerBase
{
    private const int MaxCreateRetries = 3;
    private const long MaxUploadBytes = 50L * 1024 * 1024;
    private const string LocalFileUrlPrefix = "/api/learning/items/";
    private const string OssFileUrlPrefix = "oss://";

    private static readonly HashSet<string> AdvisorRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "advisor",
        "club_advisor",
        "teacher_advisor"
    };

    private readonly ClubHubDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly LearningObjectStorage _objectStorage;
    private readonly ILogger<LearningController> _logger;

    public LearningController(
        ClubHubDbContext db,
        IWebHostEnvironment environment,
        LearningObjectStorage objectStorage,
        ILogger<LearningController> logger)
    {
        _db = db;
        _environment = environment;
        _objectStorage = objectStorage;
        _logger = logger;
    }

    /// <summary>
    /// 按学工号查询课程表单可使用的授课人，教师和学生均可选择，内部用户编号仅随选项提交。
    /// </summary>
    [HttpGet("instructor-lookup")]
    public async Task<IActionResult> GetInstructorByUserNumber(
        [FromQuery] int clubId,
        [FromQuery] string? userNumber)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");
        if (clubId <= 0) return BadRequest("请选择发布课程的社团。");
        var normalizedUserNumber = NormalizeOptionalText(userNumber);
        if (normalizedUserNumber is null) return BadRequest("请输入授课人的学号或工号。");
        if (normalizedUserNumber.Length > 30) return BadRequest("授课人的学号或工号不能超过 30 个字符。");

        var operatorUser = await LoadUserAsync(currentUserId.Value);
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
            .AnyAsync(item => item.ClubId == clubId && item.UploaderUserId == currentUserId.Value);
        if (!CanCreateLearningItem(operatorUser, club) && !ownsClubCourse)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只有本社团课程发布者、负责人、干部或指导老师可以查询授课人。");
        }

        var instructor = await _db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .FirstOrDefaultAsync(user => user.StudentNo == normalizedUserNumber);
        if (instructor is null ||
            !UsersController.IsActive(instructor.AccountStatus) ||
            (!IsTeacherAccount(instructor) && !IsStudentAccount(instructor)))
        {
            return NotFound("未找到正常状态的教师或学生账号，请核对学号或工号。");
        }

        return Ok(new ApiLearningInstructorCandidate
        {
            Id = instructor.UserId,
            RealName = instructor.RealName,
            StudentNo = instructor.StudentNo,
            DisplayName = BuildInstructorCandidateDisplayName(instructor)
        });
    }

    /// <summary>
    /// 按登录用户的角色、社团成员关系和课程开放范围返回课程。
    /// </summary>
    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] int? clubId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");
        if (clubId is not null and <= 0) return BadRequest("社团 ID 必须大于 0。");

        var viewer = await LoadUserAsync(currentUserId.Value);
        if (viewer is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(viewer.AccountStatus)) return BadRequest("当前用户账号已停用。");

        var query = _db.LearningItems
            .AsNoTracking()
            .Include(item => item.Club)
            .Include(item => item.Uploader)
                .ThenInclude(uploader => uploader!.ClubMemberships)
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
            .Where(record => itemIds.Contains(record.ItemId) && record.UserId == currentUserId.Value)
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
        if (request is null) return BadRequest("课程或资源信息不能为空。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");
        if (request.ClubId <= 0) return BadRequest("社团 ID 必须大于 0。");
        if (IsInternalFileReference(request.FileUrl))
        {
            return BadRequest("内部文件地址只能通过资源上传生成。");
        }

        var operatorUser = await LoadUserAsync(currentUserId.Value);
        if (operatorUser is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(operatorUser.AccountStatus)) return BadRequest("当前用户账号已停用。");

        var club = await _db.Clubs.FirstOrDefaultAsync(candidate => candidate.ClubId == request.ClubId);
        if (club is null) return NotFound("发布资源的社团不存在。");
        if (!UsersController.IsActive(club.ClubStatus)) return BadRequest("只有正常运营的社团可以发布资源。");
        if (!CanCreateLearningItem(operatorUser, club))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只有本社团负责人、干部或指导老师可以发布资源。");
        }

        var validation = await ValidateLearningItemInputAsync(
            request.Title,
            request.InstructorUserId,
            request.ItemType,
            request.CategoryName,
            request.Description,
            request.FileUrl,
            request.StartAt,
            request.EndAt,
            request.Capacity,
            ToVisibilityValue(request.Visibility),
            ToDownloadPermissionValue(request.DownloadPermission),
            operatorUser,
            request.ClubId);
        if (validation is not null) return validation;

        var itemStatus = ToCreateStatusValue(request.ItemStatus);
        if (itemStatus is null) return BadRequest("资源初始状态只能是草稿或已发布。");

        var isCourse = LearningWorkflow.IsSupportedCourseType(request.ItemType);

        for (var attempt = 1; attempt <= MaxCreateRetries; attempt++)
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var item = new DbLearningItem
            {
                ItemId = await GetNextLearningItemId(),
                ClubId = request.ClubId,
                UploaderUserId = currentUserId.Value,
                TeacherUserId = isCourse ? request.InstructorUserId : null,
                Title = request.Title.Trim(),
                ItemType = Normalize(request.ItemType),
                CategoryName = NormalizeOptionalText(request.CategoryName),
                Description = NormalizeOptionalText(request.Description),
                FileUrl = NormalizeOptionalText(request.FileUrl),
                StartAt = isCourse ? LearningWorkflow.AsUtc(request.StartAt) : null,
                EndAt = isCourse ? LearningWorkflow.AsUtc(request.EndAt) : null,
                Capacity = isCourse ? request.Capacity : null,
                Visibility = ToVisibilityValue(request.Visibility),
                DownloadPermission = ToDownloadPermissionValue(request.DownloadPermission),
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
                    null,
                    itemDto);
            }
            catch (DbUpdateException) when (attempt < MaxCreateRetries)
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
            }
        }

        return Conflict("资源编号生成冲突，请重试。");
    }

    /// <summary>
    /// 接收拖拽或文件选择器提交的单个文件，并直接创建对应的社团学习资源。
    /// </summary>
    [HttpPost("resources/upload")]
    [RequestSizeLimit(MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadResource(
        [FromForm] int clubId,
        [FromForm] IFormFile? file,
        [FromForm] string? title,
        [FromForm] string? categoryName,
        [FromForm] string? description,
        [FromForm] string? visibility,
        [FromForm] string? downloadPermission)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");
        if (clubId <= 0) return BadRequest("请选择文件所属社团。");
        if (file is null || file.Length <= 0) return BadRequest("请选择需要上传的文件。");
        if (file.Length > MaxUploadBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, "单个文件不能超过 50 MB。");
        }

        var originalFileName = Path.GetFileName(file.FileName).Trim();
        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Length > 255)
        {
            return BadRequest("文件名不能为空且不能超过 255 个字符。");
        }
        var normalizedTitle = NormalizeOptionalText(title) ?? originalFileName;
        if (normalizedTitle.Length > 100)
        {
            return BadRequest("资源标题不能超过 100 个字符；文件名较长时请单独填写标题。");
        }

        var normalizedCategory = NormalizeOptionalText(categoryName);
        if (normalizedCategory?.Length > 100) return BadRequest("资源分类不能超过 100 个字符。");
        var normalizedDescription = NormalizeOptionalText(description);
        if (normalizedDescription?.Length > 1000) return BadRequest("资源说明不能超过 1000 个字符。");
        var normalizedVisibility = LearningWorkflow.NormalizeVisibility(visibility);
        if (normalizedVisibility is null) return BadRequest("资源可见范围无效。");
        var normalizedDownloadPermission =
            LearningWorkflow.NormalizeDownloadPermission(downloadPermission);
        if (normalizedDownloadPermission is null) return BadRequest("资源下载设置无效。");

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return BadRequest("文件扩展名包含无效字符。");
        }
        if (IsBlockedUploadExtension(extension)) return BadRequest("不允许上传可执行文件或脚本文件。");

        var operatorUser = await LoadUserAsync(currentUserId.Value);
        if (operatorUser is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(operatorUser.AccountStatus)) return BadRequest("当前用户账号已停用。");
        var club = await _db.Clubs.FirstOrDefaultAsync(candidate => candidate.ClubId == clubId);
        if (club is null) return NotFound("文件所属社团不存在。");
        if (!UsersController.IsActive(club.ClubStatus)) return BadRequest("只有正常运营的社团可以上传资源。");
        if (!CanCreateLearningItem(operatorUser, club))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只有本社团负责人、干部或指导老师可以上传资源。");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var itemId = await GetNextLearningItemId();
        string? storageReference = null;

        try
        {
            await using var stream = file.OpenReadStream();
            storageReference = await _objectStorage.UploadAsync(
                clubId,
                itemId,
                extension,
                stream,
                file.Length,
                file.ContentType,
                originalFileName,
                HttpContext.RequestAborted);

            var item = new DbLearningItem
            {
                ItemId = itemId,
                ClubId = clubId,
                UploaderUserId = currentUserId.Value,
                Title = normalizedTitle,
                ItemType = InferResourceType(file.ContentType, extension),
                CategoryName = normalizedCategory,
                Description = normalizedDescription,
                FileUrl = storageReference,
                Visibility = normalizedVisibility,
                DownloadPermission = normalizedDownloadPermission,
                ItemStatus = LearningWorkflow.ItemStatusPublished,
                CreatedAt = LearningWorkflow.BusinessNow()
            };
            _db.LearningItems.Add(item);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            var now = LearningWorkflow.BusinessNow();
            var itemDto = TryMapItemDto(item, 0, null, true, null, now);
            return itemDto is null
                ? CourseDataIntegrityProblem(itemId)
                : CreatedAtAction(nameof(GetItems), null, itemDto);
        }
        catch (Exception exception) when (IsObjectStorageFailure(exception))
        {
            await transaction.RollbackAsync();
            await TryRemoveObjectAsync(storageReference, itemId);
            _logger.LogError(exception, "资源 {ItemId} 上传到 OSS 失败。", itemId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "资源存储暂不可用",
                detail: "无法连接 OSS 或 ECS RAM 角色凭据不可用，请稍后重试。");
        }
        catch
        {
            await transaction.RollbackAsync();
            await TryRemoveObjectAsync(storageReference, itemId);
            throw;
        }
    }

    /// <summary>
    /// 校验资源访问权限后返回历史本地文件，或从私有 OSS 读取对象并流式传输。
    /// </summary>
    [HttpGet("items/{itemId:int}/file")]
    public async Task<IActionResult> GetResourceFile(int itemId)
    {
        if (itemId <= 0) return BadRequest("资源 ID 必须大于 0。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");
        var item = await LoadLearningItemForAccessAsync(itemId);
        if (item is null)
        {
            return NotFound("上传文件不存在。");
        }
        var isLocalUpload = item.FileUrl == $"{LocalFileUrlPrefix}{itemId}/file";
        var isObjectStorageUpload = _objectStorage.IsStorageReference(item.FileUrl);
        if (!isLocalUpload && !isObjectStorageUpload) return NotFound("上传文件不存在。");

        var user = await LoadUserAsync(currentUserId.Value);
        if (user is null) return NotFound("当前用户不存在。");
        var record = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
        var canManage = CanManageLearningItem(user, item);
        var accessDecision = GetLearningAccessDecision(user, item, record, canManage);
        if (accessDecision is not null)
        {
            return StatusCode(accessDecision.StatusCode, accessDecision.Message);
        }
        var downloadDecision = GetDownloadDecision(item);
        if (downloadDecision is not null)
        {
            return StatusCode(downloadDecision.StatusCode, downloadDecision.Message);
        }

        if (isObjectStorageUpload)
        {
            try
            {
                var storedObject = await _objectStorage.OpenReadAsync(
                    item.FileUrl!,
                    HttpContext.RequestAborted);
                if (storedObject.ContentLength is > 0)
                {
                    Response.ContentLength = storedObject.ContentLength;
                }
                if (!string.IsNullOrWhiteSpace(storedObject.ContentDisposition) &&
                    storedObject.ContentDisposition.IndexOfAny(['\r', '\n']) < 0)
                {
                    Response.Headers.ContentDisposition = storedObject.ContentDisposition;
                    return File(
                        storedObject.Content,
                        storedObject.ContentType ?? "application/octet-stream");
                }
                return File(
                    storedObject.Content,
                    storedObject.ContentType ?? "application/octet-stream",
                    item.Title);
            }
            catch (Exception exception) when (IsObjectStorageFailure(exception))
            {
                _logger.LogError(exception, "资源 {ItemId} 无法从 OSS 读取。", itemId);
                return Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "资源存储暂不可用",
                    detail: "无法从 OSS 读取资源，请稍后重试。");
            }
        }

        var clubStoragePath = Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "learning-files",
            item.ClubId.ToString());
        var storedPath = Directory.Exists(clubStoragePath)
            ? Directory.EnumerateFiles(clubStoragePath, $"{itemId}.*").SingleOrDefault()
            : null;
        if (storedPath is null) return NotFound("上传文件不存在。");

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(storedPath, out var contentType))
        {
            contentType = "application/octet-stream";
        }
        return PhysicalFile(storedPath, contentType, item.Title, enableRangeProcessing: true);
    }

    /// <summary>
    /// 删除非课程资源，同时清理关联学习记录、OSS 对象或历史本地文件。
    /// </summary>
    [HttpDelete("items/{itemId:int}")]
    public async Task<IActionResult> DeleteResource(int itemId)
    {
        if (itemId <= 0) return BadRequest("资源 ID 必须大于 0。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");

        var item = await _db.LearningItems
            .Include(candidate => candidate.Club)
            .Include(candidate => candidate.Uploader)
                .ThenInclude(uploader => uploader!.ClubMemberships)
            .FirstOrDefaultAsync(candidate => candidate.ItemId == itemId);
        if (item is null) return NotFound("学习资源不存在。");
        if (!LearningWorkflow.IsSupportedResourceType(item.ItemType))
        {
            return BadRequest("课程不能通过资源删除功能删除。");
        }

        var operatorUser = await LoadUserAsync(currentUserId.Value);
        if (operatorUser is null) return NotFound("当前用户不存在。");
        if (!CanManageLearningItem(operatorUser, item))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只有资源上传者或本社团负责人、干部、指导老师可以删除资源。");
        }

        var isLocalUpload = item.FileUrl == $"{LocalFileUrlPrefix}{itemId}/file";
        var storageReference = _objectStorage.IsStorageReference(item.FileUrl)
            ? item.FileUrl
            : null;
        var clubId = item.ClubId;
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var records = await _db.LearningRecords
            .Where(record => record.ItemId == itemId)
            .ToListAsync();
        _db.LearningRecords.RemoveRange(records);
        _db.LearningItems.Remove(item);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        if (isLocalUpload)
        {
            var clubStoragePath = Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "learning-files",
                clubId.ToString());
            if (Directory.Exists(clubStoragePath))
            {
                foreach (var storedPath in Directory.EnumerateFiles(clubStoragePath, $"{itemId}.*"))
                {
                    try
                    {
                        System.IO.File.Delete(storedPath);
                    }
                    catch (IOException exception)
                    {
                        _logger.LogWarning(
                            exception,
                            "资源 {ItemId} 已从数据库删除，但物理文件 {StoredPath} 清理失败。",
                            itemId,
                            storedPath);
                    }
                }
            }
        }
        else if (storageReference is not null)
        {
            await TryRemoveObjectAsync(storageReference, itemId);
        }

        return NoContent();
    }

    /// <summary>
    /// 修改课程信息、开放范围和加入状态。
    /// </summary>
    [HttpPut("items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(
        int itemId,
        [FromBody] UpdateLearningItemRequest? request)
    {
        if (request is null) return BadRequest("课程或资源信息不能为空。");
        if (itemId <= 0) return BadRequest("资源 ID 必须大于 0。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");

        var item = await _db.LearningItems
            .Include(candidate => candidate.Club)
            .Include(candidate => candidate.Uploader)
                .ThenInclude(uploader => uploader!.ClubMemberships)
            .FirstOrDefaultAsync(candidate => candidate.ItemId == itemId);
        if (item is null) return NotFound("课程或资源不存在。");
        if (IsInternalFileReference(request.FileUrl) &&
            request.FileUrl?.Trim() != item.FileUrl?.Trim())
        {
            return BadRequest("不能将资源关联到其他上传文件。");
        }

        var operatorUser = await LoadUserAsync(currentUserId.Value);
        if (operatorUser is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(operatorUser.AccountStatus)) return BadRequest("当前用户账号已停用。");
        if (!CanManageLearningItem(operatorUser, item))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只有资源发布者或本社团负责人、干部、指导老师可以修改资源。");
        }

        var validation = await ValidateLearningItemInputAsync(
            request.Title,
            request.InstructorUserId,
            request.ItemType,
            request.CategoryName,
            request.Description,
            request.FileUrl,
            request.StartAt,
            request.EndAt,
            request.Capacity,
            ToVisibilityValue(request.Visibility),
            ToDownloadPermissionValue(request.DownloadPermission),
            item.Uploader ?? operatorUser,
            item.ClubId);
        if (validation is not null) return validation;

        var itemStatus = ToUpdateStatusValue(request.ItemStatus);
        if (itemStatus is null) return BadRequest("资源状态只能是草稿、已发布或已停止。");

        var isCourse = LearningWorkflow.IsSupportedCourseType(request.ItemType);
        var activeEnrollmentCount = await CountActiveEnrollmentsAsync(itemId);
        if (isCourse && request.Capacity < activeEnrollmentCount)
        {
            return Conflict($"课程容量不能小于当前已加入人数 {activeEnrollmentCount}。");
        }

        item.TeacherUserId = isCourse ? request.InstructorUserId : null;
        item.Title = request.Title.Trim();
        item.ItemType = Normalize(request.ItemType);
        item.CategoryName = NormalizeOptionalText(request.CategoryName);
        item.Description = NormalizeOptionalText(request.Description);
        item.FileUrl = NormalizeOptionalText(request.FileUrl);
        item.StartAt = isCourse ? LearningWorkflow.AsUtc(request.StartAt) : null;
        item.EndAt = isCourse ? LearningWorkflow.AsUtc(request.EndAt) : null;
        item.Capacity = isCourse ? request.Capacity : null;
        item.Visibility = ToVisibilityValue(request.Visibility);
        item.DownloadPermission = ToDownloadPermissionValue(request.DownloadPermission);
        item.ItemStatus = itemStatus;

        await _db.SaveChangesAsync();

        var currentRecord = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
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
    /// 加入课程；退出后再次加入会恢复原学习记录。
    /// </summary>
    [HttpPost("items/{itemId:int}/enrollments")]
    public async Task<IActionResult> Enroll(int itemId)
    {
        if (itemId <= 0) return BadRequest("课程 ID 必须大于 0。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");

        for (var attempt = 1; attempt <= MaxCreateRetries; attempt++)
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var item = await _db.LearningItems
                .Include(candidate => candidate.Club)
                .FirstOrDefaultAsync(candidate => candidate.ItemId == itemId);
            if (item is null) return NotFound("课程不存在。");

            var user = await LoadUserAsync(currentUserId.Value);
            if (user is null) return NotFound("当前用户不存在。");
            if (!UsersController.IsActive(user.AccountStatus)) return BadRequest("当前用户账号已停用。");

            var existingRecord = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
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
                    UserId = currentUserId.Value
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
                    new { itemId },
                    ToRecordDto(record));
            }
            catch (DbUpdateException) when (attempt < MaxCreateRetries)
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
            }
        }

        return Conflict("加入课程时发生并发冲突，请重试。");
    }

    /// <summary>
    /// 在课程结束前退出当前用户已加入的课程。
    /// </summary>
    [HttpDelete("items/{itemId:int}/enrollments")]
    public async Task<IActionResult> CancelEnrollment(int itemId)
    {
        if (itemId <= 0) return BadRequest("课程 ID 必须大于 0。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");

        var item = await _db.LearningItems.FirstOrDefaultAsync(candidate => candidate.ItemId == itemId);
        if (item is null) return NotFound("课程不存在。");
        if (!LearningWorkflow.IsSupportedCourseType(item.ItemType))
        {
            return BadRequest("学习资源无需退出课程，可直接保留学习记录。");
        }
        if (item.EndAt.HasValue &&
            LearningWorkflow.BusinessNow() >= item.EndAt.Value)
        {
            return BadRequest("课程已经结束，不能退出课程。");
        }

        var record = await _db.LearningRecords
            .Include(candidate => candidate.User)
            .Where(candidate =>
                candidate.ItemId == itemId &&
                candidate.UserId == currentUserId.Value)
            .OrderByDescending(candidate => candidate.EnrolledAt)
            .ThenByDescending(candidate => candidate.RecordId)
            .FirstOrDefaultAsync();
        if (record is null) return NotFound("当前用户尚未加入该课程。");

        var status = LearningWorkflow.NormalizeRecordStatus(record.EnrollStatus);
        if (status == LearningWorkflow.RecordStatusCancelled)
        {
            return BadRequest("当前用户已经退出该课程。");
        }
        if (status == LearningWorkflow.RecordStatusCompleted)
        {
            return BadRequest("已完成的课程记录不能退出。");
        }

        record.EnrollStatus = LearningWorkflow.RecordStatusCancelled;
        await _db.SaveChangesAsync();
        return Ok(ToRecordDto(record));
    }

    /// <summary>
    /// 为视频、文档或资料创建或恢复当前用户的学习记录。
    /// </summary>
    [HttpPost("items/{itemId:int}/learning")]
    public async Task<IActionResult> StartLearning(int itemId)
    {
        if (itemId <= 0) return BadRequest("资源 ID 必须大于 0。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");

        for (var attempt = 1; attempt <= MaxCreateRetries; attempt++)
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var item = await LoadLearningItemForAccessAsync(itemId);
            if (item is null) return NotFound("学习资源不存在。");
            if (!LearningWorkflow.IsSupportedResourceType(item.ItemType))
            {
                return BadRequest("培训课程请使用加入课程功能。");
            }

            var user = await LoadUserAsync(currentUserId.Value);
            if (user is null) return NotFound("当前用户不存在。");
            if (!UsersController.IsActive(user.AccountStatus)) return BadRequest("当前用户账号已停用。");

            var record = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
            var canManage = CanManageLearningItem(user, item);
            var accessDecision = GetLearningAccessDecision(user, item, record, canManage);
            if (accessDecision is not null)
            {
                return StatusCode(accessDecision.StatusCode, accessDecision.Message);
            }

            var previousStatus = record is null
                ? null
                : LearningWorkflow.NormalizeRecordStatus(record.EnrollStatus);
            if (previousStatus == LearningWorkflow.RecordStatusCompleted)
            {
                return Conflict("该资源已经完成学习，不能重新开始。");
            }
            if (record is not null && previousStatus != LearningWorkflow.RecordStatusCancelled)
            {
                return Ok(ToRecordDto(record));
            }

            var now = LearningWorkflow.BusinessNow();
            var created = record is null;
            if (record is null)
            {
                record = new DbLearningRecord
                {
                    RecordId = await GetNextLearningRecordId(),
                    ItemId = itemId,
                    UserId = currentUserId.Value
                };
                _db.LearningRecords.Add(record);
            }

            record.EnrollStatus = LearningWorkflow.RecordStatusLearning;
            record.EnrolledAt = now;
            record.Progress ??= 0;
            record.DurationSeconds ??= 0;
            record.LastLearnAt = now;
            record.CompletedAt = null;
            record.User = user;

            try
            {
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return created
                    ? CreatedAtAction(nameof(GetRecords), new { itemId }, ToRecordDto(record))
                    : Ok(ToRecordDto(record));
            }
            catch (DbUpdateException) when (attempt < MaxCreateRetries)
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
            }
        }

        return Conflict("创建学习记录时发生并发冲突，请重试。");
    }

    /// <summary>
    /// 校验资源可见范围和下载设置，记录下载用户、时间及来源 IP。
    /// </summary>
    [HttpPost("items/{itemId:int}/download")]
    public async Task<IActionResult> DownloadItem(int itemId)
    {
        if (itemId <= 0) return BadRequest("资源 ID 必须大于 0。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");

        for (var attempt = 1; attempt <= MaxCreateRetries; attempt++)
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var item = await LoadLearningItemForAccessAsync(itemId);
            if (item is null) return NotFound("学习资源不存在。");

            var user = await LoadUserAsync(currentUserId.Value);
            if (user is null) return NotFound("当前用户不存在。");
            if (!UsersController.IsActive(user.AccountStatus)) return BadRequest("当前用户账号已停用。");

            var record = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
            var canManage = CanManageLearningItem(user, item);
            var accessDecision = GetLearningAccessDecision(user, item, record, canManage);
            if (accessDecision is not null)
            {
                return StatusCode(accessDecision.StatusCode, accessDecision.Message);
            }

            var downloadDecision = GetDownloadDecision(item);
            if (downloadDecision is not null)
            {
                return StatusCode(downloadDecision.StatusCode, downloadDecision.Message);
            }

            var downloadUrl = _objectStorage.IsStorageReference(item.FileUrl)
                ? $"{LocalFileUrlPrefix}{itemId}/file"
                : item.FileUrl!.Trim();

            var now = LearningWorkflow.BusinessNow();
            if (record is null)
            {
                record = new DbLearningRecord
                {
                    RecordId = await GetNextLearningRecordId(),
                    ItemId = itemId,
                    UserId = currentUserId.Value,
                    EnrollStatus = LearningWorkflow.RecordStatusLearning,
                    EnrolledAt = now,
                    Progress = 0,
                    DurationSeconds = 0
                };
                _db.LearningRecords.Add(record);
            }
            else if (LearningWorkflow.NormalizeRecordStatus(record.EnrollStatus) ==
                     LearningWorkflow.RecordStatusCancelled)
            {
                record.EnrollStatus = LearningWorkflow.RecordStatusLearning;
                record.EnrolledAt = now;
            }

            record.DownloadedAt = now;
            record.DownloadIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok(new ApiLearningDownloadResult
                {
                    ItemId = item.ItemId,
                    Title = item.Title,
                    FileUrl = downloadUrl,
                    DownloadedAt = LearningWorkflow.AsUtc(now)
                });
            }
            catch (DbUpdateException) when (attempt < MaxCreateRetries)
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
            }
        }

        return Conflict("记录下载行为时发生并发冲突，请重试。");
    }

    /// <summary>
    /// 返回资源学习人数、完成数、平均进度、学习时长和下载人数。
    /// </summary>
    [HttpGet("items/{itemId:int}/statistics")]
    public async Task<IActionResult> GetStatistics(int itemId)
    {
        if (itemId <= 0) return BadRequest("资源 ID 必须大于 0。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");

        var user = await LoadUserAsync(currentUserId.Value);
        if (user is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(user.AccountStatus)) return BadRequest("当前用户账号已停用。");

        var item = await _db.LearningItems
            .AsNoTracking()
            .Include(candidate => candidate.Club)
            .FirstOrDefaultAsync(candidate => candidate.ItemId == itemId);
        if (item is null) return NotFound("学习资源不存在。");
        if (!CanManageLearningItem(user, item))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "当前用户无权查看该资源统计。");
        }

        var records = await _db.LearningRecords
            .AsNoTracking()
            .Where(record => record.ItemId == itemId)
            .ToListAsync();
        var activeRecords = records
            .Where(record => LearningWorkflow.NormalizeRecordStatus(record.EnrollStatus) !=
                             LearningWorkflow.RecordStatusCancelled)
            .ToList();
        var learnerCount = activeRecords.Count;
        var totalDuration = activeRecords.Sum(record => (long)(record.DurationSeconds ?? 0));

        return Ok(new ApiLearningItemStatistics
        {
            ItemId = item.ItemId,
            Title = item.Title,
            LearnerCount = learnerCount,
            CompletedCount = activeRecords.Count(record =>
                LearningWorkflow.NormalizeRecordStatus(record.EnrollStatus) ==
                    LearningWorkflow.RecordStatusCompleted ||
                record.Progress >= 100),
            DownloadCount = records.Count(record => record.DownloadedAt.HasValue),
            AverageProgress = learnerCount == 0
                ? 0
                : Math.Round(activeRecords.Average(record => (double)(record.Progress ?? 0)), 2),
            AverageDurationSeconds = learnerCount == 0
                ? 0
                : Math.Round(activeRecords.Average(record => (double)(record.DurationSeconds ?? 0)), 2),
            TotalDurationSeconds = (int)Math.Min(int.MaxValue, totalDuration)
        });
    }

    /// <summary>
    /// 用户查看自己的学习记录，课程管理者可查看课程成员名单。
    /// </summary>
    [HttpGet("records")]
    public async Task<IActionResult> GetRecords([FromQuery] int? itemId)
    {
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");
        if (itemId is not null and <= 0) return BadRequest("资源 ID 必须大于 0。");

        var user = await LoadUserAsync(currentUserId.Value);
        if (user is null) return NotFound("当前用户不存在。");
        if (!UsersController.IsActive(user.AccountStatus)) return BadRequest("当前用户账号已停用。");

        var query = _db.LearningRecords
            .AsNoTracking()
            .Include(record => record.User)
            .AsQueryable();

        if (itemId is null)
        {
            query = query.Where(record => record.UserId == currentUserId.Value);
        }
        else
        {
            var item = await _db.LearningItems
                .AsNoTracking()
                .Include(candidate => candidate.Club)
                .FirstOrDefaultAsync(candidate => candidate.ItemId == itemId.Value);
            if (item is null) return NotFound("课程或资源不存在。");

            query = CanManageLearningItem(user, item)
                ? query.Where(record => record.ItemId == itemId.Value)
                : query.Where(record =>
                    record.ItemId == itemId.Value &&
                    record.UserId == currentUserId.Value);
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
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");
        if (!LearningWorkflow.IsProgressValid((decimal)request.Progress))
        {
            return BadRequest("学习进度必须在 0 到 100 之间。");
        }
        if (request.DurationSeconds is < 0) return BadRequest("学习时长不能为负数。");

        var record = await _db.LearningRecords
            .Include(candidate => candidate.Item)
                .ThenInclude(item => item!.Club)
            .Include(candidate => candidate.Item)
                .ThenInclude(item => item!.Uploader)
                    .ThenInclude(uploader => uploader!.ClubMemberships)
            .Include(candidate => candidate.User)
            .FirstOrDefaultAsync(candidate => candidate.RecordId == recordId);
        if (record is null) return NotFound("学习记录不存在。");
        if (record.UserId != currentUserId.Value)
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
        if (record.Item is null) return Problem("学习记录关联的资源不存在。");

        var currentUser = await LoadUserAsync(currentUserId.Value);
        if (currentUser is null) return NotFound("当前用户不存在。");
        var canManage = CanManageLearningItem(currentUser, record.Item);
        var accessDecision = GetLearningAccessDecision(currentUser, record.Item, record, canManage);
        if (accessDecision is not null)
        {
            return StatusCode(accessDecision.StatusCode, accessDecision.Message);
        }

        if (LearningWorkflow.IsSupportedCourseType(record.Item.ItemType) &&
            (record.Item.StartAt is null ||
             LearningWorkflow.BusinessNow() < record.Item.StartAt.Value))
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
    /// 校验课程或资源的类型、文件地址、课程字段、可见范围及下载设置。
    /// </summary>
    private async Task<IActionResult?> ValidateLearningItemInputAsync(
        string? title,
        int? instructorUserId,
        string? itemType,
        string? categoryName,
        string? description,
        string? fileUrl,
        DateTime? startAt,
        DateTime? endAt,
        int? capacity,
        string? visibility,
        string? downloadPermission,
        User operatorUser,
        int clubId)
    {
        if (string.IsNullOrWhiteSpace(title)) return BadRequest("课程或资源标题不能为空。");
        if (title.Trim().Length > 100) return BadRequest("课程或资源标题不能超过 100 个字符。");
        if (!LearningWorkflow.IsSupportedItemType(itemType))
        {
            return BadRequest("资源类型只能是课程、讲座、培训、视频、文档或资料。");
        }
        if (NormalizeOptionalText(categoryName)?.Length > 100)
        {
            return BadRequest("资源分类不能超过 100 个字符。");
        }
        if (NormalizeOptionalText(description)?.Length > 1000)
        {
            return BadRequest("课程或资源说明不能超过 1000 个字符。");
        }
        if (!LearningWorkflow.IsVisibilityValid(visibility))
        {
            return BadRequest("可见范围只能是公开、社团内或部门内。");
        }
        if (!LearningWorkflow.IsDownloadPermissionValid(downloadPermission))
        {
            return BadRequest("下载设置只能是允许、禁止或需要审批。");
        }
        if (!string.IsNullOrWhiteSpace(fileUrl) && !LearningWorkflow.IsFileUrlValid(fileUrl))
        {
            return BadRequest("文件地址必须是长度不超过 255 的 HTTP 或 HTTPS 地址。");
        }

        if (visibility == LearningWorkflow.VisibilityDepartment &&
            GetActiveDepartmentName(operatorUser, clubId) is null)
        {
            return BadRequest("设置部门内可见前，上传人必须是该社团已设置部门的有效成员。");
        }

        if (LearningWorkflow.IsSupportedResourceType(itemType))
        {
            if (!LearningWorkflow.IsFileUrlValid(fileUrl))
            {
                return BadRequest("视频、文档和资料必须填写有效的 HTTP 或 HTTPS 文件地址。");
            }
            return null;
        }

        if (!LearningWorkflow.IsCourseTimeValid(startAt, endAt))
        {
            return BadRequest("课程开始时间必填；结束时间可不填，填写时必须晚于开始时间。");
        }
        if (!LearningWorkflow.IsCapacityValid(capacity))
        {
            return BadRequest("课程容量必填且必须大于 0。");
        }
        if (instructorUserId is null) return null;
        if (instructorUserId <= 0) return BadRequest("授课人信息无效。");

        var instructor = await LoadUserAsync(instructorUserId.Value);
        if (instructor is null ||
            !UsersController.IsActive(instructor.AccountStatus) ||
            (!IsTeacherAccount(instructor) && !IsStudentAccount(instructor)))
        {
            return BadRequest("授课人必须是正常状态的教师或学生账号。");
        }

        return null;
    }

    /// <summary>
    /// 返回当前用户不能加入课程的原因；可加入时返回空。
    /// </summary>
    private static EnrollmentDecision? GetEnrollmentDecision(
        User user,
        DbLearningItem item,
        DbLearningRecord? currentRecord,
        int activeEnrollmentCount,
        bool canManage,
        DateTime now)
    {
        if (!LearningWorkflow.IsSupportedCourseType(item.ItemType))
        {
            return new EnrollmentDecision(StatusCodes.Status400BadRequest, "该资源无需报名，请直接开始学习。");
        }
        if (!LearningWorkflow.IsPublished(item))
        {
            return new EnrollmentDecision(StatusCodes.Status400BadRequest, "课程当前未开放加入。");
        }
        if (!LearningWorkflow.IsEnrollmentWindowOpen(item, now))
        {
            return new EnrollmentDecision(StatusCodes.Status400BadRequest, "课程已经结束，不能加入。");
        }

        var recordStatus = currentRecord is null
            ? null
            : LearningWorkflow.NormalizeRecordStatus(currentRecord.EnrollStatus);
        if (recordStatus is not null && recordStatus != LearningWorkflow.RecordStatusCancelled)
        {
            return new EnrollmentDecision(StatusCodes.Status409Conflict, "当前用户已经加入该课程。");
        }

        var visibility = LearningWorkflow.NormalizeVisibility(item.Visibility);
        if (visibility is null)
        {
            return new EnrollmentDecision(
                StatusCodes.Status500InternalServerError,
                "课程开放范围配置异常，请联系管理员。");
        }
        if (visibility == LearningWorkflow.VisibilityClub &&
            !canManage &&
            !IsActiveClubMember(user, item.ClubId))
        {
            return new EnrollmentDecision(
                StatusCodes.Status403Forbidden,
                "该课程仅面向本社团有效成员开放。");
        }
        if (visibility == LearningWorkflow.VisibilityDepartment &&
            !canManage &&
            !IsSameActiveDepartment(user, item))
        {
            return new EnrollmentDecision(
                StatusCodes.Status403Forbidden,
                "该课程仅面向上传人所在部门的有效成员开放。");
        }
        if (!LearningWorkflow.HasEnrollmentCapacity(item, activeEnrollmentCount))
        {
            return new EnrollmentDecision(StatusCodes.Status409Conflict, "课程名额已满。");
        }

        return null;
    }

    /// <summary>
    /// 判断用户是否可按管理权、历史加入记录或开放范围查看课程。
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
            LearningWorkflow.VisibilityDepartment => IsSameActiveDepartment(viewer, item),
            _ => false
        };
    }

    /// <summary>
    /// 返回当前用户不能学习或下载资源的原因；允许访问时返回空。
    /// </summary>
    private static EnrollmentDecision? GetLearningAccessDecision(
        User user,
        DbLearningItem item,
        DbLearningRecord? currentRecord,
        bool canManage)
    {
        if (!CanViewLearningItem(user, item, currentRecord, canManage))
        {
            return new EnrollmentDecision(
                StatusCodes.Status403Forbidden,
                "当前用户不在该资源的可见范围内。");
        }
        if (!LearningWorkflow.IsPublished(item))
        {
            return new EnrollmentDecision(
                StatusCodes.Status400BadRequest,
                "资源当前未发布，不能学习或下载。");
        }
        return null;
    }

    /// <summary>
    /// 返回资源无法直接下载的原因；允许下载时返回空。
    /// </summary>
    private static EnrollmentDecision? GetDownloadDecision(DbLearningItem item)
    {
        if (!LearningWorkflow.IsFileUrlValid(item.FileUrl))
        {
            return new EnrollmentDecision(
                StatusCodes.Status400BadRequest,
                "资源没有有效的文件地址。");
        }

        return LearningWorkflow.NormalizeDownloadPermission(item.DownloadPermission) switch
        {
            LearningWorkflow.DownloadPermissionAllow => null,
            LearningWorkflow.DownloadPermissionApproval => new EnrollmentDecision(
                StatusCodes.Status403Forbidden,
                "该资源需要审批后下载，当前不能直接下载。"),
            _ => new EnrollmentDecision(
                StatusCodes.Status403Forbidden,
                "该资源已禁止下载。")
        };
    }

    /// <summary>
    /// 返回非课程资源当前无法开始学习的原因。
    /// </summary>
    private static EnrollmentDecision? GetLearningAvailabilityDecision(DbLearningItem item)
    {
        if (!LearningWorkflow.IsSupportedResourceType(item.ItemType))
        {
            return new EnrollmentDecision(
                StatusCodes.Status400BadRequest,
                "培训课程请使用加入课程功能。");
        }
        return LearningWorkflow.IsPublished(item)
            ? null
            : new EnrollmentDecision(
                StatusCodes.Status400BadRequest,
                "资源当前未发布，不能开始学习。");
    }

    /// <summary>
    /// 判断查看者与资源上传人是否为同一社团、同一部门的有效成员。
    /// </summary>
    private static bool IsSameActiveDepartment(User viewer, DbLearningItem item)
    {
        var viewerDepartment = GetActiveDepartmentName(viewer, item.ClubId);
        var uploaderDepartment = item.Uploader is null
            ? null
            : GetActiveDepartmentName(item.Uploader, item.ClubId);
        return viewerDepartment is not null &&
               uploaderDepartment is not null &&
               string.Equals(
                   viewerDepartment,
                   uploaderDepartment,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 返回用户在指定社团最近一条有效成员关系中的部门名称。
    /// </summary>
    private static string? GetActiveDepartmentName(User user, int clubId)
    {
        return user.ClubMemberships
            .Where(membership =>
                membership.ClubId == clubId &&
                UsersController.IsActive(membership.MemberStatus) &&
                !string.IsNullOrWhiteSpace(membership.DepartmentName))
            .OrderByDescending(membership => membership.JoinAt)
            .ThenByDescending(membership => membership.MemberId)
            .Select(membership => membership.DepartmentName!.Trim())
            .FirstOrDefault();
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
    /// 判断用户是否可维护课程及查看课程成员名单。
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
    /// 加载资源可见范围判断所需的社团和上传人部门成员关系。
    /// </summary>
    private Task<DbLearningItem?> LoadLearningItemForAccessAsync(int itemId)
    {
        return _db.LearningItems
            .Include(item => item.Club)
            .Include(item => item.Uploader)
                .ThenInclude(uploader => uploader!.ClubMemberships)
            .FirstOrDefaultAsync(item => item.ItemId == itemId);
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
    /// 根据系统身份角色判断账号是否属于教师。
    /// </summary>
    private static bool IsTeacherAccount(User user)
    {
        return user.UserRoles.Any(userRole =>
            userRole.Role is not null &&
            IsTeacherRole(userRole.Role));
    }

    /// <summary>
    /// 根据系统身份角色判断账号是否属于学生。
    /// </summary>
    private static bool IsStudentAccount(User user)
    {
        return user.UserRoles.Any(userRole =>
            userRole.Role is not null &&
            Normalize(userRole.Role.RoleCode) == "student");
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
    /// 将完整课程或学习资源实体映射为 API 模型；核心数据缺失时记录错误并返回空。
    /// </summary>
    private ApiLearningItem? TryMapItemDto(
        DbLearningItem item,
        int activeEnrollmentCount,
        DbLearningRecord? currentRecord,
        bool canManage,
        EnrollmentDecision? enrollmentDecision,
        DateTime now)
    {
        var normalizedVisibility = LearningWorkflow.NormalizeVisibility(item.Visibility);
        var normalizedDownloadPermission =
            LearningWorkflow.NormalizeDownloadPermission(item.DownloadPermission);
        var isCourse = LearningWorkflow.IsSupportedCourseType(item.ItemType);
        var isResource = LearningWorkflow.IsSupportedResourceType(item.ItemType);
        if ((!isCourse && !isResource) ||
            normalizedVisibility is null ||
            normalizedDownloadPermission is null ||
            (isCourse && (item.StartAt is null || item.Capacity is null)))
        {
            _logger.LogError(
                "学习资源 {ItemId} 数据不完整：Type={ItemType}, StartAtMissing={StartAtMissing}, CapacityMissing={CapacityMissing}, VisibilityInvalid={VisibilityInvalid}, DownloadPermissionInvalid={DownloadPermissionInvalid}。",
                item.ItemId,
                item.ItemType,
                item.StartAt is null,
                item.Capacity is null,
                normalizedVisibility is null,
                normalizedDownloadPermission is null);
            return null;
        }

        var currentRecordStatus = currentRecord is null
            ? LearningWorkflow.RecordStatusNone
            : LearningWorkflow.NormalizeRecordStatus(currentRecord.EnrollStatus);
        var canCancelEnrollment =
            isCourse &&
            (currentRecordStatus is LearningWorkflow.RecordStatusEnrolled or
                LearningWorkflow.RecordStatusLearning) &&
            (!item.EndAt.HasValue || now < item.EndAt.Value);
        var learningDecision = isResource &&
            currentRecordStatus == LearningWorkflow.RecordStatusCompleted
            ? new EnrollmentDecision(
                StatusCodes.Status409Conflict,
                "该资源已经完成学习，可在学习记录中查看结果。")
            : isResource
                ? GetLearningAvailabilityDecision(item)
                : new EnrollmentDecision(
                    StatusCodes.Status400BadRequest,
                    "培训课程请使用加入课程功能。");
        var downloadDecision = LearningWorkflow.IsPublished(item)
            ? GetDownloadDecision(item)
            : new EnrollmentDecision(
                StatusCodes.Status400BadRequest,
                "资源当前未发布，不能下载。");

        return new ApiLearningItem
        {
            Id = item.ItemId,
            ClubId = item.ClubId,
            UploaderUserId = item.UploaderUserId,
            InstructorUserId = item.TeacherUserId,
            Title = item.Title,
            ItemType = item.ItemType ?? string.Empty,
            CategoryName = item.CategoryName,
            Description = item.Description,
            FileUrl = canManage ? item.FileUrl : null,
            StartAt = LearningWorkflow.AsUtc(item.StartAt),
            EndAt = LearningWorkflow.AsUtc(item.EndAt),
            Capacity = item.Capacity,
            Visibility = ToItemVisibilityEnum(normalizedVisibility),
            DownloadPermission = ToItemDownloadPermissionEnum(normalizedDownloadPermission),
            ItemStatus = ToItemStatusEnum(
                LearningWorkflow.ResolveEffectiveItemStatus(item, now)),
            CurrentEnrollments = activeEnrollmentCount,
            CurrentUserRecordStatus = ToCurrentUserRecordStatusEnum(currentRecordStatus),
            CanManage = canManage,
            CanEnroll = isCourse && enrollmentDecision is null,
            CanCancelEnrollment = canCancelEnrollment,
            EnrollmentUnavailableReason = isCourse
                ? enrollmentDecision?.Message
                : "该资源无需报名，请直接开始学习。",
            CanStartLearning = isResource && learningDecision is null,
            LearningUnavailableReason = learningDecision?.Message,
            CanDownload = downloadDecision is null,
            DownloadUnavailableReason = downloadDecision?.Message
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
            CompletedAt = LearningWorkflow.AsUtc(record.CompletedAt),
            DownloadedAt = LearningWorkflow.AsUtc(record.DownloadedAt),
            DownloadIp = record.DownloadIp
        };
    }

    /// <summary>
    /// 返回不暴露数据库细节的课程数据完整性错误。
    /// </summary>
    private ObjectResult CourseDataIntegrityProblem(int itemId)
    {
        return Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "学习资源数据异常",
            detail: $"学习资源 {itemId} 缺少必填信息，请联系管理员检查数据。");
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
            CreateLearningItemRequest.VisibilityEnum.DepartmentEnum =>
                LearningWorkflow.VisibilityDepartment,
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
            UpdateLearningItemRequest.VisibilityEnum.DepartmentEnum =>
                LearningWorkflow.VisibilityDepartment,
            _ => string.Empty
        };
    }

    /// <summary>
    /// 将数据库开放范围转换为课程响应枚举。
    /// </summary>
    private static ApiLearningItem.VisibilityEnum ToItemVisibilityEnum(string visibility)
    {
        return visibility switch
        {
            LearningWorkflow.VisibilityPublic => ApiLearningItem.VisibilityEnum.PublicEnum,
            LearningWorkflow.VisibilityDepartment => ApiLearningItem.VisibilityEnum.DepartmentEnum,
            _ => ApiLearningItem.VisibilityEnum.ClubEnum
        };
    }

    /// <summary>
    /// 将创建请求的下载设置转换为数据库值。
    /// </summary>
    private static string ToDownloadPermissionValue(
        CreateLearningItemRequest.DownloadPermissionEnum permission)
    {
        return permission switch
        {
            CreateLearningItemRequest.DownloadPermissionEnum.AllowEnum =>
                LearningWorkflow.DownloadPermissionAllow,
            CreateLearningItemRequest.DownloadPermissionEnum.ApprovalEnum =>
                LearningWorkflow.DownloadPermissionApproval,
            _ => LearningWorkflow.DownloadPermissionDeny
        };
    }

    /// <summary>
    /// 将更新请求的下载设置转换为数据库值。
    /// </summary>
    private static string ToDownloadPermissionValue(
        UpdateLearningItemRequest.DownloadPermissionEnum permission)
    {
        return permission switch
        {
            UpdateLearningItemRequest.DownloadPermissionEnum.AllowEnum =>
                LearningWorkflow.DownloadPermissionAllow,
            UpdateLearningItemRequest.DownloadPermissionEnum.ApprovalEnum =>
                LearningWorkflow.DownloadPermissionApproval,
            _ => LearningWorkflow.DownloadPermissionDeny
        };
    }

    /// <summary>
    /// 将数据库下载设置转换为响应枚举。
    /// </summary>
    private static ApiLearningItem.DownloadPermissionEnum ToItemDownloadPermissionEnum(
        string permission)
    {
        return permission switch
        {
            LearningWorkflow.DownloadPermissionAllow =>
                ApiLearningItem.DownloadPermissionEnum.AllowEnum,
            LearningWorkflow.DownloadPermissionApproval =>
                ApiLearningItem.DownloadPermissionEnum.ApprovalEnum,
            _ => ApiLearningItem.DownloadPermissionEnum.DenyEnum
        };
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
    /// 生成包含身份、姓名和学工号的授课人候选展示文本。
    /// </summary>
    private static string BuildInstructorCandidateDisplayName(User user)
    {
        var name = BuildUserDisplayName(user, user.UserId);
        var userNumber = user.StudentNo?.Trim();
        var identity = IsTeacherAccount(user) ? "教师" : "学生";
        return string.IsNullOrWhiteSpace(userNumber)
            ? $"{identity} · {name}"
            : $"{identity} · {name}（{userNumber}）";
    }

    /// <summary>
    /// 根据 MIME 类型和扩展名确定现有资源类型，不引入新的数据库枚举值。
    /// </summary>
    private static string InferResourceType(string? contentType, string extension)
    {
        if (contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "video";
        }

        return extension is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or
            ".ppt" or ".pptx" or ".txt" or ".md"
            ? "document"
            : "material";
    }

    /// <summary>
    /// 阻止服务器端可能被误执行的常见可执行文件和脚本类型。
    /// </summary>
    private static bool IsBlockedUploadExtension(string extension) =>
        extension is ".exe" or ".dll" or ".com" or ".bat" or ".cmd" or ".ps1" or
            ".sh" or ".msi" or ".scr" or ".js" or ".vbs";

    private static bool IsInternalFileReference(string? fileUrl)
    {
        var normalized = fileUrl?.Trim();
        return normalized?.StartsWith(LocalFileUrlPrefix, StringComparison.Ordinal) == true ||
               normalized?.StartsWith(OssFileUrlPrefix, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsObjectStorageFailure(Exception exception) =>
        exception is LearningObjectStorageException;

    private async Task TryRemoveObjectAsync(string? storageReference, int itemId)
    {
        if (!_objectStorage.IsStorageReference(storageReference)) return;
        try
        {
            await _objectStorage.RemoveAsync(storageReference!, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "资源 {ItemId} 的数据库状态已更新，但 OSS 对象清理失败。",
                itemId);
        }
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
