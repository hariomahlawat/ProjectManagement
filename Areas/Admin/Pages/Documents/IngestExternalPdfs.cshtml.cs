using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Ingestion;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Documents;

[Authorize(Policy = AdminPolicies.IngestionManage)]
public sealed class IngestExternalPdfsModel : PageModel
{
    private readonly IPdfIngestionCoordinator _coordinator;
    private readonly IPdfIngestionRunHistory _history;
    private readonly IAdminTimeService _time;
    private readonly IAdminNavigationUrlBuilder _navigation;
    private readonly IHostApplicationLifetime _lifetime;

    public IngestExternalPdfsModel(
        IPdfIngestionCoordinator coordinator,
        IPdfIngestionRunHistory history,
        IAdminTimeService time,
        IAdminNavigationUrlBuilder navigation,
        IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    [BindProperty(SupportsGet = true)] public Guid? RunId { get; set; }
    public AdminPageHeaderModel Header { get; private set; } = new();
    public PdfIngestionRunRecord? Result { get; private set; }
    public IReadOnlyList<PdfIngestionRunRecord> RecentRuns { get; private set; } = Array.Empty<PdfIngestionRunRecord>();
    public bool IsRunning => _coordinator.IsRunning;

    public void OnGet()
    {
        Load();
    }

    public async Task<IActionResult> OnPostRunAsync()
    {
        // The run is not tied to the request: a closed tab or a browser/proxy timeout used to
        // cancel it part-way, with no run history recorded. It now runs to completion and
        // records its result; only an application shutdown cancels it (audited as cancelled).
        var operation = await _coordinator.RunAsync(_lifetime.ApplicationStopping);
        if (!operation.Succeeded || operation.Value is null)
        {
            TempData[FlashMessageKeys.AdminPdfIngestionError] = !string.IsNullOrWhiteSpace(operation.TraceId)
                ? $"{operation.UserMessage} Trace reference: {operation.TraceId}."
                : operation.UserMessage;
            return RedirectToPage();
        }

        TempData[FlashMessageKeys.AdminPdfIngestionSuccess] = operation.UserMessage;
        return RedirectToPage(new { RunId = operation.Value.RunId });
    }

    public IActionResult OnGetFailureReport(Guid runId)
    {
        var run = _history.Get(runId);
        if (run is null || run.Failures.Count == 0) return NotFound();

        var csv = new StringBuilder();
        csv.AppendLine("Source,Source item,File name,Result");
        foreach (var failure in run.Failures)
        {
            SafeCsv.AppendRow(csv, failure.Source, failure.SourceItemId, failure.FileName, failure.Message);
        }

        return File(
            SafeCsv.ToUtf8WithBom(csv.ToString()),
            "text/csv; charset=utf-8",
            $"pdf-ingestion-failures-{run.CompletedAtUtc:yyyyMMdd-HHmmss}.csv");
    }

    public string FormatTime(DateTimeOffset? utc) => _time.FormatIst(utc);
    public string FormatDuration(TimeSpan duration) => duration.TotalMinutes >= 1
        ? $"{duration.TotalMinutes:0.#} min"
        : $"{Math.Max(0, duration.TotalSeconds):0} sec";

    private void Load()
    {
        RecentRuns = _history.GetRecent(10);
        Result = RunId.HasValue ? _history.Get(RunId.Value) : RecentRuns.FirstOrDefault();
        Header = new AdminPageHeaderModel
        {
            Title = "PDF ingestion",
            Description = "Adds the PDFs attached to FFC, IPR and activity records to the document repository, so they can be searched. PDFs already there are skipped, and only one run can happen at a time.",
            Icon = "bi-file-earmark-arrow-up",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Data jobs",
                    Href = _navigation.GetPath(HttpContext, AdminNavigationKeys.MaintenanceCentre),
                    Icon = "bi-arrow-left"
                },
                new AdminPageActionModel
                {
                    Text = "Audit log",
                    Href = Url.Page("/Logs/Index", new { area = "Admin", Category = "pdf-ingestion" }) ?? "#",
                    Icon = "bi-journal-text"
                }
            }
        };
    }
}
