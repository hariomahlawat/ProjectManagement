using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.AllPages;

/// <summary>
/// "Every admin page": the whole Administration catalogue in one list, grouped by task, with the
/// sentence that says what each page is for. Only pages the viewer can open are listed
/// (docs/ux/admin-module.md, rule 3 "Nothing hidden").
/// </summary>
[Authorize(Policy = AdminPolicies.Access)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    private readonly IAdminNavigationAccess _access;
    private readonly IAdminNavigationUrlBuilder _urlBuilder;

    public IndexModel(IAdminNavigationAccess access, IAdminNavigationUrlBuilder urlBuilder)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _urlBuilder = urlBuilder ?? throw new ArgumentNullException(nameof(urlBuilder));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public IReadOnlyList<AdminPageIndexGroup> Groups { get; private set; } = Array.Empty<AdminPageIndexGroup>();

    public int PageCount => Groups.Sum(group => group.Pages.Count);

    public async Task OnGetAsync()
    {
        var entry = AdminNavigationCatalog.GetEntry(AdminNavigationKeys.AllPages);
        Header = new AdminPageHeaderModel
        {
            Title = entry.Item.Text,
            Description = "Every administration page you can open, grouped by task. Type a word to narrow the list.",
            Icon = entry.Item.Icon
        };

        var openable = await _access.GetOpenableEntriesAsync(User);
        var keys = openable.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);

        Groups = AdminNavigationGroups.Ordered
            .Select(groupName => new AdminPageIndexGroup(
                groupName,
                openable
                    .Where(item => string.Equals(item.Group, groupName, StringComparison.Ordinal))
                    .Select(item => ToPage(item, keys))
                    .Where(page => page.Url is not null)
                    .ToArray()))
            .Where(group => group.Pages.Count > 0)
            .ToArray();
    }

    private AdminPageIndexPage ToPage(AdminNavigationEntry entry, IReadOnlySet<string> openableKeys)
    {
        // "Opens from" names the sidebar entry that leads to this page, when the viewer can see it.
        var parent = entry.SidebarParent is { } parentKey && openableKeys.Contains(parentKey)
            ? AdminNavigationCatalog.GetEntry(parentKey).Item.Text
            : null;

        return new AdminPageIndexPage(
            entry.Key,
            entry.Item.Text,
            entry.Item.Icon,
            _urlBuilder.GetPath(HttpContext, entry.Item),
            entry.Description,
            entry.Keywords,
            parent,
            entry.UsesAdminShell ? null : OwnModuleName(entry));
    }

    internal static string OwnModuleName(AdminNavigationEntry entry) =>
        entry.Item.Page?.StartsWith("/Approvals/", StringComparison.OrdinalIgnoreCase) == true
            ? "the Decision Centre"
            : string.Equals(entry.Item.Area, "DocumentRepository", StringComparison.OrdinalIgnoreCase)
            ? "the Document Repository"
            : entry.Item.Page?.StartsWith("/Admin/Media", StringComparison.OrdinalIgnoreCase) == true
                ? "the media library"
                : "its own module";
}

public sealed record AdminPageIndexGroup(string Name, IReadOnlyList<AdminPageIndexPage> Pages);

public sealed record AdminPageIndexPage(
    string Key,
    string Text,
    string? Icon,
    string? Url,
    string Description,
    string Keywords,
    string? OpensFrom,
    string? OwnModule)
{
    /// <summary>What the filter matches: the name, the description and the search words.</summary>
    public string SearchLabel => $"{Text} {Keywords} {Description}".ToLowerInvariant();
}
