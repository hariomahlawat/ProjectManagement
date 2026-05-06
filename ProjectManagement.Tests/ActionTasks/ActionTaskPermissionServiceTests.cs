using ProjectManagement.Configuration;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskPermissionServiceTests
{
    [Fact]
    public void RoleVisibilityAndWritePermissions_AreEnforced()
    {
        // SECTION: Arrange
        var service = new ActionTaskPermissionService();

        // SECTION: Act
        var comdtCanViewAll = service.CanViewAll(RoleNames.Comdt);
        var hodCanViewAll = service.CanViewAll(RoleNames.HoD);
        var taCannotViewAll = service.CanViewAll(RoleNames.Ta);

        var ownerCanWrite = service.CanAddTaskUpdate(RoleNames.Ta, "owner", "owner");
        var nonOwnerCannotWrite = service.CanAddTaskUpdate(RoleNames.Ta, "other", "owner");

        // SECTION: Assert
        Assert.True(comdtCanViewAll);
        Assert.True(hodCanViewAll);
        Assert.False(taCannotViewAll);
        Assert.True(ownerCanWrite);
        Assert.False(nonOwnerCannotWrite);
    }
    [Theory]
    [InlineData(RoleNames.Comdt, RoleNames.HoD, true)]
    [InlineData(RoleNames.Comdt, RoleNames.ProjectOfficer, true)]
    [InlineData(RoleNames.Comdt, RoleNames.Mco, true)]
    [InlineData(RoleNames.Comdt, RoleNames.Ta, true)]
    [InlineData(RoleNames.Comdt, RoleNames.Ito, true)]
    [InlineData(RoleNames.HoD, RoleNames.HoD, false)]
    [InlineData(RoleNames.HoD, RoleNames.ProjectOfficer, true)]
    [InlineData(RoleNames.HoD, RoleNames.Mco, true)]
    [InlineData(RoleNames.HoD, RoleNames.Ta, true)]
    [InlineData(RoleNames.HoD, RoleNames.Ito, true)]
    [InlineData(RoleNames.HoD, RoleNames.Comdt, false)]
    [InlineData(RoleNames.ProjectOfficer, RoleNames.Ta, false)]
    [InlineData(RoleNames.Mco, RoleNames.Ta, false)]
    [InlineData(RoleNames.Ta, RoleNames.Ito, false)]
    [InlineData(RoleNames.Ito, RoleNames.Ta, false)]
    public void AssignmentMatrix_IsEnforcedExactly(string assignerRole, string assigneeRole, bool expected)
    {
        // SECTION: Arrange
        var service = new ActionTaskPermissionService();

        // SECTION: Act
        var canAssign = service.CanAssign(assignerRole, assigneeRole);

        // SECTION: Assert
        Assert.Equal(expected, canAssign);
    }

    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    [InlineData(RoleNames.Mco, false)]
    [InlineData(RoleNames.Ta, false)]
    [InlineData(RoleNames.Ito, false)]
    public void SprintManagementPermissions_AreLimitedToPlanningAuthorities(string role, bool expected)
    {
        // SECTION: Arrange
        var service = new ActionTaskPermissionService();

        // SECTION: Act
        var canManageSprints = service.CanManageSprints(role);
        var canMoveTasksInSprint = service.CanMoveTasksInSprint(role);
        var canCreateSprint = service.CanCreateSprint(role);
        var canEditSprint = service.CanEditSprint(role);
        var canActivateSprint = service.CanActivateSprint(role);
        var canCloseSprint = service.CanCloseSprint(role);
        var canAssignTaskToSprint = service.CanAssignTaskToSprint(role);
        var canMoveTaskToBacklog = service.CanMoveTaskToBacklog(role);

        // SECTION: Assert
        Assert.Equal(expected, canManageSprints);
        Assert.Equal(expected, canMoveTasksInSprint);
        Assert.Equal(expected, canCreateSprint);
        Assert.Equal(expected, canEditSprint);
        Assert.Equal(expected, canActivateSprint);
        Assert.Equal(expected, canCloseSprint);
        Assert.Equal(expected, canAssignTaskToSprint);
        Assert.Equal(expected, canMoveTaskToBacklog);
    }

}
