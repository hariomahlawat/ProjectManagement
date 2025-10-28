using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
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
    private readonly IOptionsMonitor<TrainingTrackerOptions> _trainingTrackerOptions;

    public RoleBasedNavigationProvider(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<TrainingTrackerOptions> trainingTrackerOptions)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
        _trainingTrackerOptions = trainingTrackerOptions;
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
            }
        };

        var trainingTrackerEnabled = _trainingTrackerOptions.CurrentValue?.Enabled ?? false;

        var projectOfficeReportsChildren = new List<NavigationItem>
        {
            new()
            {
                Text = "Visits",
                Area = "ProjectOfficeReports",
                Page = "/Visits/Index"
            },
            new()
            {
                Text = "ToT tracker",
                Area = "ProjectOfficeReports",
                Page = "/Tot/Index"
            }
        };

        if (trainingTrackerEnabled)
        {
            projectOfficeReportsChildren.Add(new NavigationItem
            {
                Text = "Training tracker",
                Area = "ProjectOfficeReports",
                Page = "/Training/Index",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewTrainingTracker
            });
        }

        var proliferationTracker = new NavigationItem
        {
            Text = "Proliferation tracker",
            Area = "ProjectOfficeReports",
            Page = "/Proliferation/Index"
        };

        projectOfficeReportsChildren.Add(new NavigationItem
        {
            Text = "IPR tracker",
            Area = "ProjectOfficeReports",
            Page = "/Ipr/Index",
            AuthorizationPolicy = Policies.Ipr.View
        });

        projectOfficeReportsChildren.Add(new NavigationItem
        {
            Text = "Social media tracker",
            Area = "ProjectOfficeReports",
            Page = "/SocialMedia/Index"
        });

        projectOfficeReportsChildren.Add(new NavigationItem
        {
            Text = "Miscellaneous activities",
            Page = "/Activities/Index"
        });

        projectOfficeReportsChildren.Add(proliferationTracker);

        if (roleSet.Contains("Admin"))
        {
            projectOfficeReportsChildren.Add(new NavigationItem
            {
                Text = "Visit types",
                Area = "ProjectOfficeReports",
                Page = "/VisitTypes/Index",
                RequiredRoles = new[] { "Admin" }
            });
            projectOfficeReportsChildren.Add(new NavigationItem
            {
                Text = "Social media event types",
                Area = "ProjectOfficeReports",
                Page = "/Admin/SocialMediaTypes/Index",
                RequiredRoles = new[] { "Admin" }
            });
        }

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

        return items;
    }

    private async Task<HashSet<string>> GetRoleSetAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return roles.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }
}
