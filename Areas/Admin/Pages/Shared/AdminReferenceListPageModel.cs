using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Shared;

/// <summary>What a reference list page shows around the shared list (its header and links).</summary>
public sealed record AdminReferenceListPresentation(
    string NavigationKey,
    string Icon,
    string AddText,
    string UsedByLabel,
    string FolderPath);

/// <summary>
/// The Index page of a reference list on the shared list page (docs/ux/admin-review-3-fixes.md,
/// owner decision 1). Each list's page names its list, order check and move command; the
/// handlers (Move, RenumberOrder, Delete) and the query names (q, status, pageNumber, pageSize)
/// are the same on every list. Each concrete page keeps its own [Authorize] policy.
/// </summary>
public abstract class AdminReferenceListPageModel : PageModel
{
    private readonly IReferenceListService _lists;
    private readonly IListOrderNoticeService _order;

    protected AdminReferenceListPageModel(IReferenceListService lists, IListOrderNoticeService order)
    {
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
        _order = order ?? throw new ArgumentNullException(nameof(order));
    }

    protected abstract ReferenceListDefinition Definition { get; }

    /// <summary>The Data checks key of the list's order, or null when the list has no order check.</summary>
    protected abstract string? OrderCheckKey { get; }

    protected abstract AdminReferenceListPresentation Presentation { get; }

    /// <summary>Moves an item up (-1) or down (+1); null when the list has no order to change.</summary>
    protected virtual Func<int, int, CancellationToken, Task<AdminOperationResult>>? Move => null;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = ReferenceListStatuses.All;

    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public ListOrderNotice OrderNotice { get; private set; } = ListOrderNotice.None(string.Empty);
    public AdminPageHeaderModel Header { get; private set; } = new();
    public AdminReferenceListModel ListModel { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var page = await _lists.GetPageAsync(
            Definition.Key,
            new ReferenceListRequest(Search, Status, PageNumber, PageSize),
            cancellationToken);
        Search = page.Search;
        Status = page.Status;
        PageNumber = page.Page;
        PageSize = page.PageSize;
        if (OrderCheckKey is not null)
        {
            OrderNotice = await _order.GetAsync(User, OrderCheckKey, cancellationToken);
        }

        Build(page);
    }

    public async Task<IActionResult> OnPostRenumberOrderAsync(CancellationToken cancellationToken)
    {
        if (OrderCheckKey is null)
        {
            return NotFound();
        }

        var result = await _order.RenumberAsync(User, OrderCheckKey, cancellationToken);
        Flash(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int offset, CancellationToken cancellationToken)
    {
        if (Move is null)
        {
            return NotFound();
        }

        Flash(await Move(id, offset, cancellationToken));
        return RedirectToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, string? rowVersion, CancellationToken cancellationToken)
    {
        Flash(await _lists.DeleteAsync(Definition.Key, id, DecodeRowVersion(rowVersion), cancellationToken));
        return RedirectToList();
    }

    private IActionResult RedirectToList() =>
        RedirectToPage(new { q = Search, status = Status, pageNumber = Math.Max(1, PageNumber), pageSize = PageSize });

    private void Flash(AdminOperationResult result) =>
        TempData[result.Succeeded ? FlashMessageKeys.AdminMasterDataSuccess : FlashMessageKeys.AdminMasterDataError] = result.UserMessage;

    private static byte[] DecodeRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<byte>();
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }

    private void Build(ReferenceListPage page)
    {
        var presentation = Presentation;
        var folder = presentation.FolderPath.TrimEnd('/');
        Header = new AdminPageHeaderModel
        {
            Title = Definition.PluralLabel,
            Description = AdminNavigationCatalog.GetEntry(presentation.NavigationKey).Description,
            Icon = presentation.Icon,
            Actions = new[]
            {
                new AdminPageActionModel { Text = "Lists used in forms", Href = Url.Page("/MasterData/Index", new { area = "Admin" }), Icon = "bi-arrow-left" },
                new AdminPageActionModel { Text = presentation.AddText, Href = Url.Page($"{folder}/Create", new { area = "Admin" }), Icon = "bi-plus-lg", IsPrimary = true }
            }
        };
        ListModel = new AdminReferenceListModel
        {
            Page = page,
            SingularLabel = Definition.Label,
            PluralLabel = Definition.PluralLabel,
            UsedByLabel = presentation.UsedByLabel,
            Icon = presentation.Icon,
            ExtraColumns = Definition.ExtraColumns,
            IndexPage = $"{folder}/Index",
            CreatePage = $"{folder}/Create",
            EditPage = $"{folder}/Edit",
            DeactivatePage = $"{folder}/Deactivate",
            MoveHandler = Move is null ? null : "Move",
            DeleteHandler = "Delete"
        };
    }
}
