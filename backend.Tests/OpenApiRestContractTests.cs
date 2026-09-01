using System.Text.RegularExpressions;

namespace ClubHub.Api.Tests;

public class OpenApiRestContractTests
{
    private static readonly string[] LegacyRpcPaths =
    [
        "/api/v1/auth/permissions/check",
        "/api/v1/auth/roles/assign",
        "/api/v1/clubs/{clubId}/dissolve",
        "/api/v1/clubs/applications/{clubId}/review",
        "/api/v1/clubs/{clubId}/members/self/exit",
        "/api/v1/clubs/{clubId}/members/{memberId}/exit",
        "/api/v1/activities/{activityId}/review",
        "/api/v1/activities/{activityId}/budget/review",
        "/api/v1/activities/{activityId}/checkin",
        "/api/v1/activities/{activityId}/checkout",
        "/api/v1/recruitments/{recruitId}/review",
        "/api/v1/recruitments/applications/{applicationId}/review",
        "/api/v1/projects/{projectId}/review",
        "/api/v1/projects/{projectId}/cancel",
        "/api/v1/venue-reservations/{reservationId}/review",
        "/api/v1/learning/instructor-lookup",
        "/api/v1/learning/items/{itemId}/review",
        "/api/v1/learning/items/{itemId}/learning",
        "/api/v1/learning/items/{itemId}/download",
        "/api/v1/learning/resources/upload"
    ];

    [Fact]
    public void EveryDocumentedApiPathUsesV1Prefix()
    {
        var document = ReadDocument();
        var paths = Regex.Matches(document, @"(?m)^  (?<path>/api/[^:]+):\s*$")
            .Select(match => match.Groups["path"].Value)
            .ToArray();

        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.StartsWith("/api/v1/", path, StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyRpcPathsAreNotPartOfV1Contract()
    {
        var document = ReadDocument();

        Assert.All(LegacyRpcPaths, path => Assert.DoesNotContain($"  {path}:", document, StringComparison.Ordinal));
    }

    [Fact]
    public void RecruitmentRequestsDoNotAcceptClientIdentity()
    {
        var document = ReadDocument();
        var recruitmentSchemas = Slice(
            document,
            "    CreateRecruitmentRequest:",
            "    UpdateUserAccountStatusRequest:");
        var recruitmentPaths = Slice(
            document,
            "  /api/v1/recruitments:",
            "  /api/v1/users:");

        Assert.DoesNotContain("currentUserId:", recruitmentSchemas, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("viewerUserId", recruitmentPaths, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PaginationComponentsAreDeclared()
    {
        var document = ReadDocument();

        Assert.Contains("    Page:\n", NormalizeNewLines(document), StringComparison.Ordinal);
        Assert.Contains("    PageSize:\n", NormalizeNewLines(document), StringComparison.Ordinal);
        Assert.Contains("    X-Total-Count:\n", NormalizeNewLines(document), StringComparison.Ordinal);
        Assert.Contains("    Link:\n", NormalizeNewLines(document), StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionCatalogAndCurrentUserCheckDeclareAccurateSecurity()
    {
        var document = ReadDocument();
        var catalogOperation = Slice(
            document,
            "  /api/v1/auth/permissions:",
            "  /api/v1/users/me/permissions:");
        var currentUserOperation = Slice(
            document,
            "  /api/v1/users/me/permissions:",
            "  /api/v1/users/{userId}/roles:");

        Assert.DoesNotContain("bearerAuth", catalogOperation, StringComparison.Ordinal);
        Assert.Contains("bearerAuth", currentUserOperation, StringComparison.Ordinal);
        Assert.Contains("required: true", currentUserOperation, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiErrorCodeUsesStandardRequiredValues()
    {
        var document = ReadDocument();
        var apiErrorSchema = Slice(document, "    ApiError:", "    HealthStatus:");

        Assert.All(
            new[]
            {
                "VALIDATION_ERROR",
                "UNAUTHORIZED",
                "FORBIDDEN",
                "NOT_FOUND",
                "CONFLICT",
                "PAYLOAD_TOO_LARGE",
                "RATE_LIMITED",
                "SERVICE_UNAVAILABLE",
                "INTERNAL_ERROR",
                "REQUEST_FAILED"
            },
            code => Assert.Contains(code, apiErrorSchema, StringComparison.Ordinal));
        Assert.Contains("pattern: '^(", apiErrorSchema, StringComparison.Ordinal);
        Assert.Contains("      required:\n        - code", NormalizeNewLines(apiErrorSchema), StringComparison.Ordinal);
    }

    [Fact]
    public void ResubmitBudgetApplicationDeclaresApiErrorResponses()
    {
        var document = ReadDocument();
        var operation = Slice(
            document,
            "  /api/v1/budget/applications/{applicationId}/resubmit:",
            "  /api/v1/budget/transactions:");

        foreach (var status in new[] { "400", "401", "403", "404", "409" })
        {
            var responsePattern =
                $@"(?ms)^        ""{status}"":\s*$.*?^          content:\s*$.*?^            application/json:\s*$.*?^              schema:\s*$.*?^                \$ref: ""#/components/schemas/ApiError""\s*$";
            Assert.Matches(responsePattern, operation);
        }
    }

    [Fact]
    public void ChangePasswordDocumentsSensitiveDataHandlingAndRateLimitResponse()
    {
        var document = ReadDocument();
        var operation = Slice(
            document,
            "  /api/v1/users/me/password:",
            "  /api/v1/auth/roles:");

        Assert.Contains("仅保存安全哈希", operation, StringComparison.Ordinal);
        Assert.Contains("不会在响应、日志或操作记录中返回或记录", operation, StringComparison.Ordinal);
        Assert.Contains("撤销该用户可撤销的全部登录会话", operation, StringComparison.Ordinal);
        var rateLimitResponse = Slice(operation, "        \"429\":", "        \"503\":");
        Assert.Contains("Retry-After:", rateLimitResponse, StringComparison.Ordinal);
        Assert.Contains("type: integer", rateLimitResponse, StringComparison.Ordinal);
        Assert.Contains("minimum: 0", rateLimitResponse, StringComparison.Ordinal);
        Assert.Contains("example: 60", rateLimitResponse, StringComparison.Ordinal);
        Assert.Matches(
            "(?ms)^        \"429\":\\s*$.*?^          content:\\s*$.*?^                \\$ref: \"#/components/schemas/ApiError\"\\s*$",
            operation);
    }

    private static string ReadDocument()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../api/openapi.yaml"));
        return File.ReadAllText(path);
    }

    private static string Slice(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);

        Assert.True(startIndex >= 0, $"Missing OpenAPI marker: {start}");
        Assert.True(endIndex > startIndex, $"Missing OpenAPI marker: {end}");
        return value[startIndex..endIndex];
    }

    private static string NormalizeNewLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
