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

public sealed record AdminNavigationEntry(
    string Key,
    string Group,
    int Order,
    NavigationItem Item,
    bool ShowOnDashboard = false,
    bool ShowInQuickLinks = false);

public static class AdminNavigationCatalog
{
    private static readonly IReadOnlyList<AdminNavigationEntry> Catalog = new AdminNavigationEntry[]
    {
        Entry(AdminNavigationKeys.Dashboard, "Overview", 10, Item("Dashboard", "Admin", "/Index", "bi-speedometer2", AdminPolicies.Access), true),
        Entry(AdminNavigationKeys.Users, "Access & Security", 20, Item("Users", "Admin", "/Users/Index", "bi-people", AdminPolicies.UsersManage), true),
        Entry(AdminNavigationKeys.Logins, "Monitoring", 30, Item("Logins", "Admin", "/Analytics/Logins", "bi-graph-up", AdminPolicies.SecurityView), true),
        Entry(AdminNavigationKeys.Logs, "Monitoring", 40, Item("Logs", "Admin", "/Logs/Index", "bi-journal-text", AdminPolicies.LogsView), true),
        Entry(AdminNavigationKeys.DatabaseHealth, "Monitoring", 50, Item("DB health", "Admin", "/Diagnostics/DbHealth", "bi-heart-pulse", AdminPolicies.SecurityView), true, true),
        Entry(AdminNavigationKeys.ProjectTrash, "Recovery", 60, Item("Project trash", "Admin", "/Projects/Trash", "bi-trash", AdminPolicies.RecoveryManage), true),
        Entry(AdminNavigationKeys.DocumentRecycle, "Recovery", 70, Item("Recycle bin", "Admin", "/Documents/Recycle", "bi-recycle", AdminPolicies.RecoveryManage), true),
        Entry(AdminNavigationKeys.PdfIngestion, "Maintenance", 80, Item("Ingest PDFs", "Admin", "/Documents/IngestExternalPdfs", "bi-upload", AdminPolicies.IngestionManage), false, true),
        Entry(AdminNavigationKeys.DeletedEvents, "Recovery", 90, Item("Deleted events", "Admin", "/Calendar/Deleted", "bi-calendar-x", AdminPolicies.RecoveryManage), true),
        Entry(AdminNavigationKeys.Holidays, "Master Data", 100, Item("Holidays", string.Empty, "/Settings/Holidays/Index", "bi-calendar-week", AdminPolicies.MasterDataManage), false, true),
        Entry(AdminNavigationKeys.Celebrations, "Master Data", 110, Item("Celebrations", string.Empty, "/Celebrations/Index", "bi-stars", AdminPolicies.MasterDataManage), false, true),
        Entry(AdminNavigationKeys.ProjectCategories, "Master Data", 120, Item("Project categories", "Admin", "/Categories/Index", "bi-diagram-3", AdminPolicies.MasterDataManage), false, true),
        Entry(AdminNavigationKeys.TechnicalCategories, "Master Data", 130, Item("Technical categories", "Admin", "/TechnicalCategories/Index", "bi-cpu", AdminPolicies.MasterDataManage), false, true),
        Entry(AdminNavigationKeys.ActivityTypes, "Master Data", 140, Item("Activity types", "Admin", "/ActivityTypes/Index", "bi-list-task", AdminPolicies.ActivityTypesManage), false, true),
        Entry(AdminNavigationKeys.ProjectTypes, "Master Data", 150, Item("Project types", "Admin", "/Lookups/ProjectTypes/Index", "bi-tags", AdminPolicies.MasterDataManage), false, true),
        Entry(AdminNavigationKeys.SponsoringUnits, "Master Data", 160, Item("Sponsoring units", "Admin", "/Lookups/SponsoringUnits/Index", "bi-building", AdminPolicies.MasterDataManage), false, true),
        Entry(AdminNavigationKeys.LineDirectorates, "Master Data", 170, Item("Line dtes", "Admin", "/Lookups/LineDirectorates/Index", "bi-diagram-2", AdminPolicies.MasterDataManage), false, true),
        Entry(AdminNavigationKeys.LegacyImport, "Maintenance", 180, Item("Legacy import", "ProjectOfficeReports", "/Projects/LegacyImport", "bi-database-up", AdminPolicies.IngestionManage), false, true),
        Entry(AdminNavigationKeys.ArchivedProjects, "Recovery", 190, new NavigationItem
        {
            Text = "Archived projects",
            Area = string.Empty,
            Page = "/Projects/Index",
            Icon = "bi-inboxes",
            AuthorizationPolicy = AdminPolicies.Access,
            RouteValues = new Dictionary<string, object?> { ["IncludeArchived"] = true }
        }, false, true),
        Entry(AdminNavigationKeys.Help, "Help", 200, Item("Help", "Admin", "/Help/Index", "bi-question-circle", AdminPolicies.Access), false, true)
    };

    public static IReadOnlyList<AdminNavigationEntry> Entries => Catalog;

    public static IReadOnlyList<NavigationItem> BuildModuleItems() =>
        Catalog.OrderBy(entry => entry.Order).Select(entry => entry.Item).ToArray();

    public static NavigationItem Get(string key) =>
        Catalog.Single(entry => string.Equals(entry.Key, key, StringComparison.Ordinal)).Item;

    public static NavigationItem BuildAdminPanel() => new()
    {
        Text = "Admin Panel",
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
        bool showOnDashboard = false,
        bool showInQuickLinks = false) =>
        new(key, group, order, item, showOnDashboard, showInQuickLinks);

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
