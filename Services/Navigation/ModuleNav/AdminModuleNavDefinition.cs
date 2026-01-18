using System.Collections.Generic;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Navigation;

namespace ProjectManagement.Services.Navigation.ModuleNav;

public static class AdminModuleNavDefinition
{
    // ===========================
    // ADMIN MODULE CHILDREN
    // ===========================
    public static IReadOnlyList<NavigationItem> Build() => new List<NavigationItem>
    {
        new()
        {
            Text = "Dashboard",
            Area = "Admin",
            Page = "/Index",
            Icon = "bi-speedometer2",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Users",
            Area = "Admin",
            Page = "/Users/Index",
            Icon = "bi-people",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Logins",
            Area = "Admin",
            Page = "/Analytics/Logins",
            Icon = "bi-graph-up",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Logs",
            Area = "Admin",
            Page = "/Logs/Index",
            Icon = "bi-journal-text",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "DB health",
            Area = "Admin",
            Page = "/Diagnostics/DbHealth",
            Icon = "bi-heart-pulse",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Project trash",
            Area = "Admin",
            Page = "/Projects/Trash",
            Icon = "bi-trash",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Recycle bin",
            Area = "Admin",
            Page = "/Documents/Recycle",
            Icon = "bi-recycle",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Ingest PDFs",
            Area = "Admin",
            Page = "/Documents/IngestExternalPdfs",
            Icon = "bi-upload",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Deleted events",
            Area = "Admin",
            Page = "/Calendar/Deleted",
            Icon = "bi-calendar-x",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Holidays",
            Page = "/Settings/Holidays/Index",
            Icon = "bi-calendar-week",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Celebrations",
            Page = "/Celebrations/Index",
            Icon = "bi-stars",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Project categories",
            Area = "Admin",
            Page = "/Categories/Index",
            Icon = "bi-diagram-3",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Technical categories",
            Area = "Admin",
            Page = "/TechnicalCategories/Index",
            Icon = "bi-cpu",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Activity types",
            Area = "Admin",
            Page = "/ActivityTypes/Index",
            Icon = "bi-list-task",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Project types",
            Area = "Admin",
            Page = "/Lookups/ProjectTypes/Index",
            Icon = "bi-tags",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Sponsoring units",
            Area = "Admin",
            Page = "/Lookups/SponsoringUnits/Index",
            Icon = "bi-building",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Line dirs",
            Area = "Admin",
            Page = "/Lookups/LineDirectorates/Index",
            Icon = "bi-diagram-2",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Legacy import",
            Area = "ProjectOfficeReports",
            Page = "/Projects/LegacyImport",
            Icon = "bi-database-up",
            RequiredRoles = new[] { RoleNames.Admin }
        },
        new()
        {
            Text = "Help",
            Area = "Admin",
            Page = "/Help",
            Icon = "bi-question-circle",
            RequiredRoles = new[] { RoleNames.Admin }
        }
    };
}
