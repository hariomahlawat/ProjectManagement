using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
public sealed class ManageModel : PageModel
{
    private readonly ProliferationManageService _manageService;
    private readonly IAuthorizationService _authorizationService;

    public ManageModel(
        ProliferationManageService manageService,
        IAuthorizationService authorizationService)
    {
        _manageService = manageService ?? throw new ArgumentNullException(nameof(manageService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public ProliferationListBootVm ListBoot { get; private set; } = default!;
    public ProliferationEditorBootVm EditorBoot { get; private set; } = default!;
    public ProliferationPreferenceOverridesBootVm OverridesBoot { get; private set; } = default!;
    public bool CanApproveRecords { get; private set; }

    public async Task OnGetAsync(
        int? projectId,
        ProliferationSource? source,
        int? year,
        ProliferationRecordKind? kind,
        CancellationToken cancellationToken)
    {
        ListBoot = await _manageService.GetListBootAsync(projectId, source, year, kind, cancellationToken);
        EditorBoot = await _manageService.GetEditorBootAsync(projectId, source, year, kind, cancellationToken);
        OverridesBoot = await _manageService.GetPreferenceOverridesBootAsync(projectId, source, year, kind, cancellationToken);

        var approvalResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.ApproveProliferationTracker);
        CanApproveRecords = approvalResult.Succeeded;
    }
}
