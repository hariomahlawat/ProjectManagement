using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin;

/// <summary>A repository list as its Add and Edit pages see it.</summary>
public sealed record DocRepoListForm(string Folder, DocRepoListKind Kind, ReferenceListDefinition Definition, string Icon, string Example);

/// <summary>
/// The Add and Edit pages of Repository document types and Repository offices, on the shared
/// list form (docs/ux/admin-review-4-fixes.md, PR 5). Before, both opened in a dialog on the list;
/// every list now adds and edits on a page of its own. Create.cshtml and Edit.cshtml in each
/// folder use this model; the list comes from the folder. Saving goes through
/// <see cref="IDocRepoListService"/>, which checks the permission, refuses a duplicate name or an
/// out-of-date version and writes the repository's audit, as the dialog did. The list page keeps
/// its Create and Update handlers for forms opened before this change.
/// </summary>
[Authorize(Policy = Policies.Documents.ManageCategories)]
public sealed class DocRepoListItemModel : PageModel
{
    public static IReadOnlyList<DocRepoListForm> All { get; } = new[]
    {
        new DocRepoListForm("DocumentCategories", DocRepoListKind.DocumentType, ReferenceListCatalog.DocumentTypes, "bi-folder2", "Minutes"),
        new DocRepoListForm("OfficeCategories", DocRepoListKind.Office, ReferenceListCatalog.DocumentOffices, "bi-building-gear", "Plans branch")
    };

    private readonly IDocRepoListService _lists;
    private readonly IReferenceListService _referenceLists;

    public DocRepoListItemModel(IDocRepoListService lists, IReferenceListService referenceLists)
    {
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
        _referenceLists = referenceLists ?? throw new ArgumentNullException(nameof(referenceLists));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public DocRepoListForm List { get; private set; } = All[0];

    public bool IsEdit { get; private set; }

    /// <summary>The list of a page, from its path ("/Admin/DocumentCategories/Edit").</summary>
    public static DocRepoListForm? FromPagePath(string? viewEnginePath)
    {
        var parts = (viewEnginePath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "Admin"
            ? All.FirstOrDefault(list => string.Equals(list.Folder, parts[1], StringComparison.Ordinal))
            : null;
    }

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken cancellationToken)
    {
        if (!Resolve(id))
        {
            return NotFound();
        }

        if (id is int existing)
        {
            var item = await _referenceLists.GetItemAsync(List.Definition.Key, existing, cancellationToken);
            if (item is null)
            {
                return NotFound();
            }

            Input = new InputModel { Name = item.Name, SortOrder = item.SortOrder, RowVersion = item.RowVersion };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken cancellationToken)
    {
        if (!Resolve(id))
        {
            return NotFound();
        }

        if (id.HasValue && Input.SortOrder is null)
        {
            ModelState.AddModelError("Input.SortOrder", "Enter the order number.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = id is int existing
            ? await _lists.UpdateAsync(List.Kind, User, existing, Input.Name, Input.SortOrder!.Value, ToXmin(Input.RowVersion), cancellationToken)
            : await _lists.CreateAsync(List.Kind, User, Input.Name, Input.SortOrder, cancellationToken);

        switch (result.ErrorCode)
        {
            case null when result.Succeeded:
                TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.Message;
                return RedirectToPage("./Index");
            case "Forbidden":
                return Forbid();
            case "DuplicateName":
            case "ValidationFailed":
                ModelState.AddModelError(result.Message.StartsWith("Position", StringComparison.Ordinal) ? "Input.SortOrder" : "Input.Name", result.Message);
                return Page();
            default:
                // Out of date or gone: the list shows the message and the current values.
                TempData[FlashMessageKeys.AdminMasterDataError] = result.Message;
                return RedirectToPage("./Index");
        }
    }

    private bool Resolve(int? id)
    {
        if (FromPagePath(PageContext?.ActionDescriptor?.ViewEnginePath ?? RouteData?.Values["page"] as string) is not { } list)
        {
            return false;
        }

        List = list;
        IsEdit = id.HasValue;
        return true;
    }

    // The list carries xmin as four bytes (base64), like every other list's row version.
    private static uint? ToXmin(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(rowVersion);
            return bytes.Length == sizeof(uint) ? BitConverter.ToUInt32(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter a name.")]
        [MaxLength(DocRepoListService.NameMaxLength)]
        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Blank on Add puts the new entry at the end of the list.</summary>
        [Range(-999, 9999)]
        [Display(Name = "Order in lists")]
        public int? SortOrder { get; set; }

        public string? RowVersion { get; set; }
    }
}
