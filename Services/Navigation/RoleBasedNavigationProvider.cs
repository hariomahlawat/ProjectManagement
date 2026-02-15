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

        // ===========================
        // PRIMARY NAVIGATION ITEMS
        // ===========================
        var items = new List<NavigationItem>
        {
            new()
            {
                Text = "Calendar",
                Page = "/Calendar/Index",
                Icon = "bi-calendar3"
            },
            new()
            {
                Text = "Dashboard",
                Page = "/Dashboard/Index",
                Icon = "bi-speedometer2"
            },
            new()
            {
                Text = "Miscellaneous activities",
                Page = "/Activities/Index",
                Icon = "bi-stars"
            },
            new()
            {
                Text = "Proliferation compendium",
                Page = "/Projects/Compendium/Index",
                Icon = "bi-file-earmark-pdf"
            },
            // Progress review entry (drawer only).
            new()
            {
                Text = "Progress review",
                Area = "ProjectOfficeReports",
                Page = "/ProgressReview/Index",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewProgressReview,
                Icon = "bi-graph-up"
            }
        };
        // ===========================
        // PROJECT MODULE NAVIGATION
        // ===========================
        var projectModuleChildren = ProjectModuleNavDefinition.Build();

        items.Add(new NavigationItem
        {
            Text = "Projects",
            Children = projectModuleChildren,
            Icon = "bi-kanban"
        });


        var documentRepositoryChildren = new List<NavigationItem>
        {
            new()
            {
                Text = "Document repository",
                Area = "DocumentRepository",
                Page = "/Documents/Index",
                AuthorizationPolicy = "DocRepo.View",
                Icon = "bi-folder2-open"
            },
            new()
            {
                Text = "Upload document",
                Area = "DocumentRepository",
                Page = "/Documents/Upload",
                AuthorizationPolicy = "DocRepo.Upload",
                Icon = "bi-cloud-upload"
            },
            new()
            {
                Text = "Delete requests",
                Area = "DocumentRepository",
                Page = "/Admin/DeleteRequests/Index",
                AuthorizationPolicy = "DocRepo.DeleteApprove",
                Icon = "bi-shield-x"
            },
            new()
            {
                Text = "Trash",
                Area = "DocumentRepository",
                Page = "/Admin/Trash/Index",
                AuthorizationPolicy = "DocRepo.Purge",
                Icon = "bi-trash3"
            },
            new()
            {
                Text = "OCR failures",
                Area = "DocumentRepository",
                Page = "/Admin/OCRFailures/OcrFailures",
                AuthorizationPolicy = "DocRepo.DeleteApprove",
                Icon = "bi-bug"
            },
            new()
            {
                Text = "Office categories",
                Area = "DocumentRepository",
                Page = "/Admin/OfficeCategories/Index",
                RequiredRoles = new[] { RoleNames.Admin },
                Icon = "bi-buildings"
            },
            new()
            {
                Text = "Document categories",
                Area = "DocumentRepository",
                Page = "/Admin/DocumentCategories/Index",
                RequiredRoles = new[] { RoleNames.Admin },
                Icon = "bi-bookmarks"
            }
        };

        items.Add(new NavigationItem
        {
            Text = "Documents",
            Children = documentRepositoryChildren,
            Icon = "bi-folder-check"
        });

        var projectOfficeReportsChildren = new List<NavigationItem>
        {
            // Visits
                new()
                {
                    Text = "Visits tracker",
                    Area = "ProjectOfficeReports",
                    Page = "/Visits/Index",
                    AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewVisits,
                    Icon = "bi-pin-map",
                    Children = new[]
                    {
                        new NavigationItem
                        {
                            Text = "Visit types",
                            Area = "ProjectOfficeReports",
                            Page = "/VisitTypes/Index",
                            RequiredRoles = new[] { RoleNames.Admin },
                            Icon = "bi-geo"
                        }
                    }
                },

            // >>> THIS is the part we changed <<<
                new()
                {
                    Text = "Social media tracker",
                    Area = "ProjectOfficeReports",
                    Page = "/SocialMedia/Index",
                    AuthorizationPolicy = ProjectOfficeReportsPolicies.ManageSocialMediaEvents,
                    Icon = "bi-hash",
                    Children = new[]
                    {
                        new NavigationItem
                        {
                            Text = "Social media event types",
                            Area = "ProjectOfficeReports",
                            Page = "/Admin/SocialMediaTypes/Index",
                            RequiredRoles = new[] { RoleNames.Admin },
                            Icon = "bi-calendar-event"
                        },
                        new NavigationItem
                        {
                            Text = "Social media platforms",
                            Area = "ProjectOfficeReports",
                            Page = "/Admin/SocialMediaTypes/Platforms/Index",
                            RequiredRoles = new[] { RoleNames.Admin },
                            Icon = "bi-ui-radios-grid"
                        }
                    }
                },
            new()
            {
                Text = "Training tracker",
                Area = "ProjectOfficeReports",
                Page = "/Training/Index",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewTrainingTracker,
                BadgeViewComponentName = "TrainingApprovalsBadge",
                Icon = "bi-award"
            },
            new()
            {
                Text = "ToT tracker",
                Area = "ProjectOfficeReports",
                Page = "/Tot/Summary",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewTotTracker,
                Icon = "bi-diagram-3"
            },
            new()
            {
                Text = "Proliferation tracker",
                Area = "ProjectOfficeReports",
                Page = "/Proliferation/Summary",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewProliferationTracker,
                Icon = "bi-radar"
            },
            new()
            {
                Text = "Patent tracker",
                Area = "ProjectOfficeReports",
                Page = "/Ipr/Index",
                AuthorizationPolicy = Policies.Ipr.View,
                Icon = "bi-lightbulb"
            },
            new()
            {
                Text = "FFC simulators",
                Area = "ProjectOfficeReports",
                Page = "/FFC/Index",
                Icon = "bi-cpu"
            }
        };

        items.Add(new NavigationItem
        {
            Text = "Project office reports",
            Area = "ProjectOfficeReports",
            Page = "/Index",
            Children = projectOfficeReportsChildren,
            Icon = "bi-clipboard-data"
        });

        var activityTypesNavigationItem = new NavigationItem
        {
            Text = "Activity types",
            Area = "Admin",
            Page = "/ActivityTypes/Index",
            RequiredRoles = new[] { "Admin", "HoD" },
            Icon = "bi-columns-gap"
        };

        if (roleSet.Contains("Admin"))
        {
            items.Add(new NavigationItem
            {
                Text = "Admin Panel",
                Area = "Admin",
                Page = "/Index",
                RequiredRoles = new[] { "Admin" },
                Icon = "bi-shield-lock",
                Accent = "danger",
                Children = new[]
                {
                    new NavigationItem
                    {
                        Text = "Manage users",
                        Area = "Admin",
                        Page = "/Users/Index",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-people"
                    },
                    new NavigationItem
                    {
                        Text = "Login scatter",
                        Area = "Admin",
                        Page = "/Analytics/Logins",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-graph-up-arrow"
                    },
                    new NavigationItem
                    {
                        Text = "Logs",
                        Area = "Admin",
                        Page = "/Logs/Index",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-list-check"
                    },
                    new NavigationItem
                    {
                        Text = "DB health",
                        Area = "Admin",
                        Page = "/Diagnostics/DbHealth",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-heart-pulse"
                    },
                    new NavigationItem
                    {
                        Text = "Sponsoring units",
                        Area = "Admin",
                        Page = "/Lookups/SponsoringUnits/Index",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-diagram-2"
                    },
                    new NavigationItem
                    {
                        Text = "Line directorates",
                        Area = "Admin",
                        Page = "/Lookups/LineDirectorates/Index",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-diagram-3"
                    },
                    new NavigationItem
                    {
                        Text = "Project categories",
                        Area = "Admin",
                        Page = "/Categories/Index",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-grid"
                    },
                    activityTypesNavigationItem,
                    new NavigationItem
                    {
                        Text = "Technical categories",
                        Area = "Admin",
                        Page = "/TechnicalCategories/Index",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-sliders2"
                    },
                    new NavigationItem
                    {
                        Text = "Project trash",
                        Area = "Admin",
                        Page = "/Projects/Trash",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-archive"
                    },
                    new NavigationItem
                    {
                        Text = "Projects – Legacy import",
                        Area = "ProjectOfficeReports",
                        Page = "/Projects/LegacyImport",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-box-arrow-in-down"
                    },
                    new NavigationItem
                    {
                        Text = "Archived projects",
                        Page = "/Projects/Index",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-inboxes",
                        RouteValues = new Dictionary<string, object?>
                        {
                            ["IncludeArchived"] = true
                        }
                    },
                    new NavigationItem
                    {
                        Text = "Document recycle bin",
                        Area = "Admin",
                        Page = "/Documents/Recycle",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-recycle"
                    },
                    new NavigationItem
                    {
                        Text = "Calendar deleted events",
                        Area = "Admin",
                        Page = "/Calendar/Deleted",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-calendar-x"
                    },
                    new NavigationItem
                    {
                        Text = "Manage holidays",
                        Page = "/Settings/Holidays/Index",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-sun"
                    },
                    new NavigationItem
                    {
                        Text = "Manage celebrations",
                        Page = "/Celebrations/Index",
                        RequiredRoles = new[] { "Admin" },
                        Icon = "bi-balloon"
                    }
                }
            });
        }

        if (roleSet.Contains("HoD") && !roleSet.Contains("Admin"))
        {
            items.Add(activityTypesNavigationItem);
        }

        return await TrimNavigationTreeAsync(items, httpContext.User, roleSet);
    }

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
            var trimmedItem = item with
            {
                Children = trimmedChildren
            };

            result.Add(trimmedItem);
        }

        return result;
    }

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
