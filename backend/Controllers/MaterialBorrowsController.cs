using System.Data;
using System.Security.Claims;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiError = Org.OpenAPITools.Models.ApiError;

namespace ClubHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class MaterialBorrowsController : ControllerBase
{
    private const string ManagePermission = "material:borrow:manage";
    private const string MaterialStatusActive = "active";
    private const string MaterialStatusDisabled = "disabled";
    private const string BorrowStatusBorrowed = "borrowed";
    private const string BorrowStatusReturned = "returned";
    private const string BorrowStatusDamaged = "damaged";
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

        var access = await GetMaterialAccessAsync(currentUserId.Value);
        if (access.Error is not null) return access.Error;
        if (!access.CanManageAll && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("material_view_forbidden", "当前用户没有物资借还管理权限。"));
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
    public async Task<IActionResult> CreateMaterial([FromBody] CreateMaterialRequest req)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var permission = await RequireClubPermissionAsync(currentUserId.Value, req.ClubId);
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

        if (!IsValidStatus(req.Status, AllowedMaterialStatuses, out var normalizedStatus))
        {
            return BadRequest(Error("material_status_invalid", "物资状态参数不合法。"));
        }

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

        var access = await GetMaterialAccessAsync(currentUserId.Value);
        if (access.Error is not null) return access.Error;
        if (!access.CanManageAll && access.ClubIds.Count == 0)
        {
            return StatusCode(403, Error("borrow_view_forbidden", "当前用户没有物资借还管理权限。"));
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
    public async Task<IActionResult> BorrowMaterial([FromBody] BorrowMaterialRequest req)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        if (req.Quantity <= 0)
        {
            return BadRequest(Error("borrow_quantity_invalid", "借用数量必须大于 0。"));
        }

        DateTime? expectedReturnAt = req.ExpectedReturnAt is null
            ? null
            : RequestTimeToUtc(req.ExpectedReturnAt.Value);
        if (expectedReturnAt is not null && expectedReturnAt.Value <= DateTime.UtcNow)
        {
            return BadRequest(Error("expected_return_time_invalid", "预计归还时间必须晚于当前时间。"));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var material = await MaterialQuery()
            .FirstOrDefaultAsync(m => m.MaterialId == req.MaterialId);
        if (material is null) return NotFound(Error("material_not_found", "物资不存在。"));
        if (material.ClubId != req.ClubId)
        {
            return BadRequest(Error("material_club_mismatch", "物资不属于所选社团。"));
        }

        var permission = await RequireClubPermissionAsync(currentUserId.Value, req.ClubId);
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
        await transaction.CommitAsync();

        return CreatedAtAction(nameof(GetBorrows), new { clubId = borrow.ClubId }, ToDto(borrow));
    }

    [HttpPost("material-borrows/{borrowId:int}/return")]
    public async Task<IActionResult> ReturnMaterial(int borrowId)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var borrow = await BorrowQuery().FirstOrDefaultAsync(b => b.BorrowId == borrowId);
        if (borrow is null) return NotFound(Error("borrow_not_found", "借用记录不存在。"));

        var permission = await RequireClubPermissionAsync(currentUserId.Value, borrow.ClubId);
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
        await transaction.CommitAsync();

        return Ok(ToDto(borrow));
    }

