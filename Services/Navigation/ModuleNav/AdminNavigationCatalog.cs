using Microsoft.AspNetCore.Http;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Navigation;

namespace ProjectManagement.Services.Navigation.ModuleNav;

public static class AdminNavigationKeys
{
    public const string Dashboard = "dashboard";
    public const string Users = "users";
    public const string Logins = "logins";
    public const string Logs = "logs";
    public const string DatabaseHealth = "database-health";
    public const string ProjectTrash = "project-trash";
    public const string DocumentRecycle = "document-recycle";
    public const string PdfIngestion = "pdf-ingestion";
    public const string DeletedEvents = "deleted-events";
    public const string Holidays = "holidays";
    public const string Celebrations = "celebrations";
    public const string ProjectCategories = "project-categories";
    public const string TechnicalCategories = "technical-categories";
    public const string ActivityTypes = "activity-types";
    public const string ProjectTypes = "project-types";
    public const string SponsoringUnits = "sponsoring-units";
    public const string LineDirectorates = "line-directorates";
    public const string LegacyImport = "legacy-import";
    public const string ArchivedProjects = "archived-projects";
    public const string Help = "help";
}

public static class AdminNavigationGroups
{
    public const string Overview = "Overview";
    public const string AccessSecurity = "Access & Security";
    public const string Monitoring = "Monitoring";
    public const string Recovery = "Recovery";
    public const string Maintenance = "Maintenance";
    public const string MasterData = "Master Data";
    public const string Help = "Help";

    public static IReadOnlyList<string> Ordered { get; } = new[]
    {
        Overview,
        AccessSecurity,
        Monitoring,
        Recovery,
        Maintenance,
        MasterData,
        Help
    };
}

