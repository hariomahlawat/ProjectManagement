using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class ReportsModel : PageModel
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly ProliferationExportOptions _exportOptions;

    public ReportsModel(
        IAuthorizationService authorizationService,
        IClock clock,
        IOptions<ProliferationExportOptions> exportOptions)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _exportOptions = exportOptions?.Value ?? throw new ArgumentNullException(nameof(exportOptions));
    }

    public bool CanManageRecords { get; private set; }

    public int MinimumYear => ProliferationYearPolicy.MinimumYear;

    public int MaximumYear => ProliferationYearPolicy.GetMaximumYear(_clock.UtcNow);

    public int CurrentYear => _clock.UtcNow.Year;

    public int ExportTimeoutMilliseconds
        => Math.Clamp(_exportOptions.TimeoutSeconds, 30, 600) * 1_000;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var submitResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.SubmitProliferationTracker);
        CanManageRecords = submitResult.Succeeded;
    }
}
