using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Services.Navigation;

public class RoleBasedNavigationProvider : INavigationProvider
{
    private static readonly IReadOnlyList<NavigationItem> AnonymousNavigation = Array.Empty<NavigationItem>();


    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;
    public RoleBasedNavigationProvider(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    public async Task<IReadOnlyList<NavigationItem>> GetNavigationAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return AnonymousNavigation;
        }

        var user = await _userManager.GetUserAsync(httpContext.User);
        if (user is null)
        {
            return AnonymousNavigation;
        }

        var roleSet = await GetRoleSetAsync(user);
        var isAdmin = roleSet.Contains(RoleNames.Admin);
        var isHodWithoutAdmin = roleSet.Contains(RoleNames.HoD) && !isAdmin;

        // The drawer lists PRISM's areas in fixed sections (docs/ux/navigation-drawer.md):
        // whole areas, not a module's internal pages and not actions. Every link keeps the
        // policy or role its destination enforces; hiding a link is never the authorization.
        var sections = new List<NavigationItem>
        {
            Section(NavigationSectionKeys.Main, "Main", BuildMainItems()),
            Section(NavigationSectionKeys.Projects, "Projects", BuildProjectItems()),
            Section(NavigationSectionKeys.Trackers, "Trackers", BuildTrackerItems(isHodWithoutAdmin)),
            Section(NavigationSectionKeys.Tools, "Tools", BuildToolItems())
        };

        if (isAdmin)
        {
            sections.Add(Section(NavigationSectionKeys.Administration, "Administration", BuildAdministrationItems()) with
            {
                AuthorizationPolicy = AdminPolicies.Access
            });
        }

