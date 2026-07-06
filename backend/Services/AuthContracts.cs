namespace ClubHub.Api.Services;

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RealName { get; set; } = string.Empty;
    public string StudentNo { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? College { get; set; }
    public string? Major { get; set; }
    public string? Grade { get; set; }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AssignRoleRequest
{
    public int OperatorUserId { get; set; }
    public int TargetUserId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public int? ClubId { get; set; }
}

public record ApiError(string Message);

public record AuthResponse(
    string Token,
    AuthUser User,
    IReadOnlyList<AuthRole> Roles,
    IReadOnlyList<string> Permissions
);

public record AuthUser(
    int Id,
    string Username,
    string RealName,
    string? StudentNo,
    string? Gender,
    string? Phone,
    string? Email,
    string? College,
    string? Major,
    string? Grade,
    string AccountStatus
);

public record AuthRole(
    int Id,
    string Code,
    string Name,
    string Scope,
    int? ClubId,
    IReadOnlyList<string> Permissions,
    string? PermissionDesc
);

public record RoleDefinition(
    string Code,
    string Name,
    string Scope,
    string Description,
    IReadOnlyList<string> Permissions
);

public record PermissionDefinition(
    string Code,
    string Name,
    string Description
);

public record PermissionCheckResult(
    int UserId,
    string Permission,
    int? ClubId,
    bool Allowed,
    IReadOnlyList<AuthRole> MatchedRoles,
    string Message
);

public record RoleAssignmentResult(
    int TargetUserId,
    AuthRole Role,
    bool AlreadyExists,
    string Message
);

public record AuthServiceResult<T>(
    int StatusCode,
    T? Value,
    string? ErrorMessage
)
{
    public bool Succeeded => ErrorMessage is null;

    public static AuthServiceResult<T> Ok(T value) => new(200, value, null);
    public static AuthServiceResult<T> Created(T value) => new(201, value, null);
    public static AuthServiceResult<T> Fail(int statusCode, string message) => new(statusCode, default, message);
}
