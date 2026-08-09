using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskInteractionCapabilitiesTests
{
    [Fact]
    public void AssignedUser_GetsDirectOwnerWorkflowActions()
    {
        var workflow = new ActionTaskWorkflowPolicy(new ActionTaskPermissionService());
        var task = new ActionTaskItem
        {
            Status = ActionTaskStatuses.InProgress,
            AssignedToUserId = "owner"
        };

        var capabilities = workflow.GetInteractionCapabilities(task, RoleNames.ProjectOfficer, "owner");

        Assert.True(capabilities.IsAssignedUser);
        Assert.True(capabilities.CanSubmitForClosure);
        Assert.True(capabilities.CanBlockAsOwner);
        Assert.False(capabilities.CanBlockAsCommandControl);
        Assert.False(capabilities.CanCloseDirectly);
    }

    [Fact]
    public void CommandReviewer_DoesNotReceiveOwnersBlockActionAsPrimaryAction()
    {
        var workflow = new ActionTaskWorkflowPolicy(new ActionTaskPermissionService());
        var task = new ActionTaskItem
        {
            Status = ActionTaskStatuses.InProgress,
            AssignedToUserId = "owner"
        };

        var capabilities = workflow.GetInteractionCapabilities(task, RoleNames.Comdt, "command");

        Assert.False(capabilities.IsAssignedUser);
        Assert.False(capabilities.CanBlockAsOwner);
        Assert.True(capabilities.CanBlockAsCommandControl);
        Assert.True(capabilities.CanChangeDate);
        Assert.True(capabilities.CanCloseDirectly);
        Assert.True(capabilities.CanAddConferenceRemark);
    }

    [Fact]
    public void SubmittedTask_ExposesReviewActionsAndNotDirectCloseOverride()
    {
        var workflow = new ActionTaskWorkflowPolicy(new ActionTaskPermissionService());
        var task = new ActionTaskItem
        {
            Status = ActionTaskStatuses.Submitted,
            AssignedToUserId = "owner"
        };

        var capabilities = workflow.GetInteractionCapabilities(task, RoleNames.HoD, "hod");

        Assert.True(capabilities.CanAcceptAndClose);
        Assert.True(capabilities.CanReturnForAction);
        Assert.False(capabilities.CanCloseDirectly);
    }
}
