using System;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public class ActionSprintWorkflowPolicy
{
    // SECTION: Sprint date validation
    public void ValidateDateRange(DateTime startDate, DateTime endDate)
    {
        if (startDate.Date > endDate.Date)
        {
            throw new InvalidOperationException("Planning Window start date must be on or before the end date.");
        }
    }

    // SECTION: Sprint lifecycle validation
    public void EnsureCanUpdate(ActionSprint sprint)
    {
        if (sprint.Status == ActionSprintStatus.Closed)
        {
            throw new InvalidOperationException("Closed Planning Windows are immutable in the normal workflow.");
        }
    }

    public void EnsureCanActivate(ActionSprint sprint)
    {
        if (sprint.Status != ActionSprintStatus.Planned)
        {
            throw new InvalidOperationException("Only planned Planning Windows can be activated.");
        }
    }

    public void EnsureCanClose(ActionSprint sprint)
    {
        if (sprint.Status != ActionSprintStatus.Active)
        {
            throw new InvalidOperationException("Only active Planning Windows can be closed.");
        }
    }

    public void EnsureCanAcceptTask(ActionSprint sprint)
    {
        if (sprint.Status == ActionSprintStatus.Closed)
        {
            throw new InvalidOperationException("Tasks cannot be assigned to a closed Planning Window.");
        }
    }

    public void EnsureCanCarryForward(ActionSprint sourceSprint, ActionSprint targetSprint)
    {
        EnsureCanClose(sourceSprint);
        if (targetSprint.Id == sourceSprint.Id)
        {
            throw new InvalidOperationException("Carry-forward target Planning Window must be different from the Planning Window being closed.");
        }

        if (targetSprint.StartDate.Date <= sourceSprint.EndDate.Date)
        {
            throw new InvalidOperationException("Carry-forward target Planning Window must start after the Planning Window being closed ends.");
        }

        if (targetSprint.Status == ActionSprintStatus.Closed)
        {
            throw new InvalidOperationException("Tasks cannot be carried forward into a closed Planning Window.");
        }
    }
}
