using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.Reports.ProgressReview;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ProgressReview;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProgressReview)]
public sealed class IndexModel : PageModel
{
    private readonly IProgressReviewService _service;

    public IndexModel(IProgressReviewService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public ProgressReviewVm? Report { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var todayIst = IstClock.ToIst(DateTime.UtcNow);
        var today = DateOnly.FromDateTime(todayIst);
        var rangeFrom = From ?? today.AddDays(-29);
        var rangeTo = To ?? today;

        if (rangeTo < rangeFrom)
        {
            (rangeFrom, rangeTo) = (rangeTo, rangeFrom);
        }

        Report = await _service.GetAsync(new ProgressReviewRequest(rangeFrom, rangeTo), cancellationToken);
        return Page();
    }
}
