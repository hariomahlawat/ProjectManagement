namespace ProjectManagement.Services.Usage;

public sealed record ErpUsageModuleDescriptor(
    string Key,
    string Label,
    string Icon,
    IReadOnlyList<string> PathPrefixes);

public interface IErpUsageModuleCatalog
{
    IReadOnlyList<ErpUsageModuleDescriptor> Modules { get; }
    ErpUsageModuleDescriptor? Find(string? key);
    bool IsKnownModule(string? key);
    string? ResolvePath(PathString path);
}

/// <summary>
/// Maps application routes to stable, privacy-safe module keys. Query strings, record
/// identifiers and page content are deliberately not recorded in usage data.
/// </summary>
public sealed class ErpUsageModuleCatalog : IErpUsageModuleCatalog
{
    private static readonly IReadOnlyList<ErpUsageModuleDescriptor> Items =
    [
        Module("dashboard", "Dashboard", "bi-speedometer2", "/Dashboard"),
        Module("workspace", "Workspace", "bi-person-workspace", "/Workspace"),
        Module("projects", "Projects", "bi-kanban", "/Projects", "/Process", "/Approvals", "/IndustryPartners"),
        Module("calendar", "Calendar", "bi-calendar3", "/Calendar", "/Celebrations", "/Settings/Holidays"),
        Module("documents", "Documents", "bi-files", "/DocumentRepository", "/Files"),
        Module("media", "Photos and media", "bi-images", "/Photos", "/MediaLibrary"),
        Module("notebook", "Notebook", "bi-journal-richtext", "/Notebook"),
        Module("tasks", "Tasks", "bi-list-check", "/ActionTasks", "/Tasks"),
        Module("activities", "Activities", "bi-activity", "/Activities"),
        Module("reports", "Reports and FFC", "bi-bar-chart", "/ProjectOfficeReports", "/FFC", "/Reports"),
        Module("administration", "Administration", "bi-shield-lock", "/Admin", "/Usage"),
        Module("search", "Search", "bi-search", "/Common/Search")
    ];

    public IReadOnlyList<ErpUsageModuleDescriptor> Modules => Items;

    public ErpUsageModuleDescriptor? Find(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : Items.FirstOrDefault(item => string.Equals(item.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool IsKnownModule(string? key) => Find(key) is not null;

    public string? ResolvePath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Items
            .FirstOrDefault(item => item.PathPrefixes.Any(prefix =>
                path.StartsWithSegments(new PathString(prefix), StringComparison.OrdinalIgnoreCase)))
            ?.Key;
    }

    private static ErpUsageModuleDescriptor Module(
        string key,
        string label,
        string icon,
        params string[] pathPrefixes) =>
        new(key, label, icon, pathPrefixes);
}
