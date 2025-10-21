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

    public ManageModel(ProliferationManageService manageService)
    {
        _manageService = manageService ?? throw new ArgumentNullException(nameof(manageService));
    }

    public ProliferationListBootVm ListBoot { get; private set; } = default!;
    public ProliferationEditorBootVm EditorBoot { get; private set; } = default!;
    public ProliferationPreferenceOverridesBootVm OverridesBoot { get; private set; } = default!;

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
    }
}
