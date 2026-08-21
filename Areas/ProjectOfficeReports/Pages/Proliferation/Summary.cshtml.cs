using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class SummaryModel : PageModel
{
    private readonly IProliferationSummaryReadService _summaryService;
    private readonly IProliferationCardExportService _cardExportService;
    private readonly ApplicationDbContext _db;
    private readonly ProliferationDataQualityService _dataQualityService;
    private readonly ProliferationChronologyQualityService _chronologyQualityService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<SummaryModel> _logger;

    public SummaryModel(
        IProliferationSummaryReadService summaryService,
        IProliferationCardExportService cardExportService,
        ApplicationDbContext db,
        ProliferationDataQualityService dataQualityService,
        ProliferationChronologyQualityService chronologyQualityService,
        IAuthorizationService authorizationService,
        IClock clock,
        IAuditService audit,
        ILogger<SummaryModel> logger)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _cardExportService = cardExportService ?? throw new ArgumentNullException(nameof(cardExportService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _dataQualityService = dataQualityService ?? throw new ArgumentNullException(nameof(dataQualityService));
        _chronologyQualityService = chronologyQualityService ?? throw new ArgumentNullException(nameof(chronologyQualityService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ProliferationSummaryViewModel Summary { get; private set; } = ProliferationSummaryViewModel.Empty;
    public ProliferationOperationalSnapshot OperationalSnapshot { get; private set; } = ProliferationOperationalSnapshot.Empty;
    public int ProjectsTotal { get; private set; }
    public int YearsTotal { get; private set; }
    public int GrandTotal { get; private set; }
    public int GrandAbw { get; private set; }
    public int GrandSdd { get; private set; }
    public string Lede { get; private set; } = string.Empty;
    public bool CanManageRecords { get; private set; }
    public bool CanReviewDataQuality { get; private set; }
    public int DataQualityIssueCount { get; private set; }
    public IReadOnlyList<int> InvalidYears { get; private set; } = Array.Empty<int>();

    public IReadOnlyList<TechnicalCategoryBreakdownRow> TechnicalCategoryBreakdown { get; private set; } =
        Array.Empty<TechnicalCategoryBreakdownRow>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Summary = await _summaryService.GetSummaryAsync(cancellationToken);
        OperationalSnapshot = await _summaryService.GetOperationalSnapshotAsync(
            recentProliferationLimit: 8,
            recentActivityLimit: 5,
            cancellationToken);

        var totals = CalculateTotals(Summary);
        ProjectsTotal = totals.ProjectsTotal;
        YearsTotal = totals.YearsTotal;
        GrandTotal = totals.GrandTotal;
        GrandAbw = totals.GrandAbw;
        GrandSdd = totals.GrandSdd;
        Lede = BuildLede(totals);

        TechnicalCategoryBreakdown = await BuildTechnicalCategoryBreakdownAsync(Summary, cancellationToken);

        var qualitySummary = await _dataQualityService.GetSummaryAsync(cancellationToken);
        DataQualityIssueCount = qualitySummary.CorrectionRequiredCount + qualitySummary.PossibleDuplicateCount;
        InvalidYears = Array.Empty<int>();

        var submitResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.SubmitProliferationTracker);
        CanManageRecords = submitResult.Succeeded;

        var qualityResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.ApproveProliferationTracker);
        CanReviewDataQuality = qualityResult.Succeeded;
    }

    public async Task<FileResult> OnGetExportProjectsAsync(CancellationToken cancellationToken)
    {
        var summary = await _summaryService.GetSummaryAsync(cancellationToken);
        var quality = await _chronologyQualityService.GetApprovedSummaryAsync(
            projectIds: null,
            source: null,
            cancellationToken);
        var metadata = BuildExportMetadata(
            quality,
            ProliferationChronologyQualityService.BuildDisclosure(quality, allTimeReport: true));
        var bytes = _cardExportService.BuildProjectsRanking(summary, metadata);

        await AuditExportAsync(
            "Project totals",
            summary.ByProject.Count,
            quality,
            cancellationToken);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            BuildExportFileName("proliferation-project-totals"));
    }

    public async Task<FileResult> OnGetExportYearBreakdownAsync(CancellationToken cancellationToken)
    {
        var summary = await _summaryService.GetSummaryAsync(cancellationToken);
        var quality = await _chronologyQualityService.GetApprovedSummaryAsync(
            projectIds: null,
            source: null,
            cancellationToken);
        var metadata = BuildExportMetadata(
            quality,
            ProliferationChronologyQualityService.BuildDisclosure(quality, allTimeReport: false));
        var bytes = _cardExportService.BuildYearBreakdown(summary, metadata);

        await AuditExportAsync(
            "Year-wise data",
            summary.ByProjectYear.Count,
            quality,
            cancellationToken);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            BuildExportFileName("proliferation-year-wise"));
    }

    private ProliferationExportMetadata BuildExportMetadata(
        ProliferationChronologyQualitySummary quality,
        string dataQualityMessage)
        => new(
            _clock.UtcNow,
            User.Identity?.Name ?? "Unknown user",
            quality,
            dataQualityMessage);

    private string BuildExportFileName(string stem)
    {
        var generatedAtIst = TimeZoneInfo.ConvertTime(_clock.UtcNow, TimeZoneHelper.GetIst());
        return $"{stem}-{generatedAtIst:yyyyMMdd-HHmmss}-IST.xlsx";
    }

    private async Task AuditExportAsync(
        string exportType,
        int rowCount,
        ProliferationChronologyQualitySummary quality,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _audit.LogAsync(
                "Proliferation.Export",
                $"Exported {exportType} workbook.",
                userId: User.FindFirstValue(ClaimTypes.NameIdentifier),
                userName: User.Identity?.Name,
                data: new Dictionary<string, string?>
                {
                    ["ExportType"] = exportType,
                    ["Rows"] = rowCount.ToString(CultureInfo.InvariantCulture),
                    ["InvalidChronologyRecords"] = quality.ApprovedRecordCount.ToString(CultureInfo.InvariantCulture),
                    ["InvalidChronologyQuantity"] = quality.ReportedQuantity.ToString(CultureInfo.InvariantCulture)
                },
                http: HttpContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The {ExportType} proliferation workbook was generated, but its export audit could not be recorded. TraceId: {TraceId}",
                exportType,
                HttpContext.TraceIdentifier);
        }
    }

    private async Task<IReadOnlyList<TechnicalCategoryBreakdownRow>> BuildTechnicalCategoryBreakdownAsync(
        ProliferationSummaryViewModel summary,
        CancellationToken cancellationToken)
    {
        if (summary.ByProject.Count == 0)
        {
            return Array.Empty<TechnicalCategoryBreakdownRow>();
        }

        var totalsByProject = summary.ByProject.ToDictionary(x => x.ProjectId, x => x.Totals.Total);
        var projectIds = totalsByProject.Keys.ToArray();

        var rows = await _db.Projects
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.TechnicalCategoryId,
                TechnicalCategoryName = x.TechnicalCategory != null
                    ? x.TechnicalCategory.Name
                    : "Uncategorised"
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => totalsByProject.ContainsKey(x.Id))
            .GroupBy(x => new
            {
                x.TechnicalCategoryId,
                Name = string.IsNullOrWhiteSpace(x.TechnicalCategoryName)
                    ? "Uncategorised"
                    : x.TechnicalCategoryName
            })
            .Select(group => new TechnicalCategoryBreakdownRow(
                group.Key.TechnicalCategoryId,
                group.Key.Name,
                group.Sum(x => totalsByProject[x.Id])))
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    public static string FormatActivityTimestamp(DateTime utc)
    {
        var ist = IstClock.ToIst(utc);
        return ist.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " IST";
    }

    public string FormatActivityAge(DateTime utc)
    {
        var nowUtc = _clock.UtcNow.UtcDateTime;
        var elapsed = nowUtc - DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        if (elapsed < TimeSpan.Zero)
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes));
            return $"{minutes} min ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = Math.Max(1, (int)Math.Floor(elapsed.TotalHours));
            return $"{hours} h ago";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            var days = Math.Max(1, (int)Math.Floor(elapsed.TotalDays));
            return days == 1 ? "yesterday" : $"{days} days ago";
        }

        return FormatActivityTimestamp(utc);
    }

    private static SummaryTotals CalculateTotals(ProliferationSummaryViewModel summary)
    {
        if (summary.ByProject.Count > 0)
        {
            return new SummaryTotals(
                summary.ByProject.Count,
                summary.ByYear.Count,
                summary.ByProject.Sum(x => x.Totals.Total),
                summary.ByProject.Sum(x => x.Totals.Abw515),
                summary.ByProject.Sum(x => x.Totals.Sdd));
        }

        return SummaryTotals.Empty;
    }

    private static string BuildLede(SummaryTotals totals)
    {
        if (totals == SummaryTotals.Empty)
        {
            return "No approved proliferation data is available yet.";
        }

        return $"Approved proliferation across {totals.ProjectsTotal.ToString("N0", CultureInfo.InvariantCulture)} " +
               $"{(totals.ProjectsTotal == 1 ? "project" : "projects")} and " +
               $"{totals.YearsTotal.ToString("N0", CultureInfo.InvariantCulture)} " +
               $"{(totals.YearsTotal == 1 ? "year" : "years")}.";
    }

    private sealed record SummaryTotals(
        int ProjectsTotal,
        int YearsTotal,
        int GrandTotal,
        int GrandAbw,
        int GrandSdd)
    {
        public static SummaryTotals Empty { get; } = new(0, 0, 0, 0, 0);
    }
}
