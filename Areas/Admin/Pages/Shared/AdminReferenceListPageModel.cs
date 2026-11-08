using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
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

    protected IReferenceListService Lists => _lists;

    protected abstract ReferenceListDefinition Definition { get; }

    /// <summary>The Data checks key of the list's order, or null when the list has no order check.</summary>
    protected abstract string? OrderCheckKey { get; }

    protected abstract AdminReferenceListPresentation Presentation { get; }

    /// <summary>Moves an item up (-1) or down (+1); null when the list has no order to change.</summary>
    protected virtual Func<int, int, CancellationToken, Task<AdminOperationResult>>? Move => null;

    /// <summary>
    /// Deactivates or reactivates an item in place: the id, the state asked for (null on an old
    /// form: the other state) and the row version shown. Null links to a Deactivate page instead.
    /// </summary>
    protected virtual Func<int, bool?, byte[], CancellationToken, Task<AdminOperationResult>>? Toggle => null;

    /// <summary>Edit and Add open a dialog on the page instead of the Edit and Create pages.</summary>
    protected virtual AdminReferenceListEditDialog? EditDialog => null;

    /// <summary>A line under the table's heading.</summary>
    protected virtual string? Note => null;

    /// <summary>Route values the list keeps on every link, form and redirect.</summary>
    protected virtual IReadOnlyDictionary<string, string> ListRouteValues => new Dictionary<string, string>();

    /// <summary>Whether the header links back to Lists used in forms.</summary>
    protected virtual Task<bool> ShowListsCentreLinkAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <summary>The header's main button; by default Add, which opens the Create page.</summary>
    protected virtual AdminPageActionModel? PrimaryAction(string folder) => new()
    {
        Text = Presentation.AddText,
        Href = Url.Page($"{folder}/Create", new { area = "Admin" }),
        Icon = "bi-plus-lg",
        IsPrimary = true
    };

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

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadListAsync(cancellationToken);

    /// <summary>Reads the list and builds the page, for a GET or a POST that shows the page again.</summary>
    protected async Task LoadListAsync(CancellationToken cancellationToken)
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

        Build(page, await ShowListsCentreLinkAsync(cancellationToken));
        await OnListLoadedAsync(cancellationToken);
    }

    /// <summary>Anything else a page shows around the list, read after the list.</summary>
    protected virtual Task OnListLoadedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<IActionResult> OnPostToggleAsync(int id, bool? isActive, string? rowVersion, CancellationToken cancellationToken)
    {
        if (Toggle is null)
        {
            return NotFound();
        }

        Flash(await Toggle(id, isActive, DecodeRowVersion(rowVersion), cancellationToken));
        return RedirectToList();
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

    protected IActionResult RedirectToList()
    {
        var values = new RouteValueDictionary
        {
            ["q"] = Search,
            ["status"] = Status,
            ["pageNumber"] = Math.Max(1, PageNumber),
            ["pageSize"] = PageSize
        };
        foreach (var pair in ListRouteValues)
        {
            values[pair.Key] = pair.Value;
        }

        return RedirectToPage(values);
    }

    protected void Flash(AdminOperationResult result) =>
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

    /// <summary>The page's heading; by default the list's name.</summary>
    protected virtual string Title => Definition.PluralLabel;

    private void Build(ReferenceListPage page, bool showListsCentreLink)
    {
        var presentation = Presentation;
        var folder = presentation.FolderPath.TrimEnd('/');
        var actions = new List<AdminPageActionModel>();
        if (showListsCentreLink)
        {
            actions.Add(new AdminPageActionModel { Text = "Lists used in forms", Href = Url.Page("/MasterData/Index", new { area = "Admin" }), Icon = "bi-arrow-left" });
        }

        if (PrimaryAction(folder) is { } primary)
        {
            actions.Add(primary);
        }

        Header = new AdminPageHeaderModel
        {
            Title = Title,
            Description = AdminNavigationCatalog.GetEntry(presentation.NavigationKey).Description,
            Icon = presentation.Icon,
            Actions = actions
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
            MoveHandler = Move is null || !Definition.HasOrder ? null : "Move",
            DeleteHandler = "Delete",
            ShowOrder = Definition.HasOrder,
            ToggleHandler = Toggle is null ? null : "Toggle",
            OffersAddChild = Definition.IsTree,
            EditDialog = EditDialog,
            Note = Note,
            RouteValues = ListRouteValues
        };
    }
}
