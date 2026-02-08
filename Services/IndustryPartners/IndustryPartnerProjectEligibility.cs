using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.IndustryPartners;

// SECTION: Joint Development Partner linking eligibility rules
public static class IndustryPartnerProjectEligibility
{
    // SECTION: Eligibility predicate aligned with Project Overview JDP panel logic
    public static bool IsEligibleForJdpLink(Project project, IEnumerable<ProjectStage> stages)
    {
        if (project.LifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return true;
        }

        var devpOrder = ProcurementWorkflow.OrderOf(project.WorkflowVersion, StageCodes.DEVP);
        var reachedMaxOrder = stages
            .Where(stage => stage.Status != StageStatus.NotStarted)
            .Select(stage => ProcurementWorkflow.OrderOf(project.WorkflowVersion, stage.StageCode))
            .Where(order => order != int.MaxValue)
            .DefaultIfEmpty(0)
            .Max();

        return reachedMaxOrder >= devpOrder;
    }
}
