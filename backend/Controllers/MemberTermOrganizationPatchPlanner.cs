using System.Text.Json;

namespace ClubHub.Api.Controllers;

internal readonly record struct MemberTermOrganizationPatchFields(
    bool HasDepartmentId,
    bool HasDepartmentName,
    bool HasGroupId,
    bool HasGroupName)
{
    public bool OrganizationRequested => HasDepartmentId || HasDepartmentName || HasGroupId || HasGroupName;
    public bool DepartmentRequested => HasDepartmentId || HasDepartmentName;
    public bool GroupRequested => HasGroupId || HasGroupName;

    public static MemberTermOrganizationPatchFields FromJson(JsonElement body) =>
        new(
            HasProperty(body, "departmentId"),
            HasProperty(body, "departmentName"),
            HasProperty(body, "groupId"),
            HasProperty(body, "groupName"));

    private static bool HasProperty(JsonElement body, string propertyName)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in body.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal readonly record struct MemberTermOrganizationPatchPlan(
    bool OrganizationRequested,
    int? DepartmentId,
    string? DepartmentName,
    int? GroupId,
    string? GroupName,
    int? PreservedGroupId,
    string? PreservedGroupName,
    string? ValidationDepartmentName,
    string? ValidationGroupName);

internal static class MemberTermOrganizationPatchPlanner
{
    public static MemberTermOrganizationPatchPlan Plan(
        MemberTermOrganizationPatchFields fields,
        int? requestDepartmentId,
        string? requestDepartmentName,
        int? requestGroupId,
        string? requestGroupName,
        int? currentDepartmentId,
        string? currentDepartmentName,
        int? currentGroupId,
        string? currentGroupName)
    {
        var requestedDepartmentName = EmptyToNull(requestDepartmentName);
        var requestedGroupName = EmptyToNull(requestGroupName);
        var departmentSelectionRequested = requestDepartmentId is not null || requestedDepartmentName is not null;
        var groupSelectionRequested = requestGroupId is not null || requestedGroupName is not null;
        var shouldClearDepartment = fields.DepartmentRequested && !departmentSelectionRequested;
        var shouldClearGroup = fields.GroupRequested && !groupSelectionRequested;
        var shouldPreserveExistingGroup = departmentSelectionRequested && !fields.GroupRequested;

        var resolutionDepartmentId = requestDepartmentId;
        var resolutionDepartmentName = requestDepartmentName;
        if (!shouldClearDepartment && shouldClearGroup && !departmentSelectionRequested)
        {
            resolutionDepartmentId = currentDepartmentId;
            resolutionDepartmentName = currentDepartmentName;
        }

        return new MemberTermOrganizationPatchPlan(
            fields.OrganizationRequested,
            resolutionDepartmentId,
            resolutionDepartmentName,
            requestGroupId,
            requestGroupName,
            shouldPreserveExistingGroup ? currentGroupId : null,
            shouldPreserveExistingGroup ? currentGroupName : null,
            fields.OrganizationRequested ? (shouldClearDepartment ? null : resolutionDepartmentName) : currentDepartmentName,
            fields.OrganizationRequested
                ? shouldPreserveExistingGroup ? currentGroupName : shouldClearGroup ? null : requestGroupName
                : currentGroupName);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
