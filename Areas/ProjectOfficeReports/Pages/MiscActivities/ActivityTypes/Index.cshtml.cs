using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.MiscActivities.ActivityTypes;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewActivityTypes)]
public class IndexModel : PageModel
{
    private readonly IActivityTypeService _service;

    public IndexModel(IActivityTypeService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public IReadOnlyList<ActivityTypeSummary> Items { get; private set; } = Array.Empty<ActivityTypeSummary>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _service.GetSummariesAsync(cancellationToken);
    }
}
