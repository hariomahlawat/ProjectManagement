using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class IndexModel : PageModel
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ProliferationAggregateReadService _aggregateReadService;

    public IndexModel(
        IAuthorizationService authorizationService,
        ProliferationAggregateReadService aggregateReadService)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _aggregateReadService = aggregateReadService ?? throw new ArgumentNullException(nameof(aggregateReadService));
    }

    public bool CanManagePreferences { get; private set; }

    public bool CanManageRecords { get; private set; }

    public IReadOnlyList<int> AvailableYears { get; private set; } = Array.Empty<int>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var managePreferencesResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.ManageProliferationPreferences);
        CanManagePreferences = managePreferencesResult.Succeeded;

        var submitResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.SubmitProliferationTracker);
        CanManageRecords = submitResult.Succeeded || CanManagePreferences;

        var maximumYear = DateTime.UtcNow.Year + 1;
        var aggregates = await _aggregateReadService.GetApprovedAggregatesAsync(null, cancellationToken);
        AvailableYears = aggregates
            .Select(x => x.Year)
            .Where(year => year is >= 2000 && year <= maximumYear)
            .Distinct()
            .OrderByDescending(year => year)
            .ToArray();
    }
}
