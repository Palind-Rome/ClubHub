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
using ReviewLearningItemRequest = Org.OpenAPITools.Models.ReviewLearningItemRequest;

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
    private const string PublicViewPermission = "public:view";
    private const string OwnRecordsViewPermission = "own:records:view";
    private const string CourseEnrollPermission = "course:enroll";
    private const string ClubResourceViewPermission = "club:resource:view";
    private const string ClubOperationViewPermission = "club:operation:view";
    private const string ResourceUploadPermission = "resource:upload";
    private const string ClubStatsViewPermission = "club:stats:view";
    private const string GlobalStatsViewPermission = "stats:view";
    private const string ResourceReviewPermission = "resource:review";
    private const string ResourceDeletePermission = "resource:delete";

    private readonly ClubHubDbContext _db;
    private readonly AuthService _authService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILearningObjectStorage _objectStorage;
    private readonly LearningPreviewService _previewService;
    private readonly LearningPreviewSessionStore _previewSessionStore;
    private readonly AuthTokenService _authTokenService;
    private readonly ILogger<LearningController> _logger;

    public LearningController(
        ClubHubDbContext db,
        AuthService authService,
        IWebHostEnvironment environment,
        ILearningObjectStorage objectStorage,
        LearningPreviewService previewService,
        LearningPreviewSessionStore previewSessionStore,
        AuthTokenService authTokenService,
        ILogger<LearningController> logger)
    {
        _db = db;
        _authService = authService;
        _environment = environment;
        _objectStorage = objectStorage;
        _previewService = previewService;
        _previewSessionStore = previewSessionStore;
        _authTokenService = authTokenService;
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

        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        if (!CanCreateLearningItem(permissionRoles, club.ClubId))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "只有拥有本社团资源发布权限的用户可以查询授课人。");
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
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);

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
            var canManage = CanManageLearningItem(permissionRoles, item, currentUserId.Value);
            if (!CanViewLearningItem(viewer, permissionRoles, item, currentRecord, canManage)) continue;

            var activeEnrollmentCount = activeEnrollmentCounts.GetValueOrDefault(item.ItemId);
            var enrollmentDecision = GetEnrollmentDecision(
                viewer,
                permissionRoles,
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
                HasPermission(permissionRoles, OwnRecordsViewPermission, item.ClubId),
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
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        if (!CanCreateLearningItem(permissionRoles, club.ClubId))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "当前用户没有在该社团发布资源的权限。");
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
        var item = new DbLearningItem
        {
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
        await _db.SaveChangesAsync();

        var now = LearningWorkflow.BusinessNow();
        var decision = GetEnrollmentDecision(
            operatorUser,
            permissionRoles,
            item,
            null,
            0,
            canManage: true,
            now);
        var itemDto = TryMapItemDto(
            item,
            0,
            null,
            true,
            HasPermission(permissionRoles, OwnRecordsViewPermission, item.ClubId),
            decision,
            now);
        if (itemDto is null) return CourseDataIntegrityProblem(item.ItemId);

        return CreatedAtAction(
            nameof(GetItems),
            null,
            itemDto);
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
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        if (!CanCreateLearningItem(permissionRoles, club.ClubId))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "当前用户没有在该社团上传资源的权限。");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        string? storageReference = null;
        var item = new DbLearningItem
        {
            ClubId = clubId,
            UploaderUserId = currentUserId.Value,
            Title = normalizedTitle,
            ItemType = InferResourceType(file.ContentType, extension),
            CategoryName = normalizedCategory,
            Description = normalizedDescription,
            Visibility = normalizedVisibility,
            DownloadPermission = normalizedDownloadPermission,
            ItemStatus = LearningWorkflow.ItemStatusPendingReview,
            CreatedAt = LearningWorkflow.BusinessNow()
        };

        try
        {
            _db.LearningItems.Add(item);
            await _db.SaveChangesAsync();

            await using var stream = file.OpenReadStream();
            storageReference = await _objectStorage.UploadAsync(
                clubId,
                item.ItemId,
                extension,
                stream,
                file.Length,
                file.ContentType,
                originalFileName,
                HttpContext.RequestAborted);

            item.FileUrl = storageReference;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            var now = LearningWorkflow.BusinessNow();
            var itemDto = TryMapItemDto(
                item,
                0,
                null,
                true,
                HasPermission(permissionRoles, OwnRecordsViewPermission, item.ClubId),
                null,
                now);
            return itemDto is null
                ? CourseDataIntegrityProblem(item.ItemId)
                : CreatedAtAction(nameof(GetItems), null, itemDto);
        }
        catch (Exception exception) when (IsObjectStorageFailure(exception))
        {
            await transaction.RollbackAsync();
            await TryRemoveObjectAsync(storageReference, item.ItemId);
            _logger.LogError(exception, "资源 {ItemId} 上传到 OSS 失败。", item.ItemId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "资源存储暂不可用",
                detail: "无法连接 OSS 或 ECS RAM 角色凭据不可用，请稍后重试。");
        }
        catch
        {
            await transaction.RollbackAsync();
            await TryRemoveObjectAsync(storageReference, item.ItemId);
            throw;
        }
    }

    /// <summary>
    /// 校验资源可见范围、准备 Office 转换副本，并建立短时在线预览会话。
    /// </summary>
    [HttpPost("items/{itemId:int}/preview-session")]
    public async Task<IActionResult> CreatePreviewSession(int itemId)
    {
        var access = await GetPreviewAccessAsync(itemId);
        if (access.Error is not null) return access.Error;

        try
        {
            var preview = await _previewService.PrepareAsync(
                access.Item!.ItemId,
                access.Item.ClubId,
                access.Item.FileUrl,
                HttpContext.RequestAborted);
            var previewToken = _authTokenService.CreatePreviewToken(access.UserId, itemId);
            _previewSessionStore.Store(
                previewToken,
                access.UserId,
                itemId,
                preview,
                _authTokenService.PreviewSessionLifetime);
            Response.Cookies.Append(
                AuthTokenService.PreviewCookieName,
                previewToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = !_environment.IsDevelopment() || Request.IsHttps,
                    SameSite = SameSiteMode.Strict,
                    Path = $"/api/learning/items/{itemId}/preview",
                    MaxAge = _authTokenService.PreviewSessionLifetime,
                    IsEssential = true
                });
            Response.Headers["X-ClubHub-Preview-Kind"] = PreviewKindValue(preview.Kind);
            Response.Headers["X-ClubHub-Preview-Converted"] =
                preview.IsConverted ? "true" : "false";
            Response.Headers.CacheControl = "no-store";
            return NoContent();
        }
        catch (LearningPreviewException exception)
        {
            return PreviewFailure(exception);
        }
        catch (Exception exception) when (IsObjectStorageFailure(exception))
        {
            _logger.LogError(exception, "资源 {ItemId} 无法准备在线预览。", itemId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "资源预览暂不可用",
                detail: "无法从私有 OSS 读取或保存预览副本，请稍后重试。");
        }
    }

    /// <summary>
    /// 使用短时预览会话重新校验权限后，以 inline 和 Range 语义返回预览内容。
    /// </summary>
    [HttpGet("items/{itemId:int}/preview")]
    public async Task<IActionResult> GetPreview(int itemId)
    {
        var access = await GetPreviewAccessAsync(itemId);
        if (access.Error is not null) return access.Error;

        PreparedLearningPreview? preview = null;
        try
        {
            if (!Request.Cookies.TryGetValue(AuthTokenService.PreviewCookieName, out var previewToken) ||
                !_previewSessionStore.TryGet(
                    previewToken,
                    access.UserId,
                    itemId,
                    out preview) || preview is null)
            {
                return Unauthorized("在线预览会话已失效，请重新打开预览。");
            }
            var preparedPreview = preview!;
            LearningPreviewHttpPolicy.Apply(
                Response,
                preparedPreview.ContentType,
                BuildPreviewFileName(access.Item.Title, preparedPreview));

            var content = await _previewService.OpenAsync(
                preparedPreview,
                Request.Headers.Range.ToString(),
                HttpContext.RequestAborted);
            if (content.PhysicalPath is not null)
            {
                return new PhysicalFileResult(content.PhysicalPath, preparedPreview.ContentType)
                {
                    EnableRangeProcessing = true
                };
            }

            if (content.Range is not null)
            {
                Response.StatusCode = StatusCodes.Status206PartialContent;
                Response.Headers.ContentRange =
                    $"bytes {content.Range.Start}-{content.Range.End}/{content.Range.TotalLength}";
            }
            Response.ContentLength = content.ContentLength;
            return File(content.Content!, preparedPreview.ContentType);
        }
        catch (LearningPreviewException exception)
        {
            if (exception.Failure == LearningPreviewFailure.InvalidRange && preview is not null)
            {
                Response.Headers.ContentRange = $"bytes */{preview.Length}";
            }
            return PreviewFailure(exception);
        }
        catch (Exception exception) when (IsObjectStorageFailure(exception))
        {
            _logger.LogError(exception, "资源 {ItemId} 无法返回在线预览内容。", itemId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "资源预览暂不可用",
                detail: "无法从私有 OSS 读取预览内容，请稍后重试。");
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
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        var record = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
        var canManage = CanManageLearningItem(permissionRoles, item, currentUserId.Value);
        var accessDecision = GetLearningAccessDecision(user, permissionRoles, item, record, canManage);
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
                var metadata = await _objectStorage.GetMetadataAsync(
                    item.FileUrl!,
                    HttpContext.RequestAborted);
                var storedObject = await _objectStorage.OpenReadAsync(
                    item.FileUrl!,
                    null,
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
            ? Directory.EnumerateFiles(clubStoragePath, $"{itemId}.*").FirstOrDefault()
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
        var operatorUser = await LoadUserAsync(currentUserId.Value);
        if (operatorUser is null) return NotFound("当前用户不存在。");
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        if (!CanManageLearningItem(permissionRoles, item, currentUserId.Value) &&
            !HasPermission(permissionRoles, ResourceDeletePermission, item.ClubId))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "当前用户没有删除该社团资源的权限。");
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

        try
        {
            await _previewService.RemovePreviewAsync(
                itemId,
                clubId,
                item.FileUrl,
                HttpContext.RequestAborted);
        }
        catch (Exception exception) when (IsObjectStorageFailure(exception))
        {
            await transaction.RollbackAsync();
            _logger.LogError(exception, "资源 {ItemId} 的预览副本无法从对象存储删除。", itemId);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "资源存储暂不可用",
                detail: "无法清理资源预览副本，数据库记录已保留，请稍后重试。");
        }

        if (storageReference is not null)
        {
            try
            {
                await _objectStorage.RemoveAsync(storageReference, HttpContext.RequestAborted);
            }
            catch (Exception exception) when (IsObjectStorageFailure(exception))
            {
                await transaction.RollbackAsync();
                _logger.LogError(exception, "资源 {ItemId} 无法从对象存储删除。", itemId);
                return Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "资源存储暂不可用",
                    detail: "无法从对象存储删除资源，数据库记录已保留，请稍后重试。");
            }
        }

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
        return NoContent();
    }

    /// <summary>
    /// 社团管理员或系统管理员审核待发布的课程、资源。
    /// </summary>
    [HttpPost("items/{itemId:int}/review")]
    public async Task<IActionResult> ReviewItem(
        int itemId,
        [FromBody] ReviewLearningItemRequest? request)
    {
        if (itemId <= 0) return BadRequest("资源 ID 必须大于 0。");
        if (request is null) return BadRequest("审核结果不能为空。");
        var currentUserId = User.GetUserId();
        if (currentUserId is null) return Unauthorized("登录状态已失效，请重新登录。");

        var item = await LoadLearningItemForAccessAsync(itemId);
        if (item is null) return NotFound("课程或资源不存在。");
        var reviewer = await LoadUserAsync(currentUserId.Value);
        if (reviewer is null) return NotFound("审核用户不存在。");
        if (!UsersController.IsActive(reviewer.AccountStatus)) return BadRequest("当前用户账号已停用。");

        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        if (!HasPermission(permissionRoles, ResourceReviewPermission, item.ClubId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "当前用户没有审核课程或资源的权限。");
        }
        if (item.UploaderUserId == currentUserId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "不能审核自己提交的课程或资源。");
        }
        if (LearningWorkflow.NormalizeItemStatus(item.ItemStatus) !=
            LearningWorkflow.ItemStatusPendingReview)
        {
            return Conflict("只有待审核的课程或资源可以处理审核结果。");
        }

        var approved = request.Result == ReviewLearningItemRequest.ResultEnum.ApprovedEnum;
        var rejected = request.Result == ReviewLearningItemRequest.ResultEnum.RejectedEnum;
        if (!approved && !rejected) return BadRequest("审核结果只能是 approved 或 rejected。");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        item.ItemStatus = approved
            ? LearningWorkflow.ItemStatusPublished
            : LearningWorkflow.ItemStatusRejected;
        _db.OperationLogs.Add(new OperationLog
        {
            UserId = currentUserId.Value,
            ModuleName = "learning",
            OperationType = approved ? "review_approved" : "review_rejected",
            TargetTable = "LEARNING_ITEMS",
            TargetId = item.ItemId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            CreatedAt = LearningWorkflow.BusinessNow()
        });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var activeEnrollmentCount = await CountActiveEnrollmentsAsync(itemId);
        var currentRecord = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
        var canManage = CanManageLearningItem(permissionRoles, item, currentUserId.Value);
        var dto = TryMapItemDto(
            item,
            activeEnrollmentCount,
            currentRecord,
            canManage,
            HasPermission(permissionRoles, OwnRecordsViewPermission, item.ClubId),
            GetEnrollmentDecision(
                reviewer,
                permissionRoles,
                item,
                currentRecord,
                activeEnrollmentCount,
                canManage,
                LearningWorkflow.BusinessNow()),
            LearningWorkflow.BusinessNow());
        return dto is null ? CourseDataIntegrityProblem(itemId) : Ok(dto);
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
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        if (!CanManageLearningItem(permissionRoles, item, currentUserId.Value))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "当前用户没有修改该社团资源的权限。");
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
            permissionRoles,
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
            HasPermission(permissionRoles, OwnRecordsViewPermission, item.ClubId),
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
            var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
            if (!HasPermission(permissionRoles, CourseEnrollPermission, item.ClubId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "当前用户没有参加课程的权限。");
            }

            var existingRecord = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
            var activeEnrollmentCount = await CountActiveEnrollmentsAsync(itemId);
            var canManage = CanManageLearningItem(permissionRoles, item, currentUserId.Value);
            var decision = GetEnrollmentDecision(
                user,
                permissionRoles,
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
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        if (!HasPermission(permissionRoles, OwnRecordsViewPermission, null))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "当前用户没有退出课程的权限。");
        }

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
            var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
            if (!HasPermission(permissionRoles, OwnRecordsViewPermission, item.ClubId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "当前用户没有学习资源的权限。");
            }

            var record = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
            var canManage = CanManageLearningItem(permissionRoles, item, currentUserId.Value);
            var accessDecision = GetLearningAccessDecision(user, permissionRoles, item, record, canManage);
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
            var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
            if (!HasPermission(permissionRoles, OwnRecordsViewPermission, item.ClubId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "当前用户没有下载学习资源的权限。");
            }

            var record = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
            var canManage = CanManageLearningItem(permissionRoles, item, currentUserId.Value);
            var accessDecision = GetLearningAccessDecision(user, permissionRoles, item, record, canManage);
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
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        if (!CanViewLearningStatistics(permissionRoles, item.ClubId))
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
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);

        var query = _db.LearningRecords
            .AsNoTracking()
            .Include(record => record.User)
            .AsQueryable();

        if (itemId is null)
        {
            if (!HasPermission(permissionRoles, OwnRecordsViewPermission, null))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "当前用户无权查看个人学习记录。");
            }
            query = query.Where(record => record.UserId == currentUserId.Value);
        }
        else
        {
            var item = await _db.LearningItems
                .AsNoTracking()
                .Include(candidate => candidate.Club)
                .FirstOrDefaultAsync(candidate => candidate.ItemId == itemId.Value);
            if (item is null) return NotFound("课程或资源不存在。");

            if (CanViewLearningRecords(permissionRoles, item.ClubId))
            {
                query = query.Where(record => record.ItemId == itemId.Value);
            }
            else if (HasPermission(permissionRoles, OwnRecordsViewPermission, item.ClubId))
            {
                query = query.Where(record =>
                    record.ItemId == itemId.Value &&
                    record.UserId == currentUserId.Value);
            }
            else
            {
                return StatusCode(StatusCodes.Status403Forbidden, "当前用户无权查看该资源的学习记录。");
            }
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
        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        if (!HasPermission(permissionRoles, OwnRecordsViewPermission, null))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "当前用户没有维护个人学习记录的权限。");
        }
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
        var canManage = CanManageLearningItem(permissionRoles, record.Item, currentUserId.Value);
        var accessDecision = GetLearningAccessDecision(
            currentUser,
            permissionRoles,
            record.Item,
            record,
            canManage);
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
        IReadOnlyList<AuthRole> permissionRoles,
        DbLearningItem item,
        DbLearningRecord? currentRecord,
        int activeEnrollmentCount,
        bool canManage,
        DateTime now)
    {
        if (!HasPermission(permissionRoles, CourseEnrollPermission, item.ClubId))
        {
            return new EnrollmentDecision(
                StatusCodes.Status403Forbidden,
                "当前用户没有参加课程的权限。");
        }
        if (item.TeacherUserId == user.UserId)
        {
            return new EnrollmentDecision(
                StatusCodes.Status403Forbidden,
                "授课人不能加入本人负责的课程。");
        }
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
    /// 判断用户是否可按管理权、授课人身份、历史加入记录或开放范围查看课程。
    /// </summary>
    private static bool CanViewLearningItem(
        User viewer,
        IReadOnlyList<AuthRole> permissionRoles,
        DbLearningItem item,
        DbLearningRecord? currentRecord,
        bool canManage)
    {
        if (canManage ||
            HasPermission(permissionRoles, ResourceReviewPermission, item.ClubId) ||
            HasPermission(permissionRoles, ResourceDeletePermission, item.ClubId)) return true;

        var itemStatus = LearningWorkflow.NormalizeItemStatus(item.ItemStatus);
        if (itemStatus == LearningWorkflow.ItemStatusDraft) return false;

        var recordStatus = currentRecord is null
            ? null
            : LearningWorkflow.NormalizeRecordStatus(currentRecord.EnrollStatus);
        if (recordStatus is not null &&
            recordStatus != LearningWorkflow.RecordStatusCancelled &&
            HasPermission(permissionRoles, OwnRecordsViewPermission, item.ClubId))
        {
            return true;
        }

        // 待审核和已驳回内容仅对提交者本人可见，普通用户只能看到已发布或已结束的内容。
        if (itemStatus is LearningWorkflow.ItemStatusPendingReview or LearningWorkflow.ItemStatusRejected)
        {
            return item.UploaderUserId == viewer.UserId;
        }

        return LearningWorkflow.NormalizeVisibility(item.Visibility) switch
        {
            LearningWorkflow.VisibilityPublic =>
                HasPermission(permissionRoles, PublicViewPermission, null),
            LearningWorkflow.VisibilityClub =>
                HasPermission(permissionRoles, ClubResourceViewPermission, item.ClubId) &&
                IsActiveClubMember(viewer, item.ClubId),
            LearningWorkflow.VisibilityDepartment =>
                HasPermission(permissionRoles, ClubResourceViewPermission, item.ClubId) &&
                IsSameActiveDepartment(viewer, item),
            _ => false
        };
    }

    /// <summary>
    /// 在线预览始终重新校验登录用户、资源可见范围和非公开状态的管理权限。
    /// </summary>
    private async Task<PreviewAccessResult> GetPreviewAccessAsync(int itemId)
    {
        if (itemId <= 0)
        {
            return new PreviewAccessResult(
                null,
                0,
                BadRequest("资源 ID 必须大于 0。"));
        }

        var currentUserId = User.GetUserId();
        if (currentUserId is null)
        {
            return new PreviewAccessResult(
                null,
                0,
                Unauthorized("在线预览会话已失效，请重新打开预览。"));
        }

        var item = await LoadLearningItemForAccessAsync(itemId);
        if (item is null)
        {
            return new PreviewAccessResult(null, currentUserId.Value, NotFound("学习资源不存在。"));
        }
        if (!LearningWorkflow.IsSupportedResourceType(item.ItemType))
        {
            return new PreviewAccessResult(
                null,
                currentUserId.Value,
                BadRequest("培训课程没有可在线预览的资源文件。"));
        }

        var user = await LoadUserAsync(currentUserId.Value);
        if (user is null)
        {
            return new PreviewAccessResult(null, currentUserId.Value, NotFound("当前用户不存在。"));
        }
        if (!UsersController.IsActive(user.AccountStatus))
        {
            return new PreviewAccessResult(
                null,
                currentUserId.Value,
                StatusCode(StatusCodes.Status403Forbidden, "当前用户账号已停用。"));
        }

        var permissionRoles = await _authService.GetPermissionRolesAsync(currentUserId.Value);
        var record = await GetLatestLearningRecordAsync(itemId, currentUserId.Value);
        var canManage = CanManageLearningItem(permissionRoles, item, currentUserId.Value);
        var canReview = HasPermission(permissionRoles, ResourceReviewPermission, item.ClubId);
        var canDelete = HasPermission(permissionRoles, ResourceDeletePermission, item.ClubId);
        var isVisible = CanViewLearningItem(user, permissionRoles, item, record, canManage);
        if (!LearningPreviewAccessPolicy.CanPreview(
                isVisible,
                LearningWorkflow.IsPublished(item),
                canManage,
                canReview,
                canDelete))
        {
            return new PreviewAccessResult(
                null,
                currentUserId.Value,
                StatusCode(
                    StatusCodes.Status403Forbidden,
                    "当前用户不在该资源的可见范围内，或没有预览非公开资源的管理权限。"));
        }

        return new PreviewAccessResult(item, currentUserId.Value, null);
    }

    private IActionResult PreviewFailure(LearningPreviewException exception)
    {
        var statusCode = exception.Failure switch
        {
            LearningPreviewFailure.Unsupported => StatusCodes.Status415UnsupportedMediaType,
            LearningPreviewFailure.InvalidRange => StatusCodes.Status416RangeNotSatisfiable,
            LearningPreviewFailure.ConversionFailed => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status404NotFound
        };
        _logger.LogWarning(
            exception,
            "学习资源在线预览失败，类型 {Failure}。",
            exception.Failure);
        return StatusCode(statusCode, exception.Message);
    }

    private static string PreviewKindValue(LearningPreviewKind kind) => kind switch
    {
        LearningPreviewKind.Image => "image",
        LearningPreviewKind.Video => "video",
        _ => "pdf"
    };

    private static string BuildPreviewFileName(
        string title,
        PreparedLearningPreview preview)
    {
        var extension = preview.ContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            _ => ".pdf"
        };
        return title.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? title
            : $"{title}{extension}";
    }

    /// <summary>
    /// 返回当前用户不能学习或下载资源的原因；允许访问时返回空。
    /// </summary>
    private static EnrollmentDecision? GetLearningAccessDecision(
        User user,
        IReadOnlyList<AuthRole> permissionRoles,
        DbLearningItem item,
        DbLearningRecord? currentRecord,
        bool canManage)
    {
        if (!CanViewLearningItem(user, permissionRoles, item, currentRecord, canManage))
        {
            return new EnrollmentDecision(
                StatusCodes.Status403Forbidden,
                "当前用户不在该资源的可见范围内。");
        }
        if (!LearningWorkflow.IsPublished(item))
        {
            // 具有审核权限且非提交者本人的用户可以查看待审核资源文件以完成审核。
            if (HasPermission(permissionRoles, ResourceReviewPermission, item.ClubId) &&
                item.UploaderUserId != user.UserId)
            {
                return null;
            }
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
    private static bool CanCreateLearningItem(
        IReadOnlyList<AuthRole> permissionRoles,
        int clubId) => HasPermission(permissionRoles, ResourceUploadPermission, clubId);

    /// <summary>
    /// 判断用户是否可按社团负责人权限维护课程；课程授课人具有同等维护权限。
    /// </summary>
    private static bool CanManageLearningItem(
        IReadOnlyList<AuthRole> permissionRoles,
        DbLearningItem item,
        int currentUserId) =>
        HasPermission(permissionRoles, ResourceUploadPermission, item.ClubId) ||
        (LearningWorkflow.IsSupportedCourseType(item.ItemType) &&
         item.TeacherUserId == currentUserId);

    /// <summary>
    /// 判断当前权限是否允许查看指定社团资源的实名学习记录。
    /// </summary>
    private static bool CanViewLearningRecords(
        IReadOnlyList<AuthRole> permissionRoles,
        int clubId) =>
        HasPermission(permissionRoles, ResourceUploadPermission, clubId) ||
        HasPermission(permissionRoles, ClubStatsViewPermission, clubId) ||
        HasPermission(permissionRoles, ClubOperationViewPermission, clubId);

    /// <summary>
    /// 判断当前权限是否允许查看指定社团资源的匿名聚合统计。
    /// </summary>
    private static bool CanViewLearningStatistics(
        IReadOnlyList<AuthRole> permissionRoles,
        int clubId) =>
        CanViewLearningRecords(permissionRoles, clubId) ||
        HasPermission(permissionRoles, GlobalStatsViewPermission, null);

    private static bool HasPermission(
        IReadOnlyList<AuthRole> permissionRoles,
        string permission,
        int? clubId) => AuthService.RolesAllow(permissionRoles, permission, clubId);

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
    /// 将创建请求枚举映射为数据库课程状态。
    /// </summary>
    private static string? ToCreateStatusValue(CreateLearningItemRequest.ItemStatusEnum status)
    {
        return status switch
        {
            CreateLearningItemRequest.ItemStatusEnum.DraftEnum =>
                LearningWorkflow.ItemStatusDraft,
            CreateLearningItemRequest.ItemStatusEnum.PublishedEnum =>
                LearningWorkflow.ItemStatusPendingReview,
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
                LearningWorkflow.ItemStatusPendingReview,
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
        bool canUseOwnRecords,
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
            canUseOwnRecords &&
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
            CanStartLearning = canUseOwnRecords && isResource && learningDecision is null,
            LearningUnavailableReason = canUseOwnRecords
                ? learningDecision?.Message
                : "当前账号没有学习资源的权限。",
            CanDownload = canUseOwnRecords && downloadDecision is null,
            DownloadUnavailableReason = canUseOwnRecords
                ? downloadDecision?.Message
                : "当前账号没有下载学习资源的权限。"
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
            LearningWorkflow.ItemStatusPendingReview =>
                ApiLearningItem.ItemStatusEnum.PendingReviewEnum,
            LearningWorkflow.ItemStatusRejected =>
                ApiLearningItem.ItemStatusEnum.RejectedEnum,
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

    private bool IsInternalFileReference(string? fileUrl)
    {
        var normalized = fileUrl?.Trim();
        return normalized?.StartsWith(LocalFileUrlPrefix, StringComparison.Ordinal) == true ||
               _objectStorage.IsStorageReference(normalized);
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

    private sealed record PreviewAccessResult(
        DbLearningItem? Item,
        int UserId,
        IActionResult? Error);
}
