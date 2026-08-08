using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using ProjectManagement.Services.Notebook;
using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Pages.Notebook;

[Authorize]
public class IndexModel : PageModel
{
    private readonly INotebookService _notebook;
    private readonly UserManager<ApplicationUser> _users;

    public IndexModel(INotebookService notebook, UserManager<ApplicationUser> users)
    {
        _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    // SECTION: Notebook query state only. All mutations are handled by the
    // versioned Notebook API so the module has one authoritative write path.
    [BindProperty(SupportsGet = true)]
    public string View { get; set; } = "home";

    [BindProperty(SupportsGet = true)]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? SelectedId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tag { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool EditLabels { get; set; }

    public NotebookIndexVm Notebook { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = _users.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        // Backwards compatibility for old drawer links. Editing now always opens
        // the modern modal through the ?note= query parameter.
        if (SelectedId.HasValue)
        {
            return RedirectToPage(new
            {
                note = SelectedId,
                view = View,
                query = Query,
                filter = Filter,
                tag = Tag
            });
        }

        // Root Labels navigation opens the in-place label manager over Home.
        if (string.Equals(View, "labels", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(Tag))
        {
            return RedirectToPage(new { view = "home", editLabels = true });
        }

        NormalizeLegacyTypeView();
        Notebook = await _notebook.GetIndexAsync(userId, View, Query, Filter, Tag, selectedId: null, ct);
        return Page();
    }

    private void NormalizeLegacyTypeView()
    {
        if (string.Equals(View, "sticky", StringComparison.OrdinalIgnoreCase))
        {
            View = "home";
            Filter ??= "notes";
        }
        else if (string.Equals(View, "notes", StringComparison.OrdinalIgnoreCase))
        {
            View = "home";
            Filter ??= "notes";
        }
        else if (string.Equals(View, "checklists", StringComparison.OrdinalIgnoreCase))
        {
            View = "home";
            Filter ??= "checklists";
        }
    }
}
