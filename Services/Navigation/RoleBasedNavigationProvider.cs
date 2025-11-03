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

        var items = new List<NavigationItem>
        {
            new()
            {
                Text = "Projects",
                Page = "/Projects/Index"
            },
            new()
            {
                Text = "Process",
                Page = "/Process/Index"
            },
            new()
            {
                Text = "Calendar",
                Page = "/Calendar/Index"
            },
            new()
            {
                Text = "Dashboard",
                Page = "/Dashboard/Index"
            },
            new()
            {
                Text = "Analytics",
                Page = "/Analytics/Index"
            },
            new()
            {
                Text = "Miscellaneous activities",
                Page = "/Activities/Index"
            }
        };

        var documentRepositoryChildren = new List<NavigationItem>
        {
            new()
            {
                Text = "Document repository",
                Area = "DocumentRepository",
                Page = "/Documents/Index",
                AuthorizationPolicy = "DocRepo.View"
            },
            new()
    {
        Text = "Upload document",
        Area = "DocumentRepository",
        Page = "/Documents/Upload",
        AuthorizationPolicy = "DocRepo.Upload"
    },
            new()
            {
                Text = "Delete requests",
                Area = "DocumentRepository",
                Page = "/Admin/DeleteRequests/Index",
                AuthorizationPolicy = "DocRepo.DeleteApprove"
            },
            new()
            {
                Text = "Office categories",
                Area = "DocumentRepository",
                Page = "/Admin/OfficeCategories/Index",
                RequiredRoles = new[] { RoleNames.Admin }
            },
            new()
            {
                Text = "Document categories",
                Area = "DocumentRepository",
                Page = "/Admin/DocumentCategories/Index",
                RequiredRoles = new[] { RoleNames.Admin }
            }
        };

        items.Add(new NavigationItem
        {
            Text = "Documents",
            Children = documentRepositoryChildren
        });

        var projectOfficeReportsChildren = new List<NavigationItem>
        {
            new()
            {
                Text = "Visits tracker",
                Area = "ProjectOfficeReports",
                Page = "/Visits/Index",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewVisits
            },
            new()
            {
                Text = "Social media tracker",
                Area = "ProjectOfficeReports",
                Page = "/SocialMedia/Index",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ManageSocialMediaEvents
            },
            new()
            {
                Text = "Training tracker",
                Area = "ProjectOfficeReports",
                Page = "/Training/Index",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewTrainingTracker,
                BadgeViewComponentName = "TrainingApprovalsBadge"
            },
            new()
            {
                Text = "ToT tracker",
                Area = "ProjectOfficeReports",
                Page = "/Tot/Index",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewTotTracker
            },
            new()
            {
                Text = "Proliferation tracker",
                Area = "ProjectOfficeReports",
                Page = "/Proliferation/Index",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewProliferationTracker
            },
            new()
            {
                Text = "Patent tracker",
                Area = "ProjectOfficeReports",
                Page = "/Ipr/Index",
                AuthorizationPolicy = Policies.Ipr.View
            },
            new()
            {
                Text = "FFC simulators",
                Area = "ProjectOfficeReports",
                Page = "/FFC/Index"
            }
        };

        items.Add(new NavigationItem
        {
            Text = "Project office reports",
            Area = "ProjectOfficeReports",
            Page = "/Index",
            Children = projectOfficeReportsChildren
        });

        var activityTypesNavigationItem = new NavigationItem
        {
            Text = "Activity types",
            Area = "Admin",
            Page = "/ActivityTypes/Index",
            RequiredRoles = new[] { "Admin", "HoD" }
        };

        if (roleSet.Contains("Admin"))
        {
            items.Add(new NavigationItem
            {
                Text = "Admin Panel",
                Area = "Admin",
                Page = "/Index",
                RequiredRoles = new[] { "Admin" },
                Children = new[]
                {
                    new NavigationItem
                    {
                        Text = "Manage users",
                        Area = "Admin",
                        Page = "/Users/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Login scatter",
                        Area = "Admin",
                        Page = "/Analytics/Logins",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Logs",
                        Area = "Admin",
                        Page = "/Logs/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "DB health",
                        Area = "Admin",
                        Page = "/Diagnostics/DbHealth",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Sponsoring units",
                        Area = "Admin",
                        Page = "/Lookups/SponsoringUnits/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Line directorates",
                        Area = "Admin",
                        Page = "/Lookups/LineDirectorates/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Project categories",
                        Area = "Admin",
                        Page = "/Categories/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    activityTypesNavigationItem,
                    new NavigationItem
                    {
                        Text = "Technical categories",
                        Area = "Admin",
                        Page = "/TechnicalCategories/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Project trash",
                        Area = "Admin",
                        Page = "/Projects/Trash",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Projects – Legacy import",
                        Area = "ProjectOfficeReports",
                        Page = "/Projects/LegacyImport",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Archived projects",
                        Page = "/Projects/Index",
                        RequiredRoles = new[] { "Admin" },
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
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Calendar deleted events",
                        Area = "Admin",
                        Page = "/Calendar/Deleted",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Manage holidays",
                        Page = "/Settings/Holidays/Index",
                        RequiredRoles = new[] { "Admin" }
                    },
                    new NavigationItem
                    {
                        Text = "Manage celebrations",
                        Page = "/Celebrations/Index",
                        RequiredRoles = new[] { "Admin" }
                    }
                }
            });
        }

        if (roleSet.Contains("HoD") && !roleSet.Contains("Admin"))
        {
            items.Add(activityTypesNavigationItem);
        }

        if (roleSet.Contains("HoD") || roleSet.Contains("Admin"))
        {
            items.Add(new NavigationItem
            {
                Text = "Approvals",
                Page = "/Projects/Documents/Approvals/Index",
                RequiredRoles = new[] { "HoD", "Admin" }
            });
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
