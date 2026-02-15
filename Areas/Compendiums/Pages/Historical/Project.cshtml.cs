using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Compendiums.Application;
using ProjectManagement.Areas.Compendiums.Application.Dto;

namespace ProjectManagement.Areas.Compendiums.Pages.Historical;

[Authorize]
public sealed class ProjectModel : PageModel
{
    private readonly ICompendiumReadService _readService;

    public ProjectModel(ICompendiumReadService readService)
    {
        _readService = readService;
    }

    public CompendiumProjectDetailDto Project { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var project = await _readService.GetProjectAsync(id, includeHistoricalExtras: true, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        Project = project;
        return Page();
    }
}
