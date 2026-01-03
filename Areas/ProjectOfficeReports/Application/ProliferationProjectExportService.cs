using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

// SECTION: Export contract
public interface IProliferationProjectExportService
{
    Task<ProliferationProjectExportResult> ExportAsync(
        ProliferationProjectExportRequest request,
        CancellationToken cancellationToken);
}

// SECTION: Export implementation
public sealed class ProliferationProjectExportService : IProliferationProjectExportService
{
    private readonly ApplicationDbContext _db;
    private readonly IProliferationProjectReadService _projectReadService;
    private readonly IProliferationProjectExcelWorkbookBuilder _workbookBuilder;
    private readonly IClock _clock;
    private readonly ILogger<ProliferationProjectExportService> _logger;

    public ProliferationProjectExportService(
        ApplicationDbContext db,
        IProliferationProjectReadService projectReadService,
        IProliferationProjectExcelWorkbookBuilder workbookBuilder,
        IClock clock,
        ILogger<ProliferationProjectExportService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _projectReadService = projectReadService ?? throw new ArgumentNullException(nameof(projectReadService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProliferationProjectExportResult> ExportAsync(
        ProliferationProjectExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await ValidateAsync(request, cancellationToken);
        if (!validation.Succeeded)
        {
            return ProliferationProjectExportResult.Failure(validation.Error ?? "Export failed.");
        }

        var filters = validation.Filters!;
        var aggregates = await _projectReadService.GetAggregatesAsync(filters.ToAggregationRequest(), cancellationToken);
        var generatedAt = _clock.UtcNow;

        var metadata = new ProliferationProjectExportFilterMetadata(
            filters.Years,
            filters.FromDate,
            filters.ToDate,
            filters.SourceLabel ?? string.Empty,
            filters.ProjectCategoryName ?? (filters.ProjectCategoryId.HasValue ? $"Category #{filters.ProjectCategoryId.Value}" : string.Empty),
            filters.TechnicalCategoryName ?? (filters.TechnicalCategoryId.HasValue ? $"Technical #{filters.TechnicalCategoryId.Value}" : string.Empty),
            filters.Search);

        var workbookContent = _workbookBuilder.Build(new ProliferationProjectExcelWorkbookContext(
            aggregates,
            generatedAt,
            filters.RequestedByUserId,
            metadata));

        var fileName = BuildFileName(filters, generatedAt);
        var file = new ProliferationProjectExportFile(fileName, workbookContent, ProliferationProjectExportFile.ExcelContentType);

        _logger.LogInformation(
            "Generated proliferation project export for user {UserId}",
            filters.RequestedByUserId);

        return ProliferationProjectExportResult.FromFile(file);
    }

    // SECTION: Validation
    private async Task<ValidationResult> ValidateAsync(
        ProliferationProjectExportRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
        {
            return ValidationResult.Fail("The requesting user could not be determined.");
        }

        var trimmedSearch = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search!.Trim();

        var years = (request.Years ?? Array.Empty<int>())
            .Where(year => year > 0)
            .Distinct()
            .OrderByDescending(year => year)
            .ToArray();

        var fromDate = request.FromDate;
        var toDate = request.ToDate;

        if (years.Length > 0 && (fromDate.HasValue || toDate.HasValue))
        {
            return ValidationResult.Fail("Choose either specific years or a date range, not both.");
        }

        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
        {
            return ValidationResult.Fail("The date range is invalid.");
        }

        if (request.Source.HasValue && !Enum.IsDefined(typeof(ProliferationSource), request.Source.Value))
        {
            return ValidationResult.Fail("The proliferation source is invalid.");
        }

        var filters = new NormalizedFilters(
            Years: years,
            FromDate: fromDate,
            ToDate: toDate,
            ProjectCategoryId: request.ProjectCategoryId,
            ProjectCategoryName: null,
            TechnicalCategoryId: request.TechnicalCategoryId,
            TechnicalCategoryName: null,
            Source: request.Source,
            SourceLabel: request.Source?.ToDisplayName(),
            Search: trimmedSearch,
            RequestedByUserId: request.RequestedByUserId.Trim());

        if (filters.ProjectCategoryId.HasValue)
        {
            var name = await _db.ProjectCategories
                .AsNoTracking()
                .Where(c => c.Id == filters.ProjectCategoryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
            filters = filters with { ProjectCategoryName = name };
        }

        if (filters.TechnicalCategoryId.HasValue)
        {
            var name = await _db.TechnicalCategories
                .AsNoTracking()
                .Where(c => c.Id == filters.TechnicalCategoryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
            filters = filters with { TechnicalCategoryName = name };
        }

        return ValidationResult.Success(filters);
    }

    // SECTION: File name helpers
    private static string BuildFileName(NormalizedFilters filters, DateTimeOffset generatedAtUtc)
    {
        var sourceSegment = SanitizeFileSegment(filters.SourceLabel, "all-sources");
        string rangeSegment;
        if (filters.Years.Count > 0)
        {
            rangeSegment = $"years-{string.Join('-', filters.Years)}";
        }
        else if (filters.FromDate.HasValue || filters.ToDate.HasValue)
        {
            var fromSegment = filters.FromDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "start";
            var toSegment = filters.ToDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "end";
            rangeSegment = $"range-{fromSegment}-to-{toSegment}";
        }
        else
        {
            rangeSegment = "all";
        }

        return $"proliferation-projects-{sourceSegment}-{rangeSegment}-{generatedAtUtc:yyyyMMdd'T'HHmmss'Z'}.xlsx";
    }

    private static string SanitizeFileSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var cleaned = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        return string.IsNullOrEmpty(cleaned) ? fallback : cleaned;
    }

    // SECTION: Supporting records
    private sealed record ValidationResult(bool Succeeded, NormalizedFilters? Filters, string? Error)
    {
        public static ValidationResult Fail(string error) => new(false, null, error);

        public static ValidationResult Success(NormalizedFilters filters) => new(true, filters, null);
    }

    private sealed record NormalizedFilters(
        IReadOnlyList<int> Years,
        DateOnly? FromDate,
        DateOnly? ToDate,
        int? ProjectCategoryId,
        string? ProjectCategoryName,
        int? TechnicalCategoryId,
        string? TechnicalCategoryName,
        ProliferationSource? Source,
        string? SourceLabel,
        string? Search,
        string RequestedByUserId)
    {
        public ProliferationProjectAggregationRequest ToAggregationRequest()
        {
            return new ProliferationProjectAggregationRequest(
                Years.Count == 0 ? null : Years,
                FromDate,
                ToDate,
                ProjectCategoryId,
                TechnicalCategoryId,
                Source,
                Search);
        }
    }
}

// SECTION: Export payloads
public sealed record ProliferationProjectExportRequest(
    IReadOnlyCollection<int>? Years,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int? ProjectCategoryId,
    int? TechnicalCategoryId,
    ProliferationSource? Source,
    string? Search,
    string RequestedByUserId);

public sealed record ProliferationProjectExportFile(string FileName, byte[] Content, string ContentType)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed record ProliferationProjectExportResult(
    bool Success,
    ProliferationProjectExportFile? File,
    IReadOnlyList<string> Errors)
{
    public static ProliferationProjectExportResult FromFile(ProliferationProjectExportFile file)
        => new(true, file, Array.Empty<string>());

    public static ProliferationProjectExportResult Failure(params string[] errors)
        => new(false, null, errors.Length == 0 ? new[] { "Export failed." } : errors);
}
