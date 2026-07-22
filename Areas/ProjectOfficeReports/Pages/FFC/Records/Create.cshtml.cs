using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records;

[Authorize(Roles = "Admin,HoD")]
public sealed class CreateModel : PageModel
{
    private readonly IFfcRecordWorkspaceService _workspaceService;
    private readonly IFfcRecordCommandService _recordCommandService;

    public CreateModel(
        IFfcRecordWorkspaceService workspaceService,
        IFfcRecordCommandService recordCommandService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _recordCommandService = recordCommandService ?? throw new ArgumentNullException(nameof(recordCommandService));
    }

    [BindProperty]
    public FfcRecordEditorInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IReadOnlyList<FfcCountryOptionDto> Countries { get; private set; } = Array.Empty<FfcCountryOptionDto>();
    public string SafeReturnUrl => ResolveReturnUrl()
        ?? "/ProjectOfficeReports/FFC";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ConfigureBreadcrumb();
        Countries = await _workspaceService.GetCountryOptionsAsync(cancellationToken: cancellationToken);
        Input.Year = (short)DateTime.UtcNow.Year;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ConfigureBreadcrumb();

        if (!ModelState.IsValid)
        {
            Countries = await _workspaceService.GetCountryOptionsAsync(cancellationToken: cancellationToken);
            return Page();
        }

        var result = await _recordCommandService.CreateAsync(
            new FfcRecordCreateCommand(
                CountryId: Input.CountryId,
                Year: Input.Year,
                IpaCompleted: Input.IpaCompleted,
                IpaDate: Input.IpaDate,
                IpaRemarks: Input.IpaRemarks,
                GslCompleted: Input.GslCompleted,
                GslDate: Input.GslDate,
                GslRemarks: Input.GslRemarks,
                OverallRemarks: Input.OverallRemarks,
                CreatedByUserId: User.FindFirstValue(ClaimTypes.NameIdentifier)),
            cancellationToken);

        if (!result.Success)
        {
            ApplyErrors(result, nameof(Input));
            Countries = await _workspaceService.GetCountryOptionsAsync(cancellationToken: cancellationToken);
            return Page();
        }

        TempData["StatusMessage"] = result.Message ?? "FFC record created.";
        return RedirectToPage(
            "/FFC/Records/Details",
            new
            {
                area = "ProjectOfficeReports",
                id = result.EntityId,
                returnUrl = ResolveReturnUrl()
            });
    }

    private string? ResolveReturnUrl()
        => !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" });

    private void ConfigureBreadcrumb()
    {
        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" })),
            ("New record", null));
    }

    private void ApplyErrors(FfcCommandResult result, string prefix)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            ModelState.AddModelError(string.Empty, result.Message);
        }

        if (result.FieldErrors is null)
        {
            return;
        }

        foreach (var pair in result.FieldErrors)
        {
            var key = string.IsNullOrWhiteSpace(pair.Key)
                ? string.Empty
                : $"{prefix}.{pair.Key}";
            foreach (var message in pair.Value)
            {
                ModelState.AddModelError(key, message);
            }
        }
    }
}
