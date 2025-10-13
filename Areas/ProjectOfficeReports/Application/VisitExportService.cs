using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IVisitExportService
{
    Task<VisitExportResult> ExportAsync(VisitExportRequest request, CancellationToken cancellationToken);
}

public sealed class VisitExportService : IVisitExportService
{
    private readonly VisitService _visitService;
    private readonly IVisitExcelWorkbookBuilder _workbookBuilder;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<VisitExportService> _logger;

    public VisitExportService(
        VisitService visitService,
        IVisitExcelWorkbookBuilder workbookBuilder,
        IClock clock,
        IAuditService audit,
        ILogger<VisitExportService> logger)
    {
        _visitService = visitService ?? throw new ArgumentNullException(nameof(visitService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VisitExportResult> ExportAsync(VisitExportRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
        {
            return VisitExportResult.Failure("The requesting user could not be determined.");
        }

        if (request.StartDate.HasValue && request.EndDate.HasValue && request.StartDate > request.EndDate)
        {
            return VisitExportResult.Failure("The start date must be on or before the end date.");
        }

        var normalizedQuery = string.IsNullOrWhiteSpace(request.RemarksQuery)
            ? null
            : request.RemarksQuery.Trim();

        var options = new VisitQueryOptions(request.VisitTypeId, request.StartDate, request.EndDate, normalizedQuery);
        var rows = await _visitService.ExportAsync(options, cancellationToken);
        var generatedAt = _clock.UtcNow;

        var content = _workbookBuilder.Build(new VisitExcelWorkbookContext(rows, generatedAt, request.StartDate, request.EndDate));
        var file = new VisitExportFile(BuildFileName(request, generatedAt), content, VisitExportFile.ExcelContentType);

        await Audit.Events.VisitExported(
                request.RequestedByUserId,
                request.VisitTypeId,
                request.StartDate,
                request.EndDate,
                normalizedQuery,
                rows.Count)
            .WriteAsync(_audit);

        _logger.LogInformation(
            "Generated visit export with {RowCount} rows for user {UserId}",
            rows.Count,
            request.RequestedByUserId);

        return VisitExportResult.FromFile(file);
    }

    private static string BuildFileName(VisitExportRequest request, DateTimeOffset generatedAtUtc)
    {
        var fromSegment = request.StartDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "start";
        var toSegment = request.EndDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "end";
        var rangeSegment = request.StartDate.HasValue || request.EndDate.HasValue
            ? $"{fromSegment}-to-{toSegment}"
            : "all";

        return $"visits-{rangeSegment}-{generatedAtUtc:yyyyMMdd'T'HHmmss'Z'}.xlsx";
    }
}

public sealed record VisitExportRequest(
    Guid? VisitTypeId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? RemarksQuery,
    string RequestedByUserId);

public sealed record VisitExportFile(string FileName, byte[] Content, string ContentType)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed record VisitExportResult(bool Success, VisitExportFile? File, IReadOnlyList<string> Errors)
{
    public static VisitExportResult FromFile(VisitExportFile file) => new(true, file, Array.Empty<string>());

    public static VisitExportResult Failure(params string[] errors)
        => new(false, null, errors.Length == 0 ? new[] { "Export failed." } : errors);
}
