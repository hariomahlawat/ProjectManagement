using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
public sealed class ProjectHistoryModel : PageModel
{
    private readonly IArppReadService _readService;

    public ProjectHistoryModel(IArppReadService readService)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
    }

    public ArppProjectHistory History { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(int projectId, CancellationToken cancellationToken)
    {
        var history = await _readService.GetProjectHistoryAsync(projectId, cancellationToken);
        if (history is null)
        {
            return NotFound();
        }

        History = history;
        return Page();
    }
}
