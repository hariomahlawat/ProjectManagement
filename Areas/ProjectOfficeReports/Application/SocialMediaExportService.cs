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

public interface ISocialMediaExportService
{
    Task<SocialMediaExportResult> ExportAsync(SocialMediaExportRequest request, CancellationToken cancellationToken);
    Task<SocialMediaExportResult> ExportPdfAsync(SocialMediaExportRequest request, CancellationToken cancellationToken);
}

public sealed class SocialMediaExportService : ISocialMediaExportService
{
    private readonly SocialMediaEventService _eventService;
    private readonly ISocialMediaExcelWorkbookBuilder _workbookBuilder;
    private readonly ISocialMediaPdfReportBuilder _pdfReportBuilder;
    private readonly ISocialMediaEventPhotoService _photoService;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<SocialMediaExportService> _logger;

    public SocialMediaExportService(
        SocialMediaEventService eventService,
        ISocialMediaExcelWorkbookBuilder workbookBuilder,
        ISocialMediaPdfReportBuilder pdfReportBuilder,
        ISocialMediaEventPhotoService photoService,
        IClock clock,
        IAuditService audit,
        ILogger<SocialMediaExportService> logger)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _pdfReportBuilder = pdfReportBuilder ?? throw new ArgumentNullException(nameof(pdfReportBuilder));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SocialMediaExportResult> ExportAsync(SocialMediaExportRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.Success)
        {
            return SocialMediaExportResult.Failure(validation.Error ?? "Export failed.");
        }

        var options = validation.Options!;
        var normalizedQuery = validation.NormalizedQuery;
        var normalizedPlatform = validation.NormalizedPlatform;
        var generatedAt = _clock.UtcNow;

        var rows = await _eventService.ExportAsync(options, cancellationToken);
        var content = _workbookBuilder.Build(new SocialMediaExcelWorkbookContext(
            rows,
            generatedAt,
            request.StartDate,
            request.EndDate,
            normalizedPlatform));

        var file = new SocialMediaExportFile(BuildFileName(request, generatedAt), content, SocialMediaExportFile.ExcelContentType);

        await Audit.Events.SocialMediaEventExported(
                request.RequestedByUserId,
                request.EventTypeId,
                request.StartDate,
                request.EndDate,
                normalizedQuery,
                normalizedPlatform,
                request.OnlyActiveEventTypes,
                rows.Count)
            .WriteAsync(_audit);

        _logger.LogInformation(
            "Generated social media event export with {RowCount} rows for user {UserId}",
            rows.Count,
            request.RequestedByUserId);

        return SocialMediaExportResult.FromFile(file);
    }

    public async Task<SocialMediaExportResult> ExportPdfAsync(SocialMediaExportRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (!validation.Success)
        {
            return SocialMediaExportResult.Failure(validation.Error ?? "Export failed.");
        }

        var options = validation.Options!;
        var normalizedQuery = validation.NormalizedQuery;
        var normalizedPlatform = validation.NormalizedPlatform;
        var generatedAt = _clock.UtcNow;

        var rows = await _eventService.ExportForPdfAsync(options, cancellationToken);
        var sections = new List<SocialMediaPdfReportSection>(rows.Count);

        foreach (var row in rows)
        {
            byte[]? coverPhoto = null;
            if (row.CoverPhotoId.HasValue)
            {
                var asset = await _photoService.OpenAsync(row.EventId, row.CoverPhotoId.Value, "feed", cancellationToken);
                if (asset is not null)
                {
                    await using var stream = asset.Stream;
                    if (stream.CanSeek)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    }

                    await using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, cancellationToken);
                    coverPhoto = buffer.ToArray();
                }
            }

            sections.Add(new SocialMediaPdfReportSection(
                row.EventId,
                row.DateOfEvent,
                row.EventTypeName,
                row.Title,
                row.Platform,
                row.Reach,
                row.PhotoCount,
                row.Description,
                coverPhoto));
        }

        var content = _pdfReportBuilder.Build(new SocialMediaPdfReportContext(
            sections,
            generatedAt,
            request.StartDate,
            request.EndDate,
            normalizedPlatform));

        var file = new SocialMediaExportFile(BuildPdfFileName(request, generatedAt), content, SocialMediaExportFile.PdfContentType);

        await Audit.Events.SocialMediaEventExported(
                request.RequestedByUserId,
                request.EventTypeId,
                request.StartDate,
                request.EndDate,
                normalizedQuery,
                normalizedPlatform,
                request.OnlyActiveEventTypes,
                rows.Count)
            .WriteAsync(_audit);

        _logger.LogInformation(
            "Generated social media event PDF report with {RowCount} sections for user {UserId}",
            rows.Count,
            request.RequestedByUserId);

        return SocialMediaExportResult.FromFile(file);
    }

    private static string BuildFileName(SocialMediaExportRequest request, DateTimeOffset generatedAtUtc)
    {
        var fromSegment = request.StartDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "start";
        var toSegment = request.EndDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "end";
        var rangeSegment = request.StartDate.HasValue || request.EndDate.HasValue
            ? $"{fromSegment}-to-{toSegment}"
            : "all";

        return $"social-media-events-{rangeSegment}-{generatedAtUtc:yyyyMMdd'T'HHmmss'Z'}.xlsx";
    }

    private static string BuildPdfFileName(SocialMediaExportRequest request, DateTimeOffset generatedAtUtc)
    {
        var fromSegment = request.StartDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "start";
        var toSegment = request.EndDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "end";
        var rangeSegment = request.StartDate.HasValue || request.EndDate.HasValue
            ? $"{fromSegment}-to-{toSegment}"
            : "all";

        return $"social-media-events-{rangeSegment}-{generatedAtUtc:yyyyMMdd'T'HHmmss'Z'}.pdf";
    }

    private static ValidationResult Validate(SocialMediaExportRequest request)
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

        var normalizedQuery = string.IsNullOrWhiteSpace(request.SearchQuery)
            ? null
            : request.SearchQuery.Trim();

        var normalizedPlatform = string.IsNullOrWhiteSpace(request.Platform)
            ? null
            : request.Platform.Trim();

        var options = new SocialMediaEventQueryOptions(
            request.EventTypeId,
            request.StartDate,
            request.EndDate,
            normalizedQuery,
            normalizedPlatform,
            request.OnlyActiveEventTypes);

        return ValidationResult.CreateSuccess(options, normalizedQuery, normalizedPlatform);
    }

    private sealed record ValidationResult(
        bool Success,
        string? Error,
        SocialMediaEventQueryOptions? Options,
        string? NormalizedQuery,
        string? NormalizedPlatform)
    {
        public static ValidationResult Fail(string error) => new(false, error, null, null, null);

        public static ValidationResult CreateSuccess(
            SocialMediaEventQueryOptions options,
            string? normalizedQuery,
            string? normalizedPlatform)
            => new(true, null, options, normalizedQuery, normalizedPlatform);
    }
}

public sealed record SocialMediaExportRequest(
    Guid? EventTypeId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? SearchQuery,
    string? Platform,
    bool OnlyActiveEventTypes,
    string RequestedByUserId);

public sealed record SocialMediaExportFile(string FileName, byte[] Content, string ContentType)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfContentType = "application/pdf";
}

public sealed record SocialMediaExportResult(bool Success, SocialMediaExportFile? File, IReadOnlyList<string> Errors)
{
    public static SocialMediaExportResult FromFile(SocialMediaExportFile file) => new(true, file, Array.Empty<string>());

    public static SocialMediaExportResult Failure(params string[] errors)
        => new(false, null, errors.Length == 0 ? new[] { "Export failed." } : errors);
}
