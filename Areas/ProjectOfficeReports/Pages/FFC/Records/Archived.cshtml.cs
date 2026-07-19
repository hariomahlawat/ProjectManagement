using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records;

[Authorize(Roles = "Admin,HoD")]
public sealed class ArchivedModel : PageModel
{
    private readonly IFfcRecordWorkspaceService _workspaceService;
    private readonly IFfcRecordCommandService _recordCommandService;

    public ArchivedModel(
        IFfcRecordWorkspaceService workspaceService,
        IFfcRecordCommandService recordCommandService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _recordCommandService = recordCommandService ?? throw new ArgumentNullException(nameof(recordCommandService));
    }

    public IReadOnlyList<FfcArchivedRecordDto> Records { get; private set; } = Array.Empty<FfcArchivedRecordDto>();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string SafeReturnUrl => ResolveReturnUrl();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ConfigureBreadcrumb();
        Records = await _workspaceService.GetArchivedRecordsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRestoreAsync(
        long id,
        string? rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await _recordCommandService.RestoreAsync(id, rowVersion, cancellationToken);
        TempData["StatusMessage"] = result.Message ??
            (result.Success ? "FFC record restored." : "The record could not be restored.");

        if (result.Success)
        {
            return RedirectToPage(
                "/FFC/Records/Details",
                new
                {
                    area = "ProjectOfficeReports",
                    id,
                    returnUrl = ResolveReturnUrl()
                });
        }

        return RedirectToPage(
            "/FFC/Records/Archived",
            new
            {
                area = "ProjectOfficeReports",
                returnUrl = ResolveReturnUrl()
            });
    }

    private string ResolveReturnUrl()
        => !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" }) ?? "/ProjectOfficeReports/FFC";

    private void ConfigureBreadcrumb()
    {
        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", ResolveReturnUrl()),
            ("Archived records", null));
    }
}
