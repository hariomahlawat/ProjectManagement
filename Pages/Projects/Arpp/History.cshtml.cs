using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Pages.Projects.Arpp;

[Authorize]
public sealed class HistoryModel : PageModel
{
    private readonly IArppLibraryService _libraryService;

    public HistoryModel(IArppLibraryService libraryService)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
    }

    [BindProperty(SupportsGet = true)]
    public int ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FinancialYearStart { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Query { get; set; }

    public ArppLibraryProjectHistory History { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var history = await _libraryService.GetProjectHistoryAsync(ProjectId, cancellationToken);
        if (history is null || history.Rows.Count == 0)
        {
            return NotFound();
        }

        History = history;
        FinancialYearStart ??= history.Rows[0].FinancialYearStart;
        Query = ArppLibrarySearch.Normalize(Query);
        return Page();
    }
}
