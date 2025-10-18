using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Services;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IProliferationExportService
{
    Task<ProliferationExportResult> ExportAsync(ProliferationExportRequest request, CancellationToken cancellationToken);
}

public sealed class ProliferationExportService : IProliferationExportService
{
    private readonly ProliferationTrackerReadService _readService;
    private readonly IProliferationExcelWorkbookBuilder _workbookBuilder;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<ProliferationExportService> _logger;

    public ProliferationExportService(
        ProliferationTrackerReadService readService,
        IProliferationExcelWorkbookBuilder workbookBuilder,
        IClock clock,
        IAuditService audit,
        ILogger<ProliferationExportService> logger)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProliferationExportResult> ExportAsync(ProliferationExportRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
        {
            return ProliferationExportResult.Failure("We could not determine who requested the export.");
        }

        if (request.YearFrom.HasValue && request.YearTo.HasValue && request.YearFrom > request.YearTo)
        {
            return ProliferationExportResult.Failure("The starting year cannot be after the ending year.");
        }

        var generatedAtUtc = _clock.UtcNow;
        var filters = BuildFilters(request);
        var rows = new List<ProliferationTrackerRow>();

        foreach (var filter in filters)
        {
            var filterRows = await _readService.GetAsync(filter, cancellationToken);
            rows.AddRange(FilterRows(filterRows, request));
        }

        if (rows.Count == 0)
        {
            _logger.LogInformation(
                "Proliferation export produced no rows for user {UserId} with filters Source={Source}, YearFrom={YearFrom}, YearTo={YearTo}, SponsoringUnit={SponsoringUnitId}, Simulator={SimulatorUserId}.",
                request.RequestedByUserId,
                request.Source,
                request.YearFrom,
                request.YearTo,
                request.SponsoringUnitId,
                request.SimulatorUserId);
        }

        var content = _workbookBuilder.Build(new ProliferationExcelWorkbookContext(rows, generatedAtUtc, request));
        var file = new ProliferationExportFile(BuildFileName(request, generatedAtUtc), content, ProliferationExportFile.ExcelContentType);

        _logger.LogInformation(
            "Generated proliferation export with {RowCount} rows for user {UserId}.",
            rows.Count,
            request.RequestedByUserId);

        await Audit.Events.ProliferationExportGenerated(
                request.RequestedByUserId,
                request.Source,
                request.YearFrom,
                request.YearTo,
                request.SponsoringUnitId,
                request.SimulatorUserId,
                request.SearchTerm,
                rows.Count,
                file.FileName)
            .WriteAsync(_audit);

        return ProliferationExportResult.FromFile(file);
    }

    private static string BuildFileName(ProliferationExportRequest request, DateTimeOffset generatedAtUtc)
    {
        var sourceSegment = request.Source?.ToString().ToLowerInvariant() ?? "all";
        var fromSegment = request.YearFrom?.ToString(CultureInfo.InvariantCulture) ?? "start";
        var toSegment = request.YearTo?.ToString(CultureInfo.InvariantCulture) ?? "end";
        var yearSegment = request.YearFrom.HasValue || request.YearTo.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "{0}-to-{1}", fromSegment, toSegment)
            : "all-years";

        return $"proliferation-tracker-{sourceSegment}-{yearSegment}-{generatedAtUtc:yyyyMMdd'T'HHmmss'Z'}.xlsx";
    }

    private static IReadOnlyList<ProliferationTrackerFilter> BuildFilters(ProliferationExportRequest request)
    {
        var baseFilter = new ProliferationTrackerFilter
        {
            ProjectSearchTerm = request.SearchTerm,
            Source = request.Source,
            SponsoringUnitId = request.SponsoringUnitId,
            SimulatorUserId = request.SimulatorUserId,
            UserId = request.RequestedByUserId
        };

        if (!request.YearFrom.HasValue && !request.YearTo.HasValue)
        {
            return new[] { baseFilter };
        }

        if (request.YearFrom.HasValue && request.YearTo.HasValue)
        {
            var filters = new List<ProliferationTrackerFilter>();
            for (var year = request.YearFrom.Value; year <= request.YearTo.Value; year++)
            {
                filters.Add(baseFilter with { Year = year });
            }

            return filters;
        }

        var targetYear = request.YearFrom ?? request.YearTo;
        return new[] { baseFilter with { Year = targetYear } };
    }

    private static IEnumerable<ProliferationTrackerRow> FilterRows(
        IReadOnlyList<ProliferationTrackerRow> rows,
        ProliferationExportRequest request)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<ProliferationTrackerRow>();
        }

        if (!request.YearFrom.HasValue && !request.YearTo.HasValue)
        {
            return rows;
        }

        var from = request.YearFrom ?? request.YearTo;
        var to = request.YearTo ?? request.YearFrom;

        if (!from.HasValue && !to.HasValue)
        {
            return rows;
        }

        var min = from ?? int.MinValue;
        var max = to ?? int.MaxValue;

        return rows.Where(r => r.Year >= min && r.Year <= max).ToList();
    }
}

public sealed record ProliferationExportRequest(
    ProliferationSource? Source,
    int? YearFrom,
    int? YearTo,
    int? SponsoringUnitId,
    string? SimulatorUserId,
    string? SearchTerm,
    string RequestedByUserId);

public sealed record ProliferationExportFile(string FileName, byte[] Content, string ContentType)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed record ProliferationExportResult(bool Success, ProliferationExportFile? File, IReadOnlyList<string> Errors)
{
    public static ProliferationExportResult FromFile(ProliferationExportFile file) => new(true, file, Array.Empty<string>());

    public static ProliferationExportResult Failure(params string[] errors)
    {
        if (errors is { Length: > 0 })
        {
            return new ProliferationExportResult(false, null, errors);
        }

        return new ProliferationExportResult(false, null, new[] { "Export failed." });
    }
}
