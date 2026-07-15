using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class ProjectModel : PageModel
{
    private readonly IProliferationProjectReadService _projectReadService;
    private readonly IAuthorizationService _authorizationService;

    public ProjectModel(
        IProliferationProjectReadService projectReadService,
        IAuthorizationService authorizationService)
    {
        _projectReadService = projectReadService ?? throw new ArgumentNullException(nameof(projectReadService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public ProliferationProjectDetailViewModel Detail { get; private set; } =
        ProliferationProjectDetailViewModel.Empty(0);

    public bool CanManageRecords { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var detail = await _projectReadService.GetProjectAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;

        var submitResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.SubmitProliferationTracker);
        CanManageRecords = submitResult.Succeeded;

        return Page();
    }
}