        return await TrimNavigationTreeAsync(sections, httpContext.User, roleSet);
    }

    private static NavigationItem Section(string key, string text, IReadOnlyList<NavigationItem> children) => new()
    {
        Key = key,
        Text = text,
        Children = children
    };

    // ===========================
    // MAIN: the top-bar areas, in the top bar's order. Below 768px the top tabs are
    // hidden and the drawer is the only primary navigation, so it must hold all of them.
    // ===========================
    private static IReadOnlyList<NavigationItem> BuildMainItems() => new NavigationItem[]
    {
        new()
        {
            Text = "Dashboard",
            Page = "/Dashboard/Index",
            Icon = "bi-speedometer2"
        },
        new()
        {
            Text = "My Workspace",
            Page = "/Workspace/Index",
            ActivePagePrefix = "/Workspace/",
            Icon = "bi-person-workspace"
        },
        new()
        {
            Text = "Calendar",
            Page = "/Calendar/Index",
            ActivePagePrefix = "/Calendar/",
            Icon = "bi-calendar3"
        },
        new()
        {
            Text = "Notebook",
            Page = "/Notebook/Index",
            ActivePagePrefix = "/Notebook/",
            Icon = "bi-journal-bookmark"
        },
        new()
        {
            Text = "Photos",
            Page = "/Photos/Index",
            ActivePagePrefix = "/Photos/",
            Icon = "bi-images"
        },
        new()
        {
            Text = "FFC",
            Area = "ProjectOfficeReports",
            Page = "/FFC/MapTableDetailed",
            ActivePagePrefix = "/FFC/",
            Icon = "bi-globe2"
        },
        new()
        {
            Text = "Documents",
            Area = "DocumentRepository",
            Page = "/Documents/Index",
            ActivePagePrefix = "/Documents/",
            AuthorizationPolicy = Policies.Documents.View,
            BadgeViewComponentName = "AotsUnreadBadge",
            BadgeViewComponentParameters = new { variant = "default" },
            Icon = "bi-folder2-open"
        }
    };

    // ===========================
    // PROJECTS: the Projects sub-nav's destinations in its order (actions such as
    // Create project stay on the sub-nav), plus project pages with no tab of their own.
    // ProjectModuleNavDefinition is shared with the sub-nav, so the extras live here.
    // ===========================
    private static IReadOnlyList<NavigationItem> BuildProjectItems()
    {
        var moduleItems = ProjectModuleNavDefinition.Build()
            .Where(item => !item.IsAction)
            .ToList();

        var extras = new NavigationItem[]
        {
            new()
            {
                Text = "Proliferation compendium",
                Page = "/Projects/Compendium/Index",
                ActivePagePrefix = "/Projects/Compendium/",
                Icon = "bi-book"
            },
            new()
            {
                Text = "Project ideas",
                Page = "/ProjectIdeas/Index",
                ActivePagePrefix = "/ProjectIdeas/",
                Icon = "bi-lightbulb"
            },
            new()
            {
                Text = "IT projects",
                Page = "/ItProjects/Index",
                ActivePagePrefix = "/ItProjects/",
                AuthorizationPolicy = Policies.ItProjects.View,
                Icon = "bi-pc-display"
            }
        };

        // Keep Pending approvals (a queue with a badge) last.
        var pendingIndex = moduleItems.FindIndex(item => item.Page == "/Approvals/Pending/Index");
        moduleItems.InsertRange(pendingIndex < 0 ? moduleItems.Count : pendingIndex, extras);
        return moduleItems;
    }

    // ===========================
    // TRACKERS: registers and trackers kept by the project office and officers.
    // Their settings pages (visit types, social media lists) open from the trackers
    // themselves and from Administration, so they are not listed here.
    // ===========================
    private static IReadOnlyList<NavigationItem> BuildTrackerItems(bool includeActivityTypes)
    {
        var items = new List<NavigationItem>
        {
            new()
            {
                Text = "Task management",
                Page = "/ActionTasks/Index",
                ActivePagePrefix = "/ActionTasks/",
                AuthorizationPolicy = Policies.ActionTracker.Access,
                Icon = "bi-list-task"
            },
            new()
            {
                Text = "Progress review",
                Area = "ProjectOfficeReports",
                Page = "/ProgressReview/Index",
                ActivePagePrefix = "/ProgressReview/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewProgressReview,
                Icon = "bi-bar-chart-line"
            },
            new()
            {
                Text = "Visits",
                Area = "ProjectOfficeReports",
                Page = "/Visits/Index",
                ActivePagePrefix = "/Visits/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewVisits,
                Icon = "bi-pin-map"
            },
            new()
            {
                Text = "Social media",
                Area = "ProjectOfficeReports",
                Page = "/SocialMedia/Index",
                ActivePagePrefix = "/SocialMedia/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ManageSocialMediaEvents,
                Icon = "bi-hash"
            },
            new()
            {
                Text = "Training",
                Area = "ProjectOfficeReports",
                Page = "/Training/Index",
                ActivePagePrefix = "/Training/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewTrainingTracker,
                BadgeViewComponentName = "TrainingApprovalsBadge",
                Icon = "bi-award"
            },
            new()
            {
                Text = "ToT",
                Area = "ProjectOfficeReports",
                Page = "/Tot/Index",
                ActivePagePrefix = "/Tot/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewTotTracker,
                Icon = "bi-arrow-left-right"
            },
            new()
            {
                Text = "Proliferation",
                Area = "ProjectOfficeReports",
                Page = "/Proliferation/Summary",
                ActivePagePrefix = "/Proliferation/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewProliferationTracker,
                Icon = "bi-radar"
            },
            new()
            {
                Text = "IPR register",
                Area = "ProjectOfficeReports",
                Page = "/Ipr/Index",
                ActivePagePrefix = "/Ipr/",
                AuthorizationPolicy = Policies.Ipr.View,
                Icon = "bi-patch-check"
            },
            new()
            {
                Text = "Activities",
                Page = "/Activities/Index",
                ActivePagePrefix = "/Activities/",
                Icon = "bi-stars"
            },
            new()
            {
                // Distinct from the Projects section's "ARPP / PPP" library.
                Text = "ARPP administration",
                Area = "ProjectOfficeReports",
                Page = "/ARPP/Index",
                ActivePagePrefix = "/ARPP/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewArpp,
                Icon = "bi-sliders"
            }
        };

        // Heads of department manage activity types without the Administration section.
        if (includeActivityTypes)
        {
            items.Add(AdminNavigationCatalog.ActivityTypesItem());
        }

        return items;
    }

    private static IReadOnlyList<NavigationItem> BuildToolItems() => new NavigationItem[]
    {
        // Standalone tool: users edit their own PDFs in the browser; nothing is stored in PRISM.
        new()
        {
            Text = "PDF Tools",
            Page = "/Tools/Pdf/Index",
            Icon = "bi-file-earmark-pdf",
            OpensInNewTab = true
        }
    };

    // ===========================
    // ADMINISTRATION: a few entry points only. Administration has its own sidebar,
    // sub-nav and "Every admin page" index; the catalog stays the source of names,
    // destinations and policies.
    // ===========================
    internal static readonly IReadOnlyList<string> AdministrationDrawerKeys = new[]
    {
        AdminNavigationKeys.Dashboard,
        AdminNavigationKeys.Users,
        AdminNavigationKeys.Logs,
        AdminNavigationKeys.DatabaseHealth,
        AdminNavigationKeys.AllPages
    };

    private static IReadOnlyList<NavigationItem> BuildAdministrationItems() =>
        AdministrationDrawerKeys
            .Select(AdminNavigationCatalog.GetEntry)
            .Select(entry => entry.Match.ExactPage
                ? entry.Item
                : entry.Item with { ActivePagePrefix = entry.Match.PagePattern })
            .ToList();

    private async Task<HashSet<string>> GetRoleSetAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return roles.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<NavigationItem>> TrimNavigationTreeAsync(
        IReadOnlyList<NavigationItem> items,
        System.Security.Claims.ClaimsPrincipal user,
        HashSet<string> roleSet)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var result = new List<NavigationItem>(items.Count);

        foreach (var item in items)
        {
            if (!await IsAuthorizedAsync(item, user, roleSet))
            {
                continue;
            }

            var trimmedChildren = await TrimNavigationTreeAsync(item.Children, user, roleSet);

            // A section heading with no destination of its own is only useful while the
            // user can open at least one of its children; otherwise it renders as a dead label.
            if (item.Children.Count > 0 && trimmedChildren.Count == 0 && !HasDestination(item))
            {
                continue;
            }

            var trimmedItem = item with
            {
                Children = trimmedChildren
            };

            result.Add(trimmedItem);
        }

        return result;
    }

    private static bool HasDestination(NavigationItem item) =>
        !string.IsNullOrEmpty(item.Page)
        || (!string.IsNullOrEmpty(item.Controller) && !string.IsNullOrEmpty(item.Action));

    private async Task<bool> IsAuthorizedAsync(
        NavigationItem item,
        System.Security.Claims.ClaimsPrincipal user,
        HashSet<string> roleSet)
    {
        if (item.RequiredRoles is { Count: > 0 })
        {
            if (!item.RequiredRoles.Any(roleSet.Contains))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(item.AuthorizationPolicy))
        {
            var authorizationResult = await _authorizationService.AuthorizeAsync(user, null, item.AuthorizationPolicy);
            if (!authorizationResult.Succeeded)
            {
                return false;
            }
        }

        return true;
    }
}
