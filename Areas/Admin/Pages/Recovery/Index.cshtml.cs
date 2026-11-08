using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Recovery;

/// <summary>
/// "Restore deleted items", Overview tab (docs/ux/admin-module.md, PR 5): every kind of deleted
/// thing, how many can come back and for how long. Each kind's tab keeps its own page and its
/// confirmed restore and delete-for-good actions.
/// </summary>
[Authorize(Policy = AdminPolicies.RecoveryManage)]
public sealed class IndexModel : PageModel
{
    private readonly IAdminRecoverySummaryService _summary;
    private readonly IAdminRestoreTabsService _tabs;
    private readonly IAdminNavigationUrlBuilder _navigation;
    private readonly IAdminTimeService _time;
    private readonly DocRepoTrashPurgeService _repositoryTrash;
    private readonly IOptionsMonitor<ChatOptions> _chat;

    public IndexModel(
        IAdminRecoverySummaryService summary,
        IAdminRestoreTabsService tabs,
        IAdminNavigationUrlBuilder navigation,
        IAdminTimeService time,
        DocRepoTrashPurgeService repositoryTrash,
        IOptionsMonitor<ChatOptions> chat)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _tabs = tabs ?? throw new ArgumentNullException(nameof(tabs));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _repositoryTrash = repositoryTrash ?? throw new ArgumentNullException(nameof(repositoryTrash));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public AdminRecoverySummary Summary { get; private set; } =
        new(0, 0, 0, 0, 0, 0, 7, 0, 0, Array.Empty<AdminRecoveryOperation>());

    public IReadOnlyList<RestoreKindViewModel> Kinds { get; private set; } = Array.Empty<RestoreKindViewModel>();

    public string? ArchivedProjectsHref { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Summary = await _summary.GetAsync(cancellationToken);
        var tabs = await _tabs.GetAsync(User, cancellationToken);

        Kinds = tabs
            .Where(tab => tab.Key != AdminRestoreTabKeys.Overview)
            .Select(tab => new RestoreKindViewModel(
                tab.Label,
                tab.Count,
                KeptFor(tab.Key),
                Href(tab),
                tab.OpensAnotherPage))
            .Where(kind => kind.Href is not null)
            .ToArray();

        ArchivedProjectsHref = Path(AdminNavigationKeys.ArchivedProjects);
        Header = new AdminPageHeaderModel
        {
            Title = "Restore deleted items",
            Description = "Everything deleted in PRISM that can still be brought back. Restoring is always safe; deleting for good is a separate step that asks you to confirm.",
            Icon = "bi-arrow-counterclockwise"
        };
    }

    // How long each kind stays restorable, in plain words.
    internal string KeptFor(string key) => key switch
    {
        AdminRestoreTabKeys.Projects => $"Kept {Summary.ProjectRetentionDays} days, then deleted for good automatically."
            + (Summary.DueSoon > 0 ? $" {Summary.DueSoon} will go within {Summary.DueSoonWindowDays} days." : string.Empty),
        AdminRestoreTabKeys.ProjectDocuments => "Kept until an administrator deletes them for good."
            + (Summary.RecycledDocumentBytes > 0 ? $" They take {FormatBytes(Summary.RecycledDocumentBytes)}." : string.Empty),
        AdminRestoreTabKeys.RepositoryDocuments => _repositoryTrash.RetentionDays > 0
            ? $"Kept {_repositoryTrash.RetentionDays} days, then deleted for good automatically."
            : "Kept until someone deletes them for good.",
        AdminRestoreTabKeys.CalendarEvents => "Kept until someone restores them.",
        AdminRestoreTabKeys.ChatFiles => $"Each clean-up can be undone for {_chat.CurrentValue.Attachments.UndoHours} hours. Opens Chat file storage.",
        _ => string.Empty
    };

    public string? Path(string key) => _navigation.GetPath(HttpContext, key);

    public string FormatTime(DateTimeOffset value) => _time.FormatIst(value);

    public static string FormatBytes(long bytes)
    {
        var size = (double)Math.Max(0, bytes);
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var index = 0;
        while (size >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }
        return $"{size:0.##} {units[index]}";
    }

    private string? Href(AdminRestoreTab tab) => tab.HrefFrom(Path(tab.NavigationKey));

    public sealed record RestoreKindViewModel(string Label, int? Count, string KeptFor, string? Href, bool OpensAnotherPage);
}