public sealed record AdminNavigationMatch(
    string Area,
    string PagePattern,
    bool ExactPage = false,
    IReadOnlyDictionary<string, string?>? RequiredQuery = null)
{
    public bool Matches(
        string? currentArea,
        string? currentPage,
        IQueryCollection? query)
    {
        if (!string.Equals(Area, currentArea ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(currentPage))
        {
            return false;
        }

        var pageMatches = ExactPage
            ? string.Equals(PagePattern, currentPage, StringComparison.OrdinalIgnoreCase)
            : currentPage.StartsWith(PagePattern, StringComparison.OrdinalIgnoreCase);

        if (!pageMatches)
        {
            return false;
        }

        if (RequiredQuery is null || RequiredQuery.Count == 0)
        {
            return true;
        }

        if (query is null)
        {
            return false;
        }

        foreach (var required in RequiredQuery)
        {
            if (!query.TryGetValue(required.Key, out var actual)
                || !string.Equals(actual.ToString(), required.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record AdminNavigationEntry(
    string Key,
    string Group,
    int Order,
    NavigationItem Item,
    AdminNavigationMatch Match,
    bool ShowOnDashboard = false,
    bool ShowInQuickLinks = false);

public static class AdminNavigationCatalog
{
    private static readonly IReadOnlyList<AdminNavigationEntry> Catalog = new AdminNavigationEntry[]
    {
        Entry(
            AdminNavigationKeys.Dashboard,
            AdminNavigationGroups.Overview,
            10,
            Item("Overview", "Admin", "/Index", "bi-grid-1x2", AdminPolicies.Access),
            exactPage: true,
            showOnDashboard: true),

        Entry(
            AdminNavigationKeys.Users,
            AdminNavigationGroups.AccessSecurity,
            20,
            Item("Users", "Admin", "/Users/Index", "bi-people", AdminPolicies.UsersManage),
            matchPagePattern: "/Users/",
            showOnDashboard: true),

        Entry(
            AdminNavigationKeys.Logins,
            AdminNavigationGroups.Monitoring,
            30,
            Item("Login activity", "Admin", "/Analytics/Logins", "bi-graph-up-arrow", AdminPolicies.SecurityView),
            matchPagePattern: "/Analytics/",
            showOnDashboard: true),

        Entry(
            AdminNavigationKeys.Logs,
            AdminNavigationGroups.Monitoring,
            40,
            Item("Audit logs", "Admin", "/Logs/Index", "bi-journal-text", AdminPolicies.LogsView),
            matchPagePattern: "/Logs/",
            showOnDashboard: true),

        Entry(
            AdminNavigationKeys.DatabaseHealth,
            AdminNavigationGroups.Monitoring,
            50,
            Item("System health", "Admin", "/Diagnostics/DbHealth", "bi-heart-pulse", AdminPolicies.SecurityView),
            exactPage: true,
            showOnDashboard: true,
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.ProjectTrash,
            AdminNavigationGroups.Recovery,
            60,
            Item("Project trash", "Admin", "/Projects/Trash", "bi-trash3", AdminPolicies.RecoveryManage),
            exactPage: true,
            showOnDashboard: true),

        Entry(
            AdminNavigationKeys.DocumentRecycle,
            AdminNavigationGroups.Recovery,
            70,
            Item("Document recycle bin", "Admin", "/Documents/Recycle", "bi-recycle", AdminPolicies.RecoveryManage),
            exactPage: true,
            showOnDashboard: true),

        Entry(
            AdminNavigationKeys.DeletedEvents,
            AdminNavigationGroups.Recovery,
            80,
            Item("Deleted events", "Admin", "/Calendar/Deleted", "bi-calendar-x", AdminPolicies.RecoveryManage),
            exactPage: true,
            showOnDashboard: true),

        Entry(
            AdminNavigationKeys.ArchivedProjects,
            AdminNavigationGroups.Recovery,
            90,
            new NavigationItem
            {
                Text = "Archived projects",
                Area = string.Empty,
                Page = "/Projects/Index",
                Icon = "bi-archive",
                AuthorizationPolicy = AdminPolicies.Access,
                RouteValues = new Dictionary<string, object?> { ["IncludeArchived"] = true }
            },
            exactPage: true,
            requiredQuery: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["IncludeArchived"] = "true"
            },
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.PdfIngestion,
            AdminNavigationGroups.Maintenance,
            100,
            Item("PDF ingestion", "Admin", "/Documents/IngestExternalPdfs", "bi-file-earmark-arrow-up", AdminPolicies.IngestionManage),
            exactPage: true,
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.LegacyImport,
            AdminNavigationGroups.Maintenance,
            110,
            Item("Legacy import", "ProjectOfficeReports", "/Projects/LegacyImport", "bi-database-up", AdminPolicies.IngestionManage),
            exactPage: true,
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.Holidays,
            AdminNavigationGroups.MasterData,
            120,
            Item("Holidays", string.Empty, "/Settings/Holidays/Index", "bi-calendar-week", AdminPolicies.HolidaysManage),
            matchPagePattern: "/Settings/Holidays/",
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.Celebrations,
            AdminNavigationGroups.MasterData,
            130,
            Item("Celebrations", string.Empty, "/Celebrations/Index", "bi-stars", AdminPolicies.MasterDataManage),
            matchPagePattern: "/Celebrations/",
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.ProjectCategories,
            AdminNavigationGroups.MasterData,
            140,
            Item("Project categories", "Admin", "/Categories/Index", "bi-diagram-3", AdminPolicies.MasterDataManage),
            matchPagePattern: "/Categories/",
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.TechnicalCategories,
            AdminNavigationGroups.MasterData,
            150,
            Item("Technical categories", "Admin", "/TechnicalCategories/Index", "bi-cpu", AdminPolicies.MasterDataManage),
            matchPagePattern: "/TechnicalCategories/",
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.ActivityTypes,
            AdminNavigationGroups.MasterData,
            160,
            Item("Activity types", "Admin", "/ActivityTypes/Index", "bi-list-task", AdminPolicies.ActivityTypesManage),
            matchPagePattern: "/ActivityTypes/",
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.ProjectTypes,
            AdminNavigationGroups.MasterData,
            170,
            Item("Project types", "Admin", "/Lookups/ProjectTypes/Index", "bi-tags", AdminPolicies.MasterDataManage),
            matchPagePattern: "/Lookups/ProjectTypes/",
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.SponsoringUnits,
            AdminNavigationGroups.MasterData,
            180,
            Item("Sponsoring units", "Admin", "/Lookups/SponsoringUnits/Index", "bi-building", AdminPolicies.MasterDataManage),
            matchPagePattern: "/Lookups/SponsoringUnits/",
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.LineDirectorates,
            AdminNavigationGroups.MasterData,
            190,
            Item("Line directorates", "Admin", "/Lookups/LineDirectorates/Index", "bi-diagram-2", AdminPolicies.MasterDataManage),
            matchPagePattern: "/Lookups/LineDirectorates/",
            showInQuickLinks: true),

        Entry(
            AdminNavigationKeys.Help,
            AdminNavigationGroups.Help,
            200,
            Item("Admin help", "Admin", "/Help/Index", "bi-question-circle", AdminPolicies.Access),
            matchPagePattern: "/Help/",
            showInQuickLinks: true)
    };

    public static IReadOnlyList<AdminNavigationEntry> Entries => Catalog;

    public static IReadOnlyList<NavigationItem> BuildModuleItems() =>
        Catalog.OrderBy(entry => entry.Order).Select(entry => entry.Item).ToArray();

    public static NavigationItem Get(string key) =>
        GetEntry(key).Item;

    public static AdminNavigationEntry GetEntry(string key) =>
        Catalog.Single(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));

    public static bool IsInScope(
        string? currentArea,
        string? currentPage,
        string? currentController,
        string? currentAction,
        IQueryCollection? query = null)
    {
        _ = currentController;
        _ = currentAction;

        if (string.Equals(currentArea, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Catalog.Any(entry => entry.Match.Matches(currentArea, currentPage, query));
    }

    public static AdminNavigationEntry? FindActiveEntry(
        string? currentArea,
        string? currentPage,
        IQueryCollection? query = null) =>
        Catalog.FirstOrDefault(entry => entry.Match.Matches(currentArea, currentPage, query));

    public static NavigationItem BuildAdminPanel() => new()
    {
        Text = "Administration",
        Area = "Admin",
        Page = "/Index",
        AuthorizationPolicy = AdminPolicies.Access,
        Icon = "bi-shield-lock",
        Accent = "danger",
        Children = BuildModuleItems()
    };

    public static NavigationItem ActivityTypesItem() => Get(AdminNavigationKeys.ActivityTypes);

    private static AdminNavigationEntry Entry(
        string key,
        string group,
        int order,
        NavigationItem item,
        string? matchPagePattern = null,
        bool exactPage = false,
        IReadOnlyDictionary<string, string?>? requiredQuery = null,
        bool showOnDashboard = false,
        bool showInQuickLinks = false)
    {
        var pagePattern = matchPagePattern ?? item.Page
            ?? throw new InvalidOperationException($"Admin navigation entry '{key}' must define a page.");

        return new AdminNavigationEntry(
            key,
            group,
            order,
            item,
            new AdminNavigationMatch(item.Area ?? string.Empty, pagePattern, exactPage, requiredQuery),
            showOnDashboard,
            showInQuickLinks);
    }

    private static NavigationItem Item(
        string text,
        string? area,
        string page,
        string icon,
        string policy) => new()
    {
        Text = text,
        Area = area,
        Page = page,
        Icon = icon,
        AuthorizationPolicy = policy
    };
}
