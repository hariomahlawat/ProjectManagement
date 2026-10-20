using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects;

public partial class OverviewModel
{
    public ProjectPortfolioPresentationVm Portfolio =>
        ProjectPortfolioPresentationVm.Create(Project, Timeline, HasBackfill);

    public ProjectOverviewAccessVm Access
    {
        get
        {
            return new ProjectOverviewAccessVm
            {
                IsAdmin = Roles.IsAdmin,
                IsHoD = Roles.IsHoD,
                IsAssignedProjectOfficer = Roles.IsAssignedProjectOfficer,
                CanEditTimeline = Roles.IsAdmin || Roles.IsHoD || Roles.IsAssignedProjectOfficer
            };
        }
    }

    public ProjectTimelinePanelVm TimelinePanel => new()
    {
        Timeline = Timeline,
        Access = Access,
        LifecycleStatus = Project?.LifecycleStatus ?? ProjectManagement.Models.ProjectLifecycleStatus.Active,
        IsLegacy = Project?.IsLegacy == true,
        CanManageHistoricalStageHistory = CanManageHistoricalStageHistory,
        CurrentUserId = CurrentUserId,
        CompletionDetailsSubmittedStages = Backfill.PendingProposal is { NeedsResubmission: false }
            ? Backfill.Stages
                .Where(stage => stage.HasProposal && stage.CompletedOn.HasValue)
                .Select(stage => stage.StageCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    };

    public bool CanManageHistoricalStageHistory =>
        Project is { IsLegacy: true, IsDeleted: false } project &&
        (project.LifecycleStatus is ProjectManagement.Models.ProjectLifecycleStatus.Completed
            or ProjectManagement.Models.ProjectLifecycleStatus.Cancelled) &&
        (Roles.IsAdmin || Roles.IsHoD);
}
