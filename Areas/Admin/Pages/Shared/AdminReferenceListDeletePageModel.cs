using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;

namespace ProjectManagement.Areas.Admin.Pages.Shared;

/// <summary>
/// The older "review deletion" page some lists kept (categories). It now follows the shared
/// list page's one rule (<see cref="IReferenceListService"/>): it says what uses the item and
/// deletes only an item nothing uses. The list page's own Delete does the same in one step.
/// </summary>
public abstract class AdminReferenceListDeletePageModel : PageModel
{
    private readonly IReferenceListService _lists;

    protected AdminReferenceListDeletePageModel(IReferenceListService lists)
    {
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
    }

    protected abstract ReferenceListDefinition Definition { get; }

    public ReferenceListRow? Item { get; private set; }

    public string SingularLabel => Definition.Label;

    public string PluralLabel => Definition.PluralLabel;

    [BindProperty]
    public string RowVersion { get; set; } = string.Empty;

    [BindProperty]
    public string Confirmation { get; set; } = string.Empty;

    public bool CanDelete => Item is { CanDelete: true };

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Item = await _lists.GetItemAsync(Definition.Key, id, cancellationToken);
        if (Item is null) return NotFound();
        RowVersion = Item.RowVersion;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        Item = await _lists.GetItemAsync(Definition.Key, id, cancellationToken);
        if (Item is null) return NotFound();

        if (!CanDelete)
        {
            ModelState.AddModelError(string.Empty, $"Something still uses this {SingularLabel}, so it cannot be deleted. Deactivate it instead.");
            return Page();
        }

        if (!string.Equals(Confirmation?.Trim(), Item.Name, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(Confirmation), $"Type '{Item.Name}' exactly to confirm.");
            return Page();
        }

        byte[] rowVersion;
        try
        {
            rowVersion = string.IsNullOrWhiteSpace(RowVersion) ? Array.Empty<byte>() : Convert.FromBase64String(RowVersion);
        }
        catch (FormatException)
        {
            rowVersion = Array.Empty<byte>();
        }

        var result = await _lists.DeleteAsync(Definition.Key, id, rowVersion, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.UserMessage ?? $"The {SingularLabel} could not be deleted.");
            return Page();
        }

        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.UserMessage;
        return RedirectToPage("Index");
    }
}
