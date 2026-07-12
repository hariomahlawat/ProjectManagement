using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Ingestion;

namespace ProjectManagement.Areas.Admin.Pages.Documents;

[Authorize(Policy = AdminPolicies.IngestionManage)]
public sealed class IngestExternalPdfsModel : PageModel
{
    private readonly IPdfIngestionCoordinator _coordinator;
    private readonly IAdminTimeService _time;

    public IngestExternalPdfsModel(
        IPdfIngestionCoordinator coordinator,
        IAdminTimeService time)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public PdfIngestionRunResult? Result { get; private set; }
    public bool IsRunning => _coordinator.IsRunning;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var operation = await _coordinator.RunAsync(cancellationToken);
        if (!operation.Succeeded)
        {
            TempData[FlashMessageKeys.AdminPdfIngestionError] = !string.IsNullOrWhiteSpace(operation.TraceId)
                ? $"{operation.UserMessage} Trace reference: {operation.TraceId}."
                : operation.UserMessage;
            return RedirectToPage();
        }

        Result = operation.Value;
        ViewData["StatusMessage"] = operation.UserMessage;
        return Page();
    }

    public string FormatTime(DateTimeOffset utc) => _time.FormatIst(utc);
}
