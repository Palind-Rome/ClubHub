namespace ClubHub.Api.Services;

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
    string DisplayName,
    string Scope,
    int? ClubId,
    IReadOnlyList<int> ClubIds,
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
