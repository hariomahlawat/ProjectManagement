using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IVisitExportService
{
    Task<VisitExportResult> ExportAsync(VisitExportRequest request, CancellationToken cancellationToken);
    Task<VisitExportResult> ExportPdfAsync(VisitExportRequest request, CancellationToken cancellationToken);
}

public sealed class VisitExportService : IVisitExportService
{
    private readonly VisitService _visitService;
    private readonly IVisitExcelWorkbookBuilder _workbookBuilder;
    private readonly IVisitPdfReportBuilder _pdfReportBuilder;
    private readonly IVisitPhotoService _photoService;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<VisitExportService> _logger;

    public VisitExportService(
        VisitService visitService,
        IVisitExcelWorkbookBuilder workbookBuilder,
        IVisitPdfReportBuilder pdfReportBuilder,
        IVisitPhotoService photoService,
        IClock clock,
        IAuditService audit,
        ILogger<VisitExportService> logger)
    {
        _visitService = visitService ?? throw new ArgumentNullException(nameof(visitService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _pdfReportBuilder = pdfReportBuilder ?? throw new ArgumentNullException(nameof(pdfReportBuilder));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VisitExportResult> ExportAsync(VisitExportRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.Success)
        {
            return VisitExportResult.Failure(validation.Error ?? "Export failed.");
        }

        var options = validation.Options!;
        var normalizedQuery = validation.NormalizedQuery;
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

    public async Task<VisitExportResult> ExportPdfAsync(VisitExportRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.Success)
        {
            return VisitExportResult.Failure(validation.Error ?? "Export failed.");
        }

        var options = validation.Options!;
        var normalizedQuery = validation.NormalizedQuery;
        var generatedAt = _clock.UtcNow;
        var rows = await _visitService.ExportForPdfAsync(options, cancellationToken);
        var sections = new List<VisitPdfReportSection>(rows.Count);

        foreach (var row in rows)
        {
            byte[]? photoBytes = null;
            if (row.CoverPhotoId.HasValue)
            {
                var asset = await _photoService.OpenAsync(row.VisitId, row.CoverPhotoId.Value, "md", cancellationToken);
                if (asset is not null)
                {
                    await using var stream = asset.Stream;
                    if (stream.CanSeek)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    }

                    await using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, cancellationToken);
                    photoBytes = buffer.ToArray();
                }
            }

            sections.Add(new VisitPdfReportSection(
                row.VisitId,
                row.DateOfVisit,
                row.VisitTypeName,
                row.VisitorName,
                row.Strength,
                row.PhotoCount,
                row.Remarks,
                photoBytes));
        }

        var content = _pdfReportBuilder.Build(new VisitPdfReportContext(sections, generatedAt, request.StartDate, request.EndDate));
        var file = new VisitExportFile(BuildPdfFileName(request, generatedAt), content, VisitExportFile.PdfContentType);

        await Audit.Events.VisitExported(
                request.RequestedByUserId,
                request.VisitTypeId,
                request.StartDate,
                request.EndDate,
                normalizedQuery,
                rows.Count)
            .WriteAsync(_audit);

        _logger.LogInformation(
            "Generated visit PDF report with {RowCount} sections for user {UserId}",
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

    private static string BuildPdfFileName(VisitExportRequest request, DateTimeOffset generatedAtUtc)
    {
        var fromSegment = request.StartDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "start";
        var toSegment = request.EndDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "end";
        var rangeSegment = request.StartDate.HasValue || request.EndDate.HasValue
            ? $"{fromSegment}-to-{toSegment}"
            : "all";

        return $"visits-{rangeSegment}-{generatedAtUtc:yyyyMMdd'T'HHmmss'Z'}.pdf";
    }

    private static ValidationResult Validate(VisitExportRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
        {
            return ValidationResult.Fail("The requesting user could not be determined.");
        }

        if (request.StartDate.HasValue && request.EndDate.HasValue && request.StartDate > request.EndDate)
        {
            return ValidationResult.Fail("The start date must be on or before the end date.");
        }

        var normalizedQuery = string.IsNullOrWhiteSpace(request.RemarksQuery)
            ? null
            : request.RemarksQuery.Trim();

        var options = new VisitQueryOptions(request.VisitTypeId, request.StartDate, request.EndDate, normalizedQuery);
        return ValidationResult.Success(options, normalizedQuery);
    }

    private sealed record ValidationResult(bool Success, string? Error, VisitQueryOptions? Options, string? NormalizedQuery)
    {
        public static ValidationResult Fail(string error) => new(false, error, null, null);

        public static ValidationResult Success(VisitQueryOptions options, string? normalizedQuery)
            => new(true, null, options, normalizedQuery);
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
    public const string PdfContentType = "application/pdf";
}

public sealed record VisitExportResult(bool Success, VisitExportFile? File, IReadOnlyList<string> Errors)
{
    public static VisitExportResult FromFile(VisitExportFile file) => new(true, file, Array.Empty<string>());

    public static VisitExportResult Failure(params string[] errors)
        => new(false, null, errors.Length == 0 ? new[] { "Export failed." } : errors);
}
