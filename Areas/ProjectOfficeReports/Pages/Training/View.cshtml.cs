// -----------------------------------------------------------------------------
// Areas/ProjectOfficeReports/Pages/Training/View.cshtml.cs
// -----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;
using ProjectManagement.Configuration;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTrainingTracker)]
public sealed class ViewModel : PageModel
{
    // -------------------------------------------------------------------------
    // dependencies
    // -------------------------------------------------------------------------
    private readonly IOptionsSnapshot<TrainingTrackerOptions> _options;
    private readonly TrainingTrackerReadService _readService;
    private readonly IAuthorizationService _authorizationService;

    public ViewModel(
        IOptionsSnapshot<TrainingTrackerOptions> options,
        TrainingTrackerReadService readService,
        IAuthorizationService authorizationService)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    // -------------------------------------------------------------------------
    // view-facing properties
    // -------------------------------------------------------------------------
    public TrainingDetailsVm Training { get; private set; } = null!;

    public bool IsFeatureEnabled { get; private set; }

    public bool CanManageTrainingTracker { get; private set; }
    // -------------------------------------------------------------------------
    // GET
    // -------------------------------------------------------------------------
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        IsFeatureEnabled = _options.Value.Enabled;

        var training = await _readService.GetDetailsAsync(id, cancellationToken);
        if (training is null)
        {
            return NotFound();
        }

        Training = training;
        CanManageTrainingTracker = (await _authorizationService.AuthorizeAsync(User, ProjectOfficeReportsPolicies.ManageTrainingTracker)).Succeeded;

        return Page();
    }
}
