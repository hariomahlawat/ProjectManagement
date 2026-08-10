using ProjectManagement.Configuration;
using ProjectManagement.Models;
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
    [InlineData(RoleNames.Admin, false)]
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
        var canChangeTaskDate = service.CanChangeTaskDate(role);
        var canCloseAssignedTaskDirectly = service.CanCloseTaskDirectly(new ActionTaskItem { Status = ActionTaskStatuses.Assigned }, role);
        var canCloseClosedTaskDirectly = service.CanCloseTaskDirectly(new ActionTaskItem { Status = ActionTaskStatuses.Closed }, role);

        // SECTION: Assert
        Assert.Equal(expected, canManageSprints);
        Assert.Equal(expected, canMoveTasksInSprint);
        Assert.Equal(expected, canCreateSprint);
        Assert.Equal(expected, canEditSprint);
        Assert.Equal(expected, canActivateSprint);
        Assert.Equal(expected, canCloseSprint);
        Assert.Equal(expected, canAssignTaskToSprint);
        Assert.Equal(expected, canMoveTaskToBacklog);
        Assert.Equal(expected, canChangeTaskDate);
        Assert.Equal(expected, canCloseAssignedTaskDirectly);
        Assert.False(canCloseClosedTaskDirectly);
    }


    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.Admin, false)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    [InlineData(RoleNames.Mco, false)]
    [InlineData(RoleNames.Ta, false)]
    [InlineData(RoleNames.Ito, false)]
    public void ReturnForActionPermission_IsLimitedToCommandRolesAndSubmittedTasks(string role, bool expected)
    {
        var service = new ActionTaskPermissionService();
        var submitted = new ActionTaskItem { Status = ActionTaskStatuses.Submitted };
        var inProgress = new ActionTaskItem { Status = ActionTaskStatuses.InProgress };

        Assert.Equal(expected, service.CanReturnTaskForAction(submitted, role));
        Assert.False(service.CanReturnTaskForAction(inProgress, role));
    }


    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.Admin, false)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    [InlineData(RoleNames.Mco, false)]
    [InlineData(RoleNames.Ta, false)]
    [InlineData(RoleNames.Ito, false)]
    public void ConferenceUpdatePermission_IsLimitedToCommandRoles(string role, bool expected)
    {
        var service = new ActionTaskPermissionService();

        Assert.Equal(expected, service.CanAddConferenceUpdate(role));
    }


    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.Admin, false)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    [InlineData(RoleNames.Mco, false)]
    [InlineData(RoleNames.Ta, false)]
    [InlineData(RoleNames.Ito, false)]
    public void OperationalMetadataManagement_IsLimitedToPlanningAuthorities(string role, bool expected)
    {
        var service = new ActionTaskPermissionService();
        var active = new ActionTaskItem { Status = ActionTaskStatuses.InProgress };

        Assert.Equal(expected, service.CanEditTaskDetails(role));
        Assert.Equal(expected, service.CanReassignTask(active, role));
        Assert.Equal(expected, service.CanChangeTaskPriority(active, role));
    }

    [Fact]
    public void GeneralRemark_AuthorMayMutateWithinThreeHours_ButNotAfterWindow()
    {
        var service = new ActionTaskPermissionService();
        var createdAt = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var update = new ActionTaskUpdate
        {
            UpdateType = ActionTaskUpdateTypes.Comment,
            CreatedByUserId = "owner",
            CreatedAtUtc = createdAt,
            IsDeleted = false
        };

        Assert.True(service.CanMutateTaskRemark(update, RoleNames.ProjectOfficer, "owner", createdAt.AddHours(2)));
        Assert.False(service.CanMutateTaskRemark(update, RoleNames.ProjectOfficer, "owner", createdAt.AddHours(4)));
        Assert.False(service.CanMutateTaskRemark(update, RoleNames.ProjectOfficer, "other", createdAt.AddHours(1)));
        Assert.True(service.CanMutateTaskRemark(update, RoleNames.HoD, "other", createdAt.AddDays(2)));
    }

    [Fact]
    public void ConferenceRemark_IsCommandGoverned_AndProgressIsImmutable()
    {
        var service = new ActionTaskPermissionService();
        var now = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var conference = new ActionTaskUpdate
        {
            UpdateType = ActionTaskUpdateTypes.Conference,
            CreatedByUserId = "owner",
            CreatedAtUtc = now.AddDays(-2)
        };
        var progress = new ActionTaskUpdate
        {
            UpdateType = ActionTaskUpdateTypes.Progress,
            CreatedByUserId = "owner",
            CreatedAtUtc = now
        };

        Assert.True(service.CanMutateTaskRemark(conference, RoleNames.Comdt, "command", now));
        Assert.True(service.CanMutateTaskRemark(conference, RoleNames.HoD, "hod", now));
        Assert.False(service.CanMutateTaskRemark(conference, RoleNames.ProjectOfficer, "owner", now));
        Assert.False(service.CanMutateTaskRemark(progress, RoleNames.Comdt, "command", now));
    }

}
