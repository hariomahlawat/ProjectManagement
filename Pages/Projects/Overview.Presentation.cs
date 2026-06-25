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
            var planLocked = PlanEdit?.State?.IsLocked ?? false;
            return new ProjectOverviewAccessVm
            {
                IsAdmin = Roles.IsAdmin,
                IsHoD = Roles.IsHoD,
                IsAssignedProjectOfficer = Roles.IsAssignedProjectOfficer,
                IsAssignedHoD = Roles.IsAssignedHoD,
                CanEditTimeline = (Roles.IsAdmin || Roles.IsAssignedProjectOfficer || Roles.IsAssignedHoD) && !planLocked
            };
        }
    }

    public ProjectTimelinePanelVm TimelinePanel => new()
    {
        Timeline = Timeline,
        Access = Access
    };
}
