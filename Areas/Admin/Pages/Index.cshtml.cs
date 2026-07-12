using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages;

[Authorize(Policy = AdminPolicies.Access)]
public sealed class AdminIndexModel : PageModel
{
    private readonly IAdminDashboardService _dashboard;

    public AdminIndexModel(IAdminDashboardService dashboard)
    {
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    }

    public AdminDashboardMetrics Metrics { get; private set; } = new();
    public IReadOnlyList<AdminDashboardAction> RecentAdminActions { get; private set; } = Array.Empty<AdminDashboardAction>();
    public AttentionViewModel Attention { get; private set; } = new();
    public IReadOnlyList<QuickLinkViewModel> QuickLinks { get; private set; } = Array.Empty<QuickLinkViewModel>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _dashboard.GetAsync(cancellationToken);
        Metrics = snapshot.Metrics;
        RecentAdminActions = snapshot.RecentActions;
        Attention = new AttentionViewModel
        {
            Items = snapshot.AttentionItems
                .Select(item => new AttentionItemViewModel
                {
                    Text = item.Text,
                    LinkText = item.LinkText,
                    Href = NavigationHref(item.NavigationKey, item.RouteValues)
                })
                .ToArray()
        };

        QuickLinks = AdminNavigationCatalog.Entries
            .Where(entry => entry.ShowInQuickLinks)
            .OrderBy(entry => entry.Order)
            .Select(entry => new QuickLinkViewModel
            {
                Text = entry.Item.Text,
                Icon = entry.Item.Icon,
                Href = NavigationHref(entry.Key)
            })
            .Where(link => !string.IsNullOrWhiteSpace(link.Href))
            .ToArray();
    }

    public string? NavigationHref(
        string navigationKey,
        IReadOnlyDictionary<string, object?>? additionalRouteValues = null)
    {
        var destination = AdminNavigationCatalog.Get(navigationKey);
        if (string.IsNullOrWhiteSpace(destination.Page))
        {
            return null;
        }

        var routeValues = new RouteValueDictionary();
        if (destination.Area is not null)
        {
            routeValues["area"] = destination.Area;
        }

        foreach (var value in destination.RouteValues ?? new Dictionary<string, object?>())
        {
            routeValues[value.Key] = value.Value;
        }

        foreach (var value in additionalRouteValues ?? new Dictionary<string, object?>())
        {
            routeValues[value.Key] = value.Value;
        }

        return Url.Page(destination.Page, values: routeValues);
    }

    public sealed class AttentionViewModel
    {
        public IReadOnlyList<AttentionItemViewModel> Items { get; init; } = Array.Empty<AttentionItemViewModel>();
    }

    public sealed class AttentionItemViewModel
    {
        public string Text { get; init; } = string.Empty;
        public string LinkText { get; init; } = string.Empty;
        public string? Href { get; init; }
    }

    public sealed class QuickLinkViewModel
    {
        public string Text { get; init; } = string.Empty;
        public string? Icon { get; init; }
        public string? Href { get; init; }
    }
}
