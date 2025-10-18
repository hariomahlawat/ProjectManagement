using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation.Reconciliation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageProliferationPreferences)]
public sealed class IndexModel : PageModel
{
    private readonly ProliferationTrackerReadService _trackerService;
    private readonly IUserContext _userContext;

    public IndexModel(ProliferationTrackerReadService trackerService, IUserContext userContext)
    {
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    [BindProperty(SupportsGet = true)]
    public ProliferationSource? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    public IReadOnlyList<ProliferationTrackerRow> Rows { get; private set; } = Array.Empty<ProliferationTrackerRow>();

    public IReadOnlyList<int> YearOptions { get; private set; } = Array.Empty<int>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        Source ??= ProliferationSource.Sdd;

        var filter = new ProliferationTrackerFilter
        {
            Source = Source,
            Year = Year,
            UserId = userId
        };

        var rows = await _trackerService.GetAsync(filter, cancellationToken);
        Rows = rows;

        YearOptions = rows
            .Select(r => r.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToArray();

        return Page();
    }
}
