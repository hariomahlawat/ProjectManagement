using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages;

[Authorize(Policy = AdminPolicies.Access)]
[ResponseCache(NoStore = true)]
public sealed class AdminIndexModel : PageModel
{
    private readonly IAdminDashboardService _dashboard;
    private readonly IAdminNavigationUrlBuilder _navigation;
    private readonly IAuditActionPresentationCatalog _auditActions;

    public AdminIndexModel(
        IAdminDashboardService dashboard,
        IAdminNavigationUrlBuilder navigation,
        IAuditActionPresentationCatalog auditActions)
    {
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _auditActions = auditActions ?? throw new ArgumentNullException(nameof(auditActions));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public AdminDashboardMetrics Metrics { get; private set; } = new();

    public IReadOnlyList<AdminMetricCardModel> MetricCards { get; private set; } =
        Array.Empty<AdminMetricCardModel>();

    public IReadOnlyList<AdminDashboardAction> RecentAdminActions { get; private set; } =
        Array.Empty<AdminDashboardAction>();

    public IReadOnlyList<AttentionItemViewModel> AttentionItems { get; private set; } =
        Array.Empty<AttentionItemViewModel>();

    public IReadOnlyList<DashboardLinkGroupViewModel> LinkGroups { get; private set; } =
        Array.Empty<DashboardLinkGroupViewModel>();

    public string OperationalTone { get; private set; } = "success";

    public string OperationalLabel { get; private set; } = "Operational";

    public string OperationalSummary { get; private set; } =
        "No immediate administrative action is indicated by the current dashboard metrics.";

    public string OperationalActionText { get; private set; } = "View system health";

    public string? OperationalActionHref { get; private set; }

    public string OperationalIcon => OperationalTone switch
    {
        "danger" => "bi-exclamation-octagon",
        "warning" => "bi-exclamation-triangle",
        _ => "bi-check-circle"
    };

    public AdminEmptyStateModel NoAttentionEmptyState { get; } = new()
    {
        Title = "No items require attention",
        Description = "The current administrative indicators do not show any outstanding action.",
        Icon = "bi-check-circle"
    };

    public AdminEmptyStateModel NoRecentActivityEmptyState { get; } = new()
    {
        Title = "No recent administrative activity",
        Description = "Administrative actions will appear here as they are recorded.",
        Icon = "bi-clock-history"
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _dashboard.GetAsync(cancellationToken);
        Metrics = snapshot.Metrics;
        RecentAdminActions = snapshot.RecentActions;

        Header = new AdminPageHeaderModel
        {
            Eyebrow = "System administration",
            Title = "Administration",
            Description = "Manage access, monitor system activity, recover records and maintain PRISM configuration.",
            Icon = "bi-shield-check",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Admin help",
                    Href = NavigationHref(AdminNavigationKeys.Help),
                    Icon = "bi-question-circle"
                },
                new AdminPageActionModel
                {
                    Text = "Manage users",
                    Href = NavigationHref(AdminNavigationKeys.Users),
                    Icon = "bi-person-gear",
                    IsPrimary = true
                }
            }
        };

        MetricCards = BuildMetricCards(Metrics);
        AttentionItems = snapshot.AttentionItems
            .Select(item => new AttentionItemViewModel
            {
                Severity = item.Severity,
                Title = item.Title,
                Detail = item.Detail,
                LinkText = item.LinkText,
                Href = NavigationHref(item.NavigationKey, item.RouteValues)
            })
            .ToArray();

