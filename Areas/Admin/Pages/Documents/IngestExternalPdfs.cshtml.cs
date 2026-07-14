using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
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

    public IngestExternalPdfsModel(
        IPdfIngestionCoordinator coordinator,
        IPdfIngestionRunHistory history,
        IAdminTimeService time,
        IAdminNavigationUrlBuilder navigation)
    {
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

    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        var operation = await _coordinator.RunAsync(cancellationToken);
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
            csv.Append(Escape(failure.Source)).Append(',')
                .Append(Escape(failure.SourceItemId)).Append(',')
                .Append(Escape(failure.FileName)).Append(',')
                .Append(Escape(failure.Message)).AppendLine();
        }

        return File(
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
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
            Eyebrow = "Controlled maintenance",
            Title = "PDF ingestion",
            Description = "Link eligible FFC, IPR and Activity PDFs into the searchable document repository through a concurrency-protected, audited run.",
            Icon = "bi-file-earmark-arrow-up",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Maintenance centre",
                    Href = _navigation.GetPath(HttpContext, AdminNavigationKeys.MaintenanceCentre),
                    Icon = "bi-arrow-left"
                },
                new AdminPageActionModel
                {
                    Text = "Audit logs",
                    Href = Url.Page("/Logs/Index", new { area = "Admin", Action = "PdfIngestion" }) ?? "#",
                    Icon = "bi-journal-text"
                }
            }
        };
    }

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@') text = "'" + text;
        return '"' + text.Replace("\"", "\"\"") + '"';
    }
}
