using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.Projects.Arpp;

[Authorize]
public sealed class PrintModel : PageModel
{
    private readonly IArppLibraryService _libraryService;
    private readonly IClock _clock;

    public PrintModel(IArppLibraryService libraryService, IClock clock)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ArppLibraryDocument Document { get; private set; } = default!;

    public DateTimeOffset GeneratedAtIst { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var document = await _libraryService.GetDocumentAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        Document = document;
        GeneratedAtIst = TimeZoneInfo.ConvertTime(_clock.UtcNow, TimeZoneHelper.GetIst());
        return Page();
    }
}
