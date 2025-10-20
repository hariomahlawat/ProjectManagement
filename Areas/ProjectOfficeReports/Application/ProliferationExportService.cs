using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IProliferationExportService
{
    Task<ProliferationExportResult> ExportAsync(ProliferationExportRequest request, CancellationToken cancellationToken);
}

public sealed class ProliferationExportService : IProliferationExportService
{
    private readonly ApplicationDbContext _db;
    private readonly ProliferationTrackerReadService _trackerReadService;
    private readonly IProliferationExcelWorkbookBuilder _workbookBuilder;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<ProliferationExportService> _logger;

    public ProliferationExportService(
        ApplicationDbContext db,
        ProliferationTrackerReadService trackerReadService,
        IProliferationExcelWorkbookBuilder workbookBuilder,
        IClock clock,
        IAuditService audit,
        ILogger<ProliferationExportService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _trackerReadService = trackerReadService ?? throw new ArgumentNullException(nameof(trackerReadService));
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

        var validation = await ValidateAsync(request, cancellationToken);
        if (!validation.Success)
        {
            return ProliferationExportResult.Failure(validation.Error ?? "Export failed.");
        }

        var filters = validation.Filters!;
        var queryResult = await LoadRowsAsync(filters, cancellationToken);
        var generatedAt = _clock.UtcNow;

        var metadata = new ProliferationExportFilterMetadata(
            queryResult.FilterYears,
            filters.FromDate,
            filters.ToDate,
            filters.SourceLabel ?? string.Empty,
            filters.ProjectCategoryName ?? (filters.ProjectCategoryId.HasValue ? $"Category #{filters.ProjectCategoryId.Value}" : string.Empty),
            filters.TechnicalCategoryName ?? (filters.TechnicalCategoryId.HasValue ? $"Technical #{filters.TechnicalCategoryId.Value}" : string.Empty),
            filters.Search);

        var workbookContent = _workbookBuilder.Build(new ProliferationExcelWorkbookContext(
            queryResult.Rows,
            generatedAt,
            filters.RequestedByUserId,
            metadata));

        var fileName = BuildFileName(filters, generatedAt);
        var file = new ProliferationExportFile(fileName, workbookContent, ProliferationExportFile.ExcelContentType);

        await Audit.Events.ProliferationExportGenerated(
                filters.RequestedByUserId,
                filters.Years,
                filters.FromDate,
                filters.ToDate,
                filters.Source,
                filters.SourceLabel,
                filters.ProjectCategoryId,
                filters.ProjectCategoryName,
                filters.TechnicalCategoryId,
                filters.TechnicalCategoryName,
                filters.Search,
                queryResult.Rows.Count,
                fileName)
            .WriteAsync(_audit);

        _logger.LogInformation(
            "Generated proliferation export with {RowCount} rows for user {UserId}",
            queryResult.Rows.Count,
            filters.RequestedByUserId);

        return ProliferationExportResult.FromFile(file);
    }

    private async Task<ValidationResult> ValidateAsync(ProliferationExportRequest request, CancellationToken cancellationToken)
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
            Source: request.Source,
            SourceLabel: request.Source?.ToDisplayName(),
            ProjectCategoryId: request.ProjectCategoryId,
            ProjectCategoryName: null,
            TechnicalCategoryId: request.TechnicalCategoryId,
            TechnicalCategoryName: null,
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

    private async Task<ExportQueryResult> LoadRowsAsync(NormalizedFilters filters, CancellationToken cancellationToken)
    {
        var projectsQuery = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived);

        if (filters.ProjectCategoryId.HasValue)
        {
            projectsQuery = projectsQuery.Where(p => p.CategoryId == filters.ProjectCategoryId.Value);
        }

        if (filters.TechnicalCategoryId.HasValue)
        {
            projectsQuery = projectsQuery.Where(p => p.TechnicalCategoryId == filters.TechnicalCategoryId.Value);
        }

        var yearlyBase = from y in _db.Set<ProliferationYearly>().AsNoTracking()
                         join p in projectsQuery on y.ProjectId equals p.Id
                         join pref in _db.Set<ProliferationYearPreference>().AsNoTracking()
                             on new { y.ProjectId, y.Source, y.Year } equals new { pref.ProjectId, pref.Source, pref.Year }
                             into prefJoin
                         from pref in prefJoin.DefaultIfEmpty()
                         select new { Yearly = y, Project = p, Preference = pref };

        var granularBase = from g in _db.Set<ProliferationGranular>().AsNoTracking()
                           join p in projectsQuery on g.ProjectId equals p.Id
                           select new { Granular = g, Project = p };

        if (filters.Years.Count > 0)
        {
            var yearsSet = filters.Years.ToHashSet();
            yearlyBase = yearlyBase.Where(x => yearsSet.Contains(x.Yearly.Year));
            granularBase = granularBase.Where(x => yearsSet.Contains(x.Granular.ProliferationDate.Year));
        }
        else
        {
            if (filters.FromDate.HasValue)
            {
                var start = filters.FromDate.Value;
                yearlyBase = yearlyBase.Where(x => x.Yearly.Year >= start.Year);
                granularBase = granularBase.Where(x => x.Granular.ProliferationDate >= start);
            }

            if (filters.ToDate.HasValue)
            {
                var end = filters.ToDate.Value;
                yearlyBase = yearlyBase.Where(x => x.Yearly.Year <= end.Year);
                granularBase = granularBase.Where(x => x.Granular.ProliferationDate <= end);
            }
        }

