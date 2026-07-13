using System.Data;
using System.Security.Claims;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiError = Org.OpenAPITools.Models.ApiError;
using ApiBorrowMaterialRequest = Org.OpenAPITools.Models.BorrowMaterialRequest;
using ApiCreateMaterialRequest = Org.OpenAPITools.Models.CreateMaterialRequest;
using ApiMaterial = Org.OpenAPITools.Models.Material;
using ApiMaterialBorrow = Org.OpenAPITools.Models.MaterialBorrow;
using ApiRegisterMaterialDamageRequest = Org.OpenAPITools.Models.RegisterMaterialDamageRequest;
using ApiUpdateMaterialRequest = Org.OpenAPITools.Models.UpdateMaterialRequest;

namespace ClubHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class MaterialBorrowsController : ControllerBase
{
    private const string BorrowUsePermission = "material:borrow:use";
    private const string BorrowRecordPermission = "material:borrow:record";
    private const string InventoryManagePermission = "material:inventory:manage";
    private static readonly TimeSpan MaxBorrowDuration = TimeSpan.FromDays(7);
    private const string MaterialStatusActive = "active";
    private const string MaterialStatusDisabled = "disabled";
    private const string BorrowStatusBorrowed = "borrowed";
    private const string BorrowStatusReturned = "returned";
    private const string BorrowStatusDamaged = "damaged";
    private const int MaxWriteRetries = 3;
    private static readonly TimeZoneInfo BeijingTimeZone = ResolveBeijingTimeZone();