    [HttpPost("material-borrows/{borrowId:int}/damage")]
    public async Task<IActionResult> RegisterDamage(int borrowId, [FromBody] RegisterMaterialDamageRequest req)
    {
        var currentUserId = GetAuthenticatedUserId();
        if (currentUserId is null) return Unauthorized(Error("auth_required", "登录状态已失效，请重新登录。"));

        var damageDesc = NormalizeText(req.DamageDescription);
        if (string.IsNullOrWhiteSpace(damageDesc))
        {
            return BadRequest(Error("damage_description_required", "损坏说明不能为空。"));
        }

        if (req.CompensationAmount < 0)
        {
            return BadRequest(Error("compensation_amount_invalid", "赔偿金额不能为负数。"));
        }

        var borrow = await BorrowQuery().FirstOrDefaultAsync(b => b.BorrowId == borrowId);
        if (borrow is null) return NotFound(Error("borrow_not_found", "借用记录不存在。"));

        var permission = await RequireClubPermissionAsync(currentUserId.Value, borrow.ClubId);
        if (permission is not null) return permission;

        if (NormalizeStatus(borrow.BorrowStatus) != BorrowStatusBorrowed)
        {
            return BadRequest(Error("borrow_status_not_borrowed", "只有借用中的记录可以登记损坏。"));
        }

        borrow.BorrowStatus = BorrowStatusDamaged;
        borrow.ReturnAt = DateTime.UtcNow;
        borrow.DamageDesc = damageDesc;
        borrow.CompensationAmount = req.CompensationAmount;

        await _db.SaveChangesAsync();

        return Ok(ToDto(borrow));
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

    private async Task<IActionResult?> RequireClubPermissionAsync(int userId, int clubId)
    {
        var permission = await _authService.CheckPermissionAsync(userId, ManagePermission, clubId);
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

    private async Task<(IActionResult? Error, bool CanManageAll, IReadOnlyList<int> ClubIds)> GetMaterialAccessAsync(int userId)
    {
        var globalPermission = await _authService.CheckPermissionAsync(userId, ManagePermission, null);
        if (!globalPermission.Succeeded)
        {
            return (
                StatusCode(globalPermission.StatusCode, Error(
                    "material_permission_check_failed",
                    globalPermission.ErrorMessage ?? "物资借还权限校验失败。")),
                false,
                []);
        }

        var clubPermission = await _authService.GetPermissionClubIdsAsync(userId, ManagePermission);
        if (!clubPermission.Succeeded)
        {
            return (
                StatusCode(clubPermission.StatusCode, Error(
                    "material_permission_check_failed",
                    clubPermission.ErrorMessage ?? "物资借还权限校验失败。")),
                false,
                []);
        }

        return (null, globalPermission.Value?.Allowed == true, clubPermission.Value ?? []);
    }

    private static MaterialDto ToDto(Material material)
    {
        var totalQty = material.TotalQty ?? 0;
        var availableQty = Math.Clamp(material.AvailableQty ?? 0, 0, totalQty);
        return new MaterialDto(
            material.MaterialId,
            material.ClubId,
            material.Club?.ClubName ?? "",
            material.MaterialName ?? "",
            material.Specification,
            totalQty,
            availableQty,
            Math.Max(0, totalQty - availableQty),
            material.StorageLocation,
            NormalizeMaterialStatus(material.MaterialStatus),
            StoredTimeToUtc(material.CreatedAt ?? DateTime.MinValue));
    }

    private static MaterialBorrowDto ToDto(MaterialBorrow borrow)
    {
        return new MaterialBorrowDto(
            borrow.BorrowId,
            borrow.MaterialId,
            borrow.Material?.MaterialName ?? "",
            borrow.Material?.Specification,
            borrow.ClubId,
            borrow.Club?.ClubName ?? "",
            borrow.BorrowerUserId,
            DisplayName(borrow.BorrowerUser),
            borrow.Quantity ?? 0,
            StoredTimeToUtc(borrow.BorrowAt ?? DateTime.MinValue),
            borrow.ExpectedReturnAt is null ? null : StoredTimeToUtc(borrow.ExpectedReturnAt.Value),
            borrow.ReturnAt is null ? null : StoredTimeToUtc(borrow.ReturnAt.Value),
            NormalizeBorrowStatus(borrow.BorrowStatus),
            borrow.DamageDesc,
            borrow.CompensationAmount ?? 0,
            NormalizeBorrowStatus(borrow.BorrowStatus) == BorrowStatusBorrowed &&
                borrow.ExpectedReturnAt is not null &&
                StoredTimeToUtc(borrow.ExpectedReturnAt.Value) < DateTime.UtcNow);
    }

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

public record MaterialDto(
    int Id,
    int ClubId,
    string ClubName,
    string Name,
    string? Specification,
    int TotalQuantity,
    int AvailableQuantity,
    int BorrowedQuantity,
    string? StorageLocation,
    string Status,
    DateTime CreatedAt);

public record MaterialBorrowDto(
    int Id,
    int MaterialId,
    string MaterialName,
    string? Specification,
    int ClubId,
    string ClubName,
    int BorrowerUserId,
    string? BorrowerName,
    int Quantity,
    DateTime BorrowAt,
    DateTime? ExpectedReturnAt,
    DateTime? ReturnAt,
    string Status,
    string? DamageDescription,
    decimal CompensationAmount,
    bool Overdue);

public record CreateMaterialRequest(
    int ClubId,
    string Name,
    string? Specification,
    int TotalQuantity,
    int? AvailableQuantity,
    string? StorageLocation,
    string? Status);

public record BorrowMaterialRequest(
    int MaterialId,
    int ClubId,
    int Quantity,
    DateTime? ExpectedReturnAt);

public record RegisterMaterialDamageRequest(
    string DamageDescription,
    decimal CompensationAmount);
