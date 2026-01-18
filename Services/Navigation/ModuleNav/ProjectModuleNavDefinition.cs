using System.Collections.Generic;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Navigation;

namespace ProjectManagement.Services.Navigation.ModuleNav;

public static class ProjectModuleNavDefinition
{
    // ===========================
    // PROJECT MODULE CHILDREN
    // ===========================
    public static IReadOnlyList<NavigationItem> Build() => new List<NavigationItem>
    {
        new()
        {
            Text = "Projects repository",
            Page = "/Projects/Index",
            Icon = "bi-collection"
        },
        new()
        {
            Text = "Ongoing projects",
            Page = "/Projects/Ongoing/Index",
            Icon = "bi-clock-history"
        },
        new()
        {
            Text = "Completed projects summary",
            Page = "/Projects/CompletedSummary/Index",
            Icon = "bi-clipboard-data"
        },
        new()
        {
            Text = "Process",
            Page = "/Process/Index",
            Icon = "bi-diagram-3"
        },
        new()
        {
            Text = "Analytics",
            Page = "/Analytics/Index",
            Icon = "bi-graph-up"
        },
        new()
        {
            Text = "Create",
            Page = "/Projects/Create",
            Icon = "bi-plus-circle",
            AuthorizationPolicy = "Project.Create"
        },

        new()
        {
            Text = "Pending approvals",
            Page = "/Approvals/Pending/Index",
            RequiredRoles = new[] { RoleNames.Admin, RoleNames.HoD },
            BadgeViewComponentName = "PendingApprovalsBadge",
            Icon = "bi-check2-square"
        }
    };
}
