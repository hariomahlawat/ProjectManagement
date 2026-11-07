using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin;

/// <summary>
/// The Repository document types and Repository offices pages. Both only call
/// <see cref="IDocRepoListService"/>; the rules (target state, row version, delete only when
/// unused, audit) live there. Routes, handler names and form fields are unchanged; the toggle
/// form now also posts the state wanted ("active") and every row form posts its "version".
/// </summary>
public abstract class DocRepoListPageModel : PageModel
{
    private readonly IDocRepoListService _lists;
    private readonly IListOrderNoticeService _order;

    protected DocRepoListPageModel(IDocRepoListService lists, IListOrderNoticeService order)
    {
        _lists = lists;
        _order = order;
        OrderNotice = ListOrderNotice.None(OrderKey);
    }

    protected abstract DocRepoListKind Kind { get; }

    protected abstract string OrderKey { get; }

    /// <summary>"type" or "office", for the page's words.</summary>
    public abstract string Noun { get; }

    /// <summary>The library filter that lists the documents filed under a value.</summary>
    public abstract string LibraryFilter { get; }

    /// <summary>Shown when positions have gaps or repeats (for example every row at 100).</summary>
    public ListOrderNotice OrderNotice { get; private set; }

    public IReadOnlyList<DocRepoListItem> Items { get; private set; } = Array.Empty<DocRepoListItem>();

    [BindProperty]
    public CreateInput Input { get; set; } = new();

    public class CreateInput
    {
        [Required, MaxLength(DocRepoListService.NameMaxLength)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Blank puts the new entry at the end of the list.</summary>
        [Range(-999, 9999)]
        public int? SortOrder { get; set; }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _lists.ListAsync(Kind, cancellationToken);
        OrderNotice = await _order.GetAsync(User, OrderKey, cancellationToken);
    }

    public async Task<IActionResult> OnPostRenumberOrderAsync(CancellationToken cancellationToken)
    {
        var result = await _order.RenumberAsync(User, OrderKey, cancellationToken);
        TempData["ToastMessage"] = result.UserMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var result = await _lists.CreateAsync(Kind, User, Input.Name, Input.SortOrder, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.Name), result.Message);
            await OnGetAsync(cancellationToken);
            return Page();
        }

        TempData["ToastMessage"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string? name, int sortOrder, uint? version, CancellationToken cancellationToken) =>
        Finish(await _lists.UpdateAsync(Kind, User, id, name, sortOrder, version, cancellationToken));

    public async Task<IActionResult> OnPostToggleAsync(int id, bool? active, uint? version, CancellationToken cancellationToken) =>
        Finish(await _lists.SetActiveAsync(Kind, User, id, active, version, cancellationToken));

    public async Task<IActionResult> OnPostDeleteAsync(int id, uint? version, CancellationToken cancellationToken) =>
        Finish(await _lists.DeleteAsync(Kind, User, id, version, cancellationToken));

    private IActionResult Finish(DocRepoListResult result)
    {
        if (result.ErrorCode == "Forbidden")
        {
            return Forbid();
        }

        TempData[result.Succeeded ? "ToastMessage" : "ToastError"] = result.Message;
        return RedirectToPage();
    }
}
