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
                ActivePagePrefix = "/Calendar/",
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
                Text = "Photos",
                Page = "/Photos/Index",
                ActivePagePrefix = "/Photos/",
                Icon = "bi-images"
            },
            new()
            {
                Text = "Miscellaneous activities",
                Page = "/Activities/Index",
                ActivePagePrefix = "/Activities/",
                Icon = "bi-stars"
            },
            new()
            {
                Text = "Task Management",
                Page = "/ActionTasks/Index",
                ActivePagePrefix = "/ActionTasks/",
                AuthorizationPolicy = "ActionTracker.Access",
                Icon = "bi-list-task"
            },
            new()
            {
                Text = "Project Ideas",
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
            },
            new()
            {
                Text = "Proliferation compendium",
                Page = "/Projects/Compendium/Index",
                ActivePagePrefix = "/Projects/Compendium/",
                Icon = "bi-file-earmark-pdf"
            },
            // Standalone tool: users edit their own PDFs in the browser; nothing is stored in PRISM.
            new()
            {
                Text = "PDF Tools",
                Page = "/Tools/Pdf/Index",
                Icon = "bi-file-earmark-pdf-fill",
                OpensInNewTab = true
            },
            // Progress review entry (drawer only).
            new()
            {
                Text = "Progress review",
                Area = "ProjectOfficeReports",
                Page = "/ProgressReview/Index",
                ActivePagePrefix = "/ProgressReview/",
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
                Text = "Document deletion approvals",
                Area = "DocumentRepository",
                Page = "/Admin/DeleteRequests/Index",
                AuthorizationPolicy = "DocRepo.DeleteApprove",
                Icon = "bi-shield-x"
            },
            new()
            {
                Text = "Deleted repository documents",
                Area = "DocumentRepository",
                Page = "/Admin/Trash/Index",
                AuthorizationPolicy = "DocRepo.Purge",
                Icon = "bi-trash3"
            },
            new()
            {
                Text = "Unreadable PDFs",
                Area = "DocumentRepository",
                Page = "/Admin/OCRFailures/OcrFailures",
                AuthorizationPolicy = "DocRepo.DeleteApprove",
                Icon = "bi-bug"
            },
            new()
            {
                Text = "Repository offices",
                Area = "DocumentRepository",
                Page = "/Admin/OfficeCategories/Index",
                RequiredRoles = new[] { RoleNames.Admin },
                Icon = "bi-buildings"
            },
            new()
            {
                Text = "Repository document types",
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
                    ActivePagePrefix = "/Visits/",
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

                new()
                {
                    Text = "Social media tracker",
                    Area = "ProjectOfficeReports",
                    Page = "/SocialMedia/Index",
                    ActivePagePrefix = "/SocialMedia/",
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
                ActivePagePrefix = "/Training/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewTrainingTracker,
                BadgeViewComponentName = "TrainingApprovalsBadge",
                Icon = "bi-award"
            },
            new()
            {
                Text = "ToT tracker",
                Area = "ProjectOfficeReports",
                Page = "/Tot/Index",
                ActivePagePrefix = "/Tot/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewTotTracker,
                Icon = "bi-diagram-3"
            },
            new()
            {
                Text = "Proliferation tracker",
                Area = "ProjectOfficeReports",
                Page = "/Proliferation/Summary",
                ActivePagePrefix = "/Proliferation/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewProliferationTracker,
                Icon = "bi-radar"
            },
            new()
            {
                Text = "IPR Register",
                Area = "ProjectOfficeReports",
                Page = "/Ipr/Index",
                ActivePagePrefix = "/Ipr/",
                AuthorizationPolicy = Policies.Ipr.View,
                Icon = "bi-lightbulb"
            },
            new()
            {
                Text = "FFC simulators",
                Area = "ProjectOfficeReports",
                Page = "/FFC/Index",
                ActivePagePrefix = "/FFC/",
                Icon = "bi-cpu"
            },
            new()
            {
                Text = "ARPP / PPP",
                Area = "ProjectOfficeReports",
                Page = "/ARPP/Index",
                ActivePagePrefix = "/ARPP/",
                AuthorizationPolicy = ProjectOfficeReportsPolicies.ViewArpp,
                Icon = "bi-journal-check"
            }
        };

        items.Add(new NavigationItem
        {
            Text = "Project office reports",
            Children = projectOfficeReportsChildren,
            Icon = "bi-clipboard-data"
        });

        var activityTypesNavigationItem = AdminNavigationCatalog.ActivityTypesItem();

        if (roleSet.Contains(RoleNames.Admin))
        {
            items.Add(AdminNavigationCatalog.BuildAdminPanel());
        }

        if (roleSet.Contains(RoleNames.HoD) && !roleSet.Contains(RoleNames.Admin))
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