        LinkGroups = BuildLinkGroups();
        ResolveOperationalStatus(Metrics);
        ResolveOperationalAction();
    }

    public string? NavigationHref(
        string navigationKey,
        IReadOnlyDictionary<string, object?>? additionalRouteValues = null) =>
        _navigation.GetPath(HttpContext, navigationKey, additionalRouteValues);

    public string LevelCss(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "error" => "danger",
        "warning" => "warning",
        _ => "neutral"
    };

    public AuditActionPresentation ActionPresentation(AdminDashboardAction action) =>
        _auditActions.Describe(action.Action, action.Level);

    private IReadOnlyList<AdminMetricCardModel> BuildMetricCards(AdminDashboardMetrics metrics) =>
        new[]
        {
            new AdminMetricCardModel
            {
                Eyebrow = "Access & security",
                Value = metrics.TotalUsers.ToString("N0"),
                Label = "Total user accounts",
                Icon = "bi-people",
                Tone = metrics.RestrictedUsers > 0 ? "warning" : "success",
                Href = NavigationHref(AdminNavigationKeys.Users),
                LinkText = "Manage",
                Details = new[]
                {
                    new AdminMetricDetailModel("Active", metrics.ActiveUsers.ToString("N0"), "success"),
                    new AdminMetricDetailModel("Restricted", metrics.RestrictedUsers.ToString("N0"), metrics.RestrictedUsers > 0 ? "warning" : null)
                }
            },
            new AdminMetricCardModel
            {
                Eyebrow = "Authentication",
                Value = metrics.LoginsLast7Days.ToString("N0"),
                Label = "Successful logins in 7 days",
                Icon = "bi-box-arrow-in-right",
                Tone = metrics.FailedLoginsLast7Days > 0 ? "warning" : "neutral",
                Href = NavigationHref(AdminNavigationKeys.Logins),
                LinkText = "Analyse",
                Details = new[]
                {
                    new AdminMetricDetailModel("Unique users", metrics.UniqueUsersLast7Days.ToString("N0")),
                    new AdminMetricDetailModel("Failed", metrics.FailedLoginsLast7Days.ToString("N0"), metrics.FailedLoginsLast7Days > 0 ? "warning" : null)
                }
            },
            new AdminMetricCardModel
            {
                Eyebrow = "Audit activity",
                Value = metrics.AuditEventsLast24Hours.ToString("N0"),
                Label = "Events in the last 24 hours",
                Icon = "bi-journal-check",
                Tone = metrics.ErrorEventsLast24Hours > 0
                    ? "danger"
                    : metrics.WarningEventsLast24Hours > 0 ? "warning" : "success",
                Href = NavigationHref(AdminNavigationKeys.Logs),
                LinkText = "Review",
                Details = new[]
                {
                    new AdminMetricDetailModel("Warnings", metrics.WarningEventsLast24Hours.ToString("N0"), metrics.WarningEventsLast24Hours > 0 ? "warning" : null),
                    new AdminMetricDetailModel("Errors", metrics.ErrorEventsLast24Hours.ToString("N0"), metrics.ErrorEventsLast24Hours > 0 ? "danger" : null)
                }
            },
            new AdminMetricCardModel
            {
                Eyebrow = "Recovery",
                Value = metrics.RecoveryQueue.ToString("N0"),
                Label = "Items awaiting recovery review",
                Icon = "bi-arrow-counterclockwise",
                Tone = metrics.RecoveryQueue > 0 ? "warning" : "success",
                Href = NavigationHref(AdminNavigationKeys.ProjectTrash),
                LinkText = "Open",
                Details = new[]
                {
                    new AdminMetricDetailModel("Projects", metrics.TrashedProjects.ToString("N0")),
                    new AdminMetricDetailModel("Documents / events", (metrics.DeletedDocuments + metrics.DeletedEvents).ToString("N0"))
                }
            }
        };

    private IReadOnlyList<DashboardLinkGroupViewModel> BuildLinkGroups() =>
        new[]
        {
            new DashboardLinkGroupViewModel
            {
                Title = "Monitoring",
                Description = "Review authentication, audit activity and system diagnostics.",
                Icon = "bi-activity",
                Links = BuildLinks(
                    AdminNavigationKeys.Logins,
                    AdminNavigationKeys.Logs,
                    AdminNavigationKeys.DatabaseHealth)
            },
            new DashboardLinkGroupViewModel
            {
                Title = "Recovery & maintenance",
                Description = "Restore deleted records and manage controlled data-ingestion tasks.",
                Icon = "bi-tools",
                Links = BuildLinks(
                    AdminNavigationKeys.ProjectTrash,
                    AdminNavigationKeys.DocumentRecycle,
                    AdminNavigationKeys.DeletedEvents,
                    AdminNavigationKeys.ArchivedProjects,
                    AdminNavigationKeys.PdfIngestion,
                    AdminNavigationKeys.LegacyImport)
            },
            new DashboardLinkGroupViewModel
            {
                Title = "Master data",
                Description = "Maintain the controlled lists and taxonomies used across PRISM.",
                Icon = "bi-diagram-3",
                Links = BuildLinks(
                    AdminNavigationKeys.ProjectCategories,
                    AdminNavigationKeys.TechnicalCategories,
                    AdminNavigationKeys.ActivityTypes,
                    AdminNavigationKeys.ProjectTypes,
                    AdminNavigationKeys.SponsoringUnits,
                    AdminNavigationKeys.LineDirectorates,
                    AdminNavigationKeys.Holidays,
                    AdminNavigationKeys.Celebrations)
            }
        };

    private IReadOnlyList<DashboardLinkViewModel> BuildLinks(params string[] keys) =>
        keys.Select(key =>
            {
                var entry = AdminNavigationCatalog.GetEntry(key);
                return new DashboardLinkViewModel
                {
                    Text = entry.Item.Text,
                    Icon = entry.Item.Icon,
                    Href = NavigationHref(key)
                };
            })
            .Where(link => !string.IsNullOrWhiteSpace(link.Href))
            .ToArray();


    private void ResolveOperationalAction()
    {
        var priority = AttentionItems.FirstOrDefault();
        if (priority is not null && !string.IsNullOrWhiteSpace(priority.Href))
        {
            OperationalActionText = priority.LinkText;
            OperationalActionHref = priority.Href;
            return;
        }

        OperationalActionText = "View system health";
        OperationalActionHref = NavigationHref(AdminNavigationKeys.DatabaseHealth);
    }

    private void ResolveOperationalStatus(AdminDashboardMetrics metrics)
    {
        if (metrics.ErrorEventsLast24Hours > 0)
        {
            OperationalTone = "danger";
            OperationalLabel = "Action required";
            OperationalSummary = $"{metrics.ErrorEventsLast24Hours:N0} audit error event(s) were recorded in the last 24 hours.";
            return;
        }

        if (metrics.PendingDeletionUsers > 0
            || metrics.LockedUsers > 0
            || metrics.MustChangePasswordUsers > 0)
        {
            OperationalTone = "warning";
            OperationalLabel = "Review required";
            OperationalSummary = "User access controls contain items requiring administrator review.";
            return;
        }

        OperationalTone = "success";
        OperationalLabel = "Operational";
        OperationalSummary = "No critical audit or user-access condition is indicated by the current dashboard metrics.";
    }

    public sealed class AttentionItemViewModel
    {
        public AdminAttentionSeverity Severity { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Detail { get; init; } = string.Empty;

        public string LinkText { get; init; } = string.Empty;

        public string? Href { get; init; }

        public string Tone => Severity switch
        {
            AdminAttentionSeverity.Critical => "danger",
            AdminAttentionSeverity.Warning => "warning",
            _ => "info"
        };

        public string Icon => Severity switch
        {
            AdminAttentionSeverity.Critical => "bi-exclamation-octagon",
            AdminAttentionSeverity.Warning => "bi-exclamation-triangle",
            _ => "bi-info-circle"
        };
    }

    public sealed class DashboardLinkGroupViewModel
    {
        public string Title { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string Icon { get; init; } = "bi-grid";

        public IReadOnlyList<DashboardLinkViewModel> Links { get; init; } =
            Array.Empty<DashboardLinkViewModel>();
    }

    public sealed class DashboardLinkViewModel
    {
        public string Text { get; init; } = string.Empty;

        public string? Icon { get; init; }

        public string? Href { get; init; }
    }
}
