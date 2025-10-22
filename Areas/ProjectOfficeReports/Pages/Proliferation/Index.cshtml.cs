using System;
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

    public IndexModel(IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public bool CanManagePreferences { get; private set; }

    public bool CanManageRecords { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var managePreferencesResult = await _authorizationService.AuthorizeAsync(User, ProjectOfficeReportsPolicies.ApproveProliferationTracker);
        CanManagePreferences = managePreferencesResult.Succeeded;

        var submitResult = await _authorizationService.AuthorizeAsync(User, ProjectOfficeReportsPolicies.SubmitProliferationTracker);
        CanManageRecords = submitResult.Succeeded || CanManagePreferences;
    }
}