    private static readonly HashSet<string> AllowedMaterialStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        MaterialStatusActive,
        MaterialStatusDisabled
    };

    private static readonly HashSet<string> AllowedBorrowStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        BorrowStatusBorrowed,
        BorrowStatusReturned,
        BorrowStatusDamaged
    };

    private readonly ClubHubDbContext _db;
    private readonly AuthService _authService;

    public MaterialBorrowsController(ClubHubDbContext db, AuthService authService)
    {
        _db = db;
        _authService = authService;
    }

    [HttpGet("materials")]
    public async Task<IActionResult> GetMaterials([FromQuery] int? clubId, [FromQuery] string? status)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        if (!IsValidStatus(status, AllowedMaterialStatuses, out var normalizedStatus))
        {
            return BadRequest(Error("material_status_invalid", "物资状态参数不合法。"));
        }

        var access = await GetMaterialAccessAsync(
            currentUserId.Value,
            BorrowUsePermission,
            BorrowRecordPermission,
            InventoryManagePermission);
        if (access.Error is not null) return access.Error;
        if (!access.CanManageAll && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("material_view_forbidden", "当前用户没有物资借还查看权限。"));
        }

        var query = MaterialQuery().AsNoTracking();
        if (!access.CanManageAll)
        {
            query = query.Where(m => access.ClubIds.Contains(m.ClubId));
        }

        if (clubId is not null)
        {
            if (!access.CanManageAll && !access.ClubIds.Contains(clubId.Value))
            {
                return StatusCode(403, Error("material_club_forbidden", "当前用户没有该社团的物资管理权限。"));
            }

            query = query.Where(m => m.ClubId == clubId.Value);
        }

        if (normalizedStatus is not null)
        {
            query = query.Where(m => m.MaterialStatus == normalizedStatus);
        }

        var materials = await query
            .OrderBy(m => m.ClubId)
            .ThenBy(m => m.MaterialName)
            .ThenBy(m => m.MaterialId)
            .ToListAsync();

        return Ok(materials.Select(ToDto));
    }

    [HttpPost("materials")]
    public async Task<IActionResult> CreateMaterial([FromBody] ApiCreateMaterialRequest req)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var permission = await RequireClubPermissionAsync(currentUserId.Value, InventoryManagePermission, req.ClubId);
        if (permission is not null) return permission;

        var name = NormalizeText(req.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(Error("material_name_required", "物资名称不能为空。"));
        }

        if (req.TotalQuantity <= 0)
        {
            return BadRequest(Error("material_quantity_invalid", "物资总数量必须大于 0。"));
        }

        if (req.AvailableQuantity is not null && (req.AvailableQuantity < 0 || req.AvailableQuantity > req.TotalQuantity))
        {
            return BadRequest(Error("material_available_quantity_invalid", "可用数量必须在 0 到总数量之间。"));
        }

        var normalizedStatus = NormalizeMaterialStatus(req.Status);
        if (normalizedStatus is null)
        {
            return BadRequest(Error("material_status_invalid", "物资状态参数不合法。"));
        }

        return await ExecuteSerializableWriteAsync(
            async () =>
            {
                var club = await _db.Clubs.FindAsync(req.ClubId);
                if (club is null) return NotFound(Error("club_not_found", "社团不存在。"));

                var material = new Material
                {
                    MaterialId = (await _db.Materials.MaxAsync(m => (int?)m.MaterialId) ?? 0) + 1,
                    ClubId = req.ClubId,
                    MaterialName = name,
                    Specification = NullIfBlank(req.Specification),
                    TotalQty = req.TotalQuantity,
                    AvailableQty = req.AvailableQuantity ?? req.TotalQuantity,
                    StorageLocation = NullIfBlank(req.StorageLocation),
                    MaterialStatus = normalizedStatus ?? MaterialStatusActive,
                    CreatedAt = DateTime.UtcNow,
                    Club = club
                };

                _db.Materials.Add(material);
                await _db.SaveChangesAsync();

                return CreatedAtAction(nameof(GetMaterials), new { clubId = material.ClubId }, ToDto(material));
            },
            "material_write_conflict",
            "物资正在被其他操作修改，请稍后重试。");
    }

    [HttpPut("materials/{materialId:int}")]
    public async Task<IActionResult> UpdateMaterial(int materialId, [FromBody] ApiUpdateMaterialRequest req)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var material = await MaterialQuery().FirstOrDefaultAsync(m => m.MaterialId == materialId);
        if (material is null) return NotFound(Error("material_not_found", "物资不存在。"));

        var permission = await RequireClubPermissionAsync(currentUserId.Value, InventoryManagePermission, material.ClubId);
        if (permission is not null) return permission;

        var name = NormalizeText(req.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(Error("material_name_required", "物资名称不能为空。"));
        }

        if (req.TotalQuantity <= 0)
        {
            return BadRequest(Error("material_quantity_invalid", "物资总数量必须大于 0。"));
        }

        if (req.AvailableQuantity < 0 || req.AvailableQuantity > req.TotalQuantity)
        {
            return BadRequest(Error("material_available_quantity_invalid", "可用数量必须在 0 到总数量之间。"));
        }

        var borrowedQuantity = Math.Max(0, (material.TotalQty ?? 0) - (material.AvailableQty ?? 0));
        if (req.TotalQuantity < borrowedQuantity)
        {
            return BadRequest(Error("material_total_quantity_invalid", "总数量不能小于当前借出数量。"));
        }

        if (req.AvailableQuantity > req.TotalQuantity - borrowedQuantity)
        {
            return BadRequest(Error("material_available_quantity_invalid", "可用数量不能超过扣除当前借出数量后的库存余量。"));
        }

        var normalizedStatus = NormalizeMaterialStatus(req.Status);
        if (normalizedStatus is null)
        {
            return BadRequest(Error("material_status_invalid", "物资状态参数不合法。"));
        }

        material.MaterialName = name;
        material.Specification = NullIfBlank(req.Specification);
        material.TotalQty = req.TotalQuantity;
        material.AvailableQty = req.AvailableQuantity;
        material.StorageLocation = NullIfBlank(req.StorageLocation);
        material.MaterialStatus = normalizedStatus;

        await _db.SaveChangesAsync();

        return Ok(ToDto(material));
    }

    [HttpGet("material-borrows")]
    public async Task<IActionResult> GetBorrows(
        [FromQuery] int? clubId,
        [FromQuery] int? materialId,
        [FromQuery] int? borrowerUserId,
        [FromQuery] string? status)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        if (!IsValidStatus(status, AllowedBorrowStatuses, out var normalizedStatus))
        {
            return BadRequest(Error("borrow_status_invalid", "借用状态参数不合法。"));
        }

        var access = await GetMaterialAccessAsync(
            currentUserId.Value,
            BorrowRecordPermission,
            InventoryManagePermission);
        if (access.Error is not null) return access.Error;
        if (!access.CanManageAll && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("borrow_view_forbidden", "当前用户没有查看物资借还记录权限。"));
        }

        var query = BorrowQuery().AsNoTracking();
        if (!access.CanManageAll)
        {
            query = query.Where(b => access.ClubIds.Contains(b.ClubId));
        }

        if (clubId is not null)
        {
            if (!access.CanManageAll && !access.ClubIds.Contains(clubId.Value))
            {
                return StatusCode(403, Error("borrow_club_forbidden", "当前用户没有该社团的物资借还管理权限。"));
            }

            query = query.Where(b => b.ClubId == clubId.Value);
        }

        if (materialId is not null)
        {
            query = query.Where(b => b.MaterialId == materialId.Value);
        }

        if (borrowerUserId is not null)
        {
            query = query.Where(b => b.BorrowerUserId == borrowerUserId.Value);
        }

        if (normalizedStatus is not null)
        {
            query = query.Where(b => b.BorrowStatus == normalizedStatus);
        }

        var borrows = await query
            .OrderByDescending(b => b.BorrowAt)
            .ThenByDescending(b => b.BorrowId)
            .ToListAsync();

        return Ok(borrows.Select(ToDto));
    }

    [HttpPost("material-borrows")]
    public async Task<IActionResult> BorrowMaterial([FromBody] ApiBorrowMaterialRequest req)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        if (req.Quantity <= 0)
        {
            return BadRequest(Error("borrow_quantity_invalid", "借用数量必须大于 0。"));
        }

        if (req.ExpectedReturnAt == default)
        {
            return BadRequest(Error("expected_return_time_required", "预计归还时间不能为空。"));
        }

        var now = DateTime.UtcNow;
        var expectedReturnAt = RequestTimeToUtc(req.ExpectedReturnAt);
        if (expectedReturnAt <= now)
        {
            return BadRequest(Error("expected_return_time_invalid", "预计归还时间必须晚于当前时间。"));
        }

        if (expectedReturnAt > now.Add(MaxBorrowDuration))
        {
            return BadRequest(Error("expected_return_time_too_late", "预计归还时间不能超过 7 天。"));
        }

        return await ExecuteSerializableWriteAsync(
            async () =>
            {
                var material = await MaterialQuery()
                    .FirstOrDefaultAsync(m => m.MaterialId == req.MaterialId);
                if (material is null) return NotFound(Error("material_not_found", "物资不存在。"));
                if (material.ClubId != req.ClubId)
                {
                    return BadRequest(Error("material_club_mismatch", "物资不属于所选社团。"));
                }

                var permission = await RequireClubPermissionAsync(currentUserId.Value, BorrowUsePermission, req.ClubId);
                if (permission is not null) return permission;

                if (NormalizeStatus(material.MaterialStatus) != MaterialStatusActive)
                {
                    return BadRequest(Error("material_not_active", "该物资当前不可借用。"));
                }

                var availableQty = material.AvailableQty ?? 0;
                if (availableQty < req.Quantity)
                {
                    return Conflict(Error("material_stock_not_enough", "物资可用数量不足。"));
                }

                var borrower = await _db.Users.FindAsync(currentUserId.Value);
                if (borrower is null) return NotFound(Error("borrower_not_found", "借用人不存在。"));

                material.AvailableQty = availableQty - req.Quantity;

                var borrow = new MaterialBorrow
                {
                    BorrowId = (await _db.MaterialBorrows.MaxAsync(b => (int?)b.BorrowId) ?? 0) + 1,
                    MaterialId = req.MaterialId,
                    ClubId = req.ClubId,
                    BorrowerUserId = currentUserId.Value,
                    Quantity = req.Quantity,
                    BorrowAt = DateTime.UtcNow,
                    ExpectedReturnAt = expectedReturnAt,
                    BorrowStatus = BorrowStatusBorrowed,
                    Material = material,
                    Club = material.Club,
                    BorrowerUser = borrower
                };

                _db.MaterialBorrows.Add(borrow);
                await _db.SaveChangesAsync();

                return CreatedAtAction(nameof(GetBorrows), new { clubId = borrow.ClubId }, ToDto(borrow));
            },
            "borrow_concurrent_conflict",
            "物资库存正在被其他操作修改，请稍后重试。");
    }

    [HttpPost("material-borrows/{borrowId:int}/return")]
    public async Task<IActionResult> ReturnMaterial(int borrowId)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        return await ExecuteSerializableWriteAsync(
            async () =>
            {
                var borrow = await BorrowQuery().FirstOrDefaultAsync(b => b.BorrowId == borrowId);
                if (borrow is null) return NotFound(Error("borrow_not_found", "借用记录不存在。"));

                var permission = await RequireClubPermissionAsync(currentUserId.Value, BorrowRecordPermission, borrow.ClubId);
                if (permission is not null) return permission;

                if (NormalizeStatus(borrow.BorrowStatus) != BorrowStatusBorrowed)
                {
                    return BadRequest(Error("borrow_status_not_borrowed", "只有借用中的记录可以登记归还。"));
                }

                var material = borrow.Material;
                if (material is null) return NotFound(Error("material_not_found", "物资不存在。"));

                var quantity = borrow.Quantity ?? 0;
                var totalQty = material.TotalQty ?? 0;
                material.AvailableQty = Math.Min(totalQty, (material.AvailableQty ?? 0) + quantity);
                borrow.BorrowStatus = BorrowStatusReturned;
                borrow.ReturnAt = DateTime.UtcNow;
                borrow.DamageDesc = null;
                borrow.CompensationAmount = null;

                await _db.SaveChangesAsync();

                return Ok(ToDto(borrow));
            },
            "borrow_concurrent_conflict",
            "借用记录正在被其他操作修改，请稍后重试。");
    }

    [HttpPost("material-borrows/{borrowId:int}/damage")]
    public async Task<IActionResult> RegisterDamage(int borrowId, [FromBody] ApiRegisterMaterialDamageRequest req)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var damageDesc = NormalizeText(req.DamageDescription);
        if (string.IsNullOrWhiteSpace(damageDesc))
        {
            return BadRequest(Error("damage_description_required", "损坏说明不能为空。"));
        }

        if (!double.IsFinite(req.CompensationAmount) ||
            req.CompensationAmount < 0 ||
            req.CompensationAmount > (double)decimal.MaxValue)
        {
            return BadRequest(Error("compensation_amount_invalid", "赔偿金额不合法。"));
        }

        var compensationAmount = Convert.ToDecimal(req.CompensationAmount);
        return await ExecuteSerializableWriteAsync(
            async () =>
            {
                var borrow = await BorrowQuery().AsNoTracking().FirstOrDefaultAsync(b => b.BorrowId == borrowId);
                if (borrow is null) return NotFound(Error("borrow_not_found", "借用记录不存在。"));

                var permission = await RequireClubPermissionAsync(currentUserId.Value, BorrowRecordPermission, borrow.ClubId);
                if (permission is not null) return permission;

                var returnAt = DateTime.UtcNow;
                var affected = await _db.MaterialBorrows
                    .Where(b => b.BorrowId == borrowId && b.BorrowStatus == BorrowStatusBorrowed)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(b => b.BorrowStatus, BorrowStatusDamaged)
                        .SetProperty(b => b.ReturnAt, returnAt)
                        .SetProperty(b => b.DamageDesc, damageDesc)
                        .SetProperty(b => b.CompensationAmount, compensationAmount));

                if (affected != 1)
                {
                    return Conflict(Error("borrow_status_changed", "借用记录状态已变化，请刷新后重试。"));
                }

                var updatedBorrow = await BorrowQuery().AsNoTracking().FirstAsync(b => b.BorrowId == borrowId);
                return Ok(ToDto(updatedBorrow));
            },
            "borrow_concurrent_conflict",
            "借用记录正在被其他操作修改，请稍后重试。");
    }

    private IQueryable<Material> MaterialQuery()
    {
        return _db.Materials
            .Include(m => m.Club);
    }

    private IQueryable<MaterialBorrow> BorrowQuery()
    {
        return _db.MaterialBorrows
            .Include(b => b.Material)
            .Include(b => b.Club)
            .Include(b => b.BorrowerUser);
    }

    private async Task<IActionResult> ExecuteSerializableWriteAsync(
        Func<Task<IActionResult>> operation,
        string conflictCode,
        string conflictMessage)
    {
        for (var attempt = 1; attempt <= MaxWriteRetries; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var result = await operation();
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

    private async Task<IActionResult?> RequireClubPermissionAsync(int userId, string permissionCode, int clubId)
    {
        var permission = await _authService.CheckPermissionAsync(userId, permissionCode, clubId);
        if (!permission.Succeeded)
        {
            return StatusCode(permission.StatusCode, Error(
                "material_permission_check_failed",
                permission.ErrorMessage ?? "物资借还权限校验失败。"));
        }

        if (permission.Value?.Allowed != true)
        {
            return StatusCode(403, Error(
                "material_permission_forbidden",
                permission.Value?.Message ?? "当前用户没有该社团的物资借还管理权限。"));
        }

        return null;
    }

    private async Task<(IActionResult? Error, bool CanManageAll, IReadOnlyList<int> ClubIds)> GetMaterialAccessAsync(
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
                        "material_permission_check_failed",
                        globalPermission.ErrorMessage ?? "物资借还权限校验失败。")),
                    false,
                    []);
            }

            canManageAll = canManageAll || globalPermission.Value?.Allowed == true;

            var clubPermission = await _authService.GetPermissionClubIdsAsync(userId, permissionCode);
            if (!clubPermission.Succeeded)
            {
                return (
                    StatusCode(clubPermission.StatusCode, Error(
                        "material_permission_check_failed",
                        clubPermission.ErrorMessage ?? "物资借还权限校验失败。")),
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

    private static ApiMaterial ToDto(Material material)
    {
        var totalQty = material.TotalQty ?? 0;
        var availableQty = Math.Clamp(material.AvailableQty ?? 0, 0, totalQty);
        return new ApiMaterial
        {
            Id = material.MaterialId,
            ClubId = material.ClubId,
            ClubName = material.Club?.ClubName ?? "",
            Name = material.MaterialName ?? "",
            Specification = material.Specification,
            TotalQuantity = totalQty,
            AvailableQuantity = availableQty,
            BorrowedQuantity = Math.Max(0, totalQty - availableQty),
            StorageLocation = material.StorageLocation,
            Status = ToApiMaterialStatus(material.MaterialStatus),
            CreatedAt = StoredTimeToUtc(material.CreatedAt ?? DateTime.MinValue)
        };
    }

    private static ApiMaterialBorrow ToDto(MaterialBorrow borrow)
    {
        return new ApiMaterialBorrow
        {
            Id = borrow.BorrowId,
            MaterialId = borrow.MaterialId,
            MaterialName = borrow.Material?.MaterialName ?? "",
            Specification = borrow.Material?.Specification,
            ClubId = borrow.ClubId,
            ClubName = borrow.Club?.ClubName ?? "",
            BorrowerUserId = borrow.BorrowerUserId,
            BorrowerName = DisplayName(borrow.BorrowerUser),
            Quantity = borrow.Quantity ?? 0,
            BorrowAt = StoredTimeToUtc(borrow.BorrowAt ?? DateTime.MinValue),
            ExpectedReturnAt = borrow.ExpectedReturnAt is null
                ? null
                : StoredTimeToUtc(borrow.ExpectedReturnAt.Value),
            ReturnAt = borrow.ReturnAt is null ? null : StoredTimeToUtc(borrow.ReturnAt.Value),
            Status = ToApiBorrowStatus(borrow.BorrowStatus),
            DamageDescription = borrow.DamageDesc,
            CompensationAmount = Convert.ToDouble(borrow.CompensationAmount ?? 0),
            Overdue = NormalizeBorrowStatus(borrow.BorrowStatus) == BorrowStatusBorrowed &&
                borrow.ExpectedReturnAt is not null &&
                StoredTimeToUtc(borrow.ExpectedReturnAt.Value) < DateTime.UtcNow
        };
    }

    private static string? NormalizeMaterialStatus(ApiCreateMaterialRequest.StatusEnum status) => status switch
    {
        ApiCreateMaterialRequest.StatusEnum.ActiveEnum => MaterialStatusActive,
        ApiCreateMaterialRequest.StatusEnum.DisabledEnum => MaterialStatusDisabled,
        _ => null
    };

    private static string? NormalizeMaterialStatus(ApiUpdateMaterialRequest.StatusEnum status) => status switch
    {
        ApiUpdateMaterialRequest.StatusEnum.ActiveEnum => MaterialStatusActive,
        ApiUpdateMaterialRequest.StatusEnum.DisabledEnum => MaterialStatusDisabled,
        _ => null
    };

    private static ApiMaterial.StatusEnum ToApiMaterialStatus(string? status) =>
        NormalizeMaterialStatus(status) == MaterialStatusDisabled
            ? ApiMaterial.StatusEnum.DisabledEnum
            : ApiMaterial.StatusEnum.ActiveEnum;

    private static ApiMaterialBorrow.StatusEnum ToApiBorrowStatus(string? status) =>
        NormalizeBorrowStatus(status) switch
        {
            BorrowStatusReturned => ApiMaterialBorrow.StatusEnum.ReturnedEnum,
            BorrowStatusDamaged => ApiMaterialBorrow.StatusEnum.DamagedEnum,
            _ => ApiMaterialBorrow.StatusEnum.BorrowedEnum
        };

    private static bool IsValidStatus(string? status, HashSet<string> allowed, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        return normalized is null || allowed.Contains(normalized);
    }

    private static string NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? "" : status.Trim().ToLowerInvariant();
    }

    private static string NormalizeMaterialStatus(string? status)
    {
        var normalized = NormalizeStatus(status);
        return AllowedMaterialStatuses.Contains(normalized) ? normalized : MaterialStatusActive;
    }

    private static string NormalizeBorrowStatus(string? status)
    {
        var normalized = NormalizeStatus(status);
        return AllowedBorrowStatuses.Contains(normalized) ? normalized : BorrowStatusBorrowed;
    }

    private static DateTime RequestTimeToUtc(DateTime value)
    {
        if (value == default) return value;

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(value, BeijingTimeZone)
        };
    }

    private static DateTime StoredTimeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static TimeZoneInfo ResolveBeijingTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
    }

    private int? GetAuthenticatedUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(rawUserId, out var userId) && userId > 0 ? userId : null;
    }

    private static string? DisplayName(User? user)
    {
        if (user is null) return null;
        return string.IsNullOrWhiteSpace(user.RealName) ? user.Username : user.RealName;
    }

    private static string NormalizeText(string? value) => (value ?? string.Empty).Trim();

    private static string? NullIfBlank(string? value)
    {
        var normalized = NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static ApiError Error(string code, string message, string? detail = null) => new()
    {
        Code = code,
        Message = message,
        Detail = detail
    };
}
