using System.Text.Json;
using ClubHub.Api.Controllers;

namespace ClubHub.Api.Tests;

public class MemberTermOrganizationPatchPlannerTests
{
    [Fact]
    public void Plan_DoesNotRequestOrganization_WhenOrganizationFieldsAreOmitted()
    {
        using var document = JsonDocument.Parse("{\"positionName\":\"Member\"}");

        var fields = MemberTermOrganizationPatchFields.FromJson(document.RootElement);
        var plan = MemberTermOrganizationPatchPlanner.Plan(
            fields,
            requestDepartmentId: null,
            requestDepartmentName: null,
            requestGroupId: null,
            requestGroupName: null,
            currentDepartmentId: 10,
            currentDepartmentName: "Department A",
            currentGroupId: 20,
            currentGroupName: "Group A");

        Assert.False(plan.OrganizationRequested);
        Assert.Equal("Department A", plan.ValidationDepartmentName);
        Assert.Equal("Group A", plan.ValidationGroupName);
    }

    [Fact]
    public void Plan_PreservesCurrentGroup_WhenOnlyDepartmentIsChanged()
    {
        using var document = JsonDocument.Parse("{\"departmentId\":11}");

        var fields = MemberTermOrganizationPatchFields.FromJson(document.RootElement);
        var plan = MemberTermOrganizationPatchPlanner.Plan(
            fields,
            requestDepartmentId: 11,
            requestDepartmentName: null,
            requestGroupId: null,
            requestGroupName: null,
            currentDepartmentId: 10,
            currentDepartmentName: "Department A",
            currentGroupId: 20,
            currentGroupName: "Group A");

        Assert.True(plan.OrganizationRequested);
        Assert.Equal(11, plan.DepartmentId);
        Assert.Equal(20, plan.PreservedGroupId);
        Assert.Equal("Group A", plan.PreservedGroupName);
        Assert.Equal("Group A", plan.ValidationGroupName);
    }

    [Fact]
    public void Plan_ClearsGroupButKeepsDepartment_WhenGroupIdIsExplicitNull()
    {
        using var document = JsonDocument.Parse("{\"groupId\":null}");

        var fields = MemberTermOrganizationPatchFields.FromJson(document.RootElement);
        var plan = MemberTermOrganizationPatchPlanner.Plan(
            fields,
            requestDepartmentId: null,
            requestDepartmentName: null,
            requestGroupId: null,
            requestGroupName: null,
            currentDepartmentId: 10,
            currentDepartmentName: "Department A",
            currentGroupId: 20,
            currentGroupName: "Group A");

        Assert.True(plan.OrganizationRequested);
        Assert.Equal(10, plan.DepartmentId);
        Assert.Equal("Department A", plan.DepartmentName);
        Assert.Null(plan.GroupId);
        Assert.Null(plan.GroupName);
        Assert.Null(plan.PreservedGroupId);
        Assert.Null(plan.PreservedGroupName);
        Assert.Equal("Department A", plan.ValidationDepartmentName);
        Assert.Null(plan.ValidationGroupName);
    }

    [Fact]
    public void Plan_ClearsDepartmentAndGroup_WhenDepartmentIdIsExplicitNull()
    {
        using var document = JsonDocument.Parse("{\"departmentId\":null}");

        var fields = MemberTermOrganizationPatchFields.FromJson(document.RootElement);
        var plan = MemberTermOrganizationPatchPlanner.Plan(
            fields,
            requestDepartmentId: null,
            requestDepartmentName: null,
            requestGroupId: null,
            requestGroupName: null,
            currentDepartmentId: 10,
            currentDepartmentName: "Department A",
            currentGroupId: 20,
            currentGroupName: "Group A");

        Assert.True(plan.OrganizationRequested);
        Assert.Null(plan.DepartmentId);
        Assert.Null(plan.DepartmentName);
        Assert.Null(plan.GroupId);
        Assert.Null(plan.GroupName);
        Assert.Null(plan.PreservedGroupId);
        Assert.Null(plan.PreservedGroupName);
        Assert.Null(plan.ValidationDepartmentName);
        Assert.Null(plan.ValidationGroupName);
    }
}