        if (filters.Source.HasValue)
        {
            yearlyBase = yearlyBase.Where(x => x.Yearly.Source == filters.Source.Value);
            granularBase = granularBase.Where(x => x.Granular.Source == filters.Source.Value);
        }

        if (!string.IsNullOrEmpty(filters.Search))
        {
            var like = $"%{filters.Search}%";
            yearlyBase = yearlyBase.Where(x =>
                EF.Functions.ILike(x.Project.Name, like) ||
                (x.Project.CaseFileNumber != null && EF.Functions.ILike(x.Project.CaseFileNumber, like)));

            granularBase = granularBase.Where(x =>
                EF.Functions.ILike(x.Project.Name, like) ||
                (x.Project.CaseFileNumber != null && EF.Functions.ILike(x.Project.CaseFileNumber, like)) ||
                EF.Functions.ILike(x.Granular.SimulatorName, like) ||
                EF.Functions.ILike(x.Granular.UnitName, like));
        }

        var yearlyRowsQuery = yearlyBase.Select(x => new RowProjection(
            x.Project.Id,
            x.Yearly.Year,
            x.Project.Name,
            x.Project.CaseFileNumber,
            x.Yearly.Source,
            "Yearly",
            null,
            null,
            null,
            x.Yearly.TotalQuantity,
            x.Yearly.ApprovalStatus,
            x.Preference != null ? x.Preference.Mode : (YearPreferenceMode?)null));

        var granularRowsQuery = granularBase.Select(x => new RowProjection(
            x.Project.Id,
            x.Granular.ProliferationDate.Year,
            x.Project.Name,
            x.Project.CaseFileNumber,
            x.Granular.Source,
            "Granular",
            x.Granular.UnitName,
            x.Granular.SimulatorName,
            x.Granular.ProliferationDate,
            x.Granular.Quantity,
            x.Granular.ApprovalStatus,
            null));

        var combinedQuery = yearlyRowsQuery.Concat(granularRowsQuery)
            .OrderByDescending(r => r.Year)
            .ThenBy(r => r.Project)
            .ThenBy(r => r.Source)
            .ThenBy(r => r.DataType)
            .ThenBy(r => r.Date);

        var projections = await combinedQuery.ToListAsync(cancellationToken);

        var combinations = projections
            .Select(r => new Combination(r.ProjectId, r.Source, r.Year))
            .Distinct()
            .ToList();

        var totalsLookup = new Dictionary<Combination, int>(combinations.Count);
        foreach (var combo in combinations)
        {
            var total = await _trackerReadService.GetEffectiveTotalAsync(combo.ProjectId, combo.Source, combo.Year, cancellationToken);
            totalsLookup[combo] = total;
        }

        var rows = new List<ProliferationExcelRow>(projections.Count);
        foreach (var projection in projections)
        {
            totalsLookup.TryGetValue(new Combination(projection.ProjectId, projection.Source, projection.Year), out var effective);
            rows.Add(new ProliferationExcelRow(
                projection.Year,
                projection.Project,
                projection.ProjectCode,
                projection.Source,
                projection.DataType,
                projection.UnitName,
                projection.SimulatorName,
                projection.Date,
                projection.Quantity,
                effective,
                projection.ApprovalStatus.ToString(),
                projection.PreferenceMode));
        }

        return new ExportQueryResult(rows, filters.Years);
    }

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

        return $"proliferation-overview-{sourceSegment}-{rangeSegment}-{generatedAtUtc:yyyyMMdd'T'HHmmss'Z'}.xlsx";
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

    private sealed record ValidationResult(bool Success, NormalizedFilters? Filters, string? Error)
    {
        public static ValidationResult Fail(string error) => new(false, null, error);

        public static ValidationResult Success(NormalizedFilters filters) => new(true, filters, null);
    }

    private sealed record NormalizedFilters(
        IReadOnlyList<int> Years,
        DateOnly? FromDate,
        DateOnly? ToDate,
        ProliferationSource? Source,
        string? SourceLabel,
        int? ProjectCategoryId,
        string? ProjectCategoryName,
        int? TechnicalCategoryId,
        string? TechnicalCategoryName,
        string? Search,
        string RequestedByUserId);

    private sealed record ExportQueryResult(IReadOnlyList<ProliferationExcelRow> Rows, IReadOnlyList<int> FilterYears);

    private readonly record struct Combination(int ProjectId, ProliferationSource Source, int Year);

    private sealed record RowProjection(
        int ProjectId,
        int Year,
        string Project,
        string? ProjectCode,
        ProliferationSource Source,
        string DataType,
        string? UnitName,
        string? SimulatorName,
        DateOnly? Date,
        int Quantity,
        ApprovalStatus ApprovalStatus,
        YearPreferenceMode? PreferenceMode);
}

public sealed record ProliferationExportRequest(
    IReadOnlyCollection<int>? Years,
    DateOnly? FromDate,
    DateOnly? ToDate,
    ProliferationSource? Source,
    int? ProjectCategoryId,
    int? TechnicalCategoryId,
    string? Search,
    string RequestedByUserId);

public sealed record ProliferationExportFile(string FileName, byte[] Content, string ContentType)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed record ProliferationExportResult(bool Success, ProliferationExportFile? File, IReadOnlyList<string> Errors)
{
    public static ProliferationExportResult FromFile(ProliferationExportFile file)
        => new(true, file, Array.Empty<string>());

    public static ProliferationExportResult Failure(params string[] errors)
        => new(false, null, errors.Length == 0 ? new[] { "Export failed." } : errors);
}
