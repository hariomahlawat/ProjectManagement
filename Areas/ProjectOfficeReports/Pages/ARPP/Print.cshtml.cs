using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
public sealed class PrintModel : PageModel
{
    private readonly IArppReadService _readService;
    private readonly IClock _clock;

    public PrintModel(IArppReadService readService, IClock clock)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ArppIssueDetails Issue { get; private set; } = default!;
    public DateTimeOffset GeneratedAtIst { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var issue = await _readService.GetIssueAsync(id, cancellationToken);
        if (issue is null)
        {
            return NotFound();
        }

        Issue = issue;
        GeneratedAtIst = TimeZoneInfo.ConvertTime(_clock.UtcNow, TimeZoneHelper.GetIst());
        return Page();
    }
}
