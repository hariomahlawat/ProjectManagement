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
    private readonly IProgressReviewExportService _export;

    public IndexModel(IProgressReviewService service, IProgressReviewExportService export)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _export = export ?? throw new ArgumentNullException(nameof(export));
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public ProgressReviewVm? Report { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var (rangeFrom, rangeTo) = BuildRange();
        Report = await _service.GetAsync(new ProgressReviewRequest(rangeFrom, rangeTo), cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetExportPdfAsync(CancellationToken ct)
    {
        var (rangeFrom, rangeTo) = BuildRange();
        var report = await _service.GetAsync(new ProgressReviewRequest(rangeFrom, rangeTo), ct);
        var pdf = await _export.ExportPdfAsync(report, rangeFrom, rangeTo, ct);

        return File(pdf.Bytes, "application/pdf", pdf.FileName);
    }

    private (DateOnly RangeFrom, DateOnly RangeTo) BuildRange()
    {
        var todayIst = IstClock.ToIst(DateTime.UtcNow);
        var today = DateOnly.FromDateTime(todayIst);
        var rangeFrom = From ?? today.AddDays(-29);
        var rangeTo = To ?? today;

        if (rangeTo < rangeFrom)
        {
            (rangeFrom, rangeTo) = (rangeTo, rangeFrom);
        }

        return (rangeFrom, rangeTo);
    }
}
