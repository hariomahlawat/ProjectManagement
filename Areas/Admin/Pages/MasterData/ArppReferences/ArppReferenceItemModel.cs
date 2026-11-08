using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Areas.Admin.Pages.MasterData.ArppReferences;

/// <summary>
/// The Add and Edit pages of the three ARPP value lists (?list=cfa|funds|dfpds), on the shared
/// list form (docs/ux/admin-review-4-fixes.md, PR 5). Before, both opened in a dialog on the
/// list; every list now adds and edits on a page of its own. Saving goes through
/// <see cref="IArppReferenceDataService.SaveAsync"/>, as the dialog's Save handler did, which the
/// list page keeps for forms opened before this change. The form fields are the dialog's
/// (Input.Kind, Input.Id, Input.Value, Input.Description, Input.SortOrder, Input.RowVersion).
/// </summary>
[Authorize(Policy = AdminPolicies.MasterDataManage)]
[ResponseCache(NoStore = true)]
public sealed class ArppReferenceItemModel : PageModel
{
    private readonly IArppReferenceDataService _referenceData;
    private readonly IReferenceListService _lists;

    public ArppReferenceItemModel(IArppReferenceDataService referenceData, IReferenceListService lists)
    {
        _referenceData = referenceData ?? throw new ArgumentNullException(nameof(referenceData));
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
    }

    /// <summary>Which list: cfa (the default), funds or dfpds.</summary>
    [BindProperty(SupportsGet = true, Name = "list")]
    public string? List { get; set; }

    [BindProperty]
    public IndexModel.ReferenceInputModel Input { get; set; } = new();

    public IndexModel.ArppList Current =>
        IndexModel.AllLists.FirstOrDefault(item => string.Equals(item.Key, List, StringComparison.OrdinalIgnoreCase)) ?? IndexModel.AllLists[0];

    public bool IsEdit { get; private set; }

    /// <summary>Only DFPDS schedules carry a description; their value is the schedule number.</summary>
    public bool HasDescription => Current.Kind == ArppReferenceDataKind.DfpdsSchedule;

    public string ValueLabel => HasDescription ? "Schedule number" : Current.KindLabel;

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken cancellationToken)
    {
        IsEdit = id.HasValue;
        Input = new IndexModel.ReferenceInputModel { Kind = Current.Kind };
        if (id is int existing)
        {
            var item = await _lists.GetItemAsync(Current.Definition.Key, existing, cancellationToken);
            if (item is null)
            {
                return NotFound();
            }

            Input.Id = existing;
            Input.Value = item.Name;
            Input.Description = item.Detail;
            Input.SortOrder = item.SortOrder;
            Input.RowVersion = item.RowVersion;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken cancellationToken)
    {
        IsEdit = id.HasValue;
        // The page's list and id decide what is saved, whatever the form says.
        Input.Kind = Current.Kind;
        Input.Id = id;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _referenceData.SaveAsync(
            new ArppReferenceDataSaveCommand(
                Input.Kind,
                Input.Id,
                Input.Value,
                HasDescription ? Input.Description : null,
                Input.SortOrder,
                Input.RowVersion,
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                User.Identity?.Name),
            cancellationToken);

        if (!result.Success)
        {
            foreach (var pair in result.FieldErrors)
            {
                var key = pair.Key switch
                {
                    nameof(ArppReferenceDataSaveCommand.Value) => "Input.Value",
                    nameof(ArppReferenceDataSaveCommand.Description) => "Input.Description",
                    nameof(ArppReferenceDataSaveCommand.SortOrder) => "Input.SortOrder",
                    _ => string.Empty
                };
                foreach (var message in pair.Value)
                {
                    ModelState.AddModelError(key, message);
                }
            }

            if (result.FieldErrors.Count == 0)
            {
                ModelState.AddModelError(string.Empty, result.Message);
            }

            return Page();
        }

        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.Message;
        return RedirectToPage("./Index", new { list = Current.Key });
    }
}
