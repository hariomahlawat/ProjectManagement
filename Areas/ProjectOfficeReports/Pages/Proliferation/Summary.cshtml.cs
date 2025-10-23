using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class SummaryModel : PageModel
{
    private readonly IProliferationSummaryReadService _summaryService;

    public SummaryModel(IProliferationSummaryReadService summaryService)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
    }

    public ProliferationSummaryViewModel Summary { get; private set; } = ProliferationSummaryViewModel.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Summary = await _summaryService.GetSummaryAsync(cancellationToken);
    }
}
