using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Ffc;

// SECTION: Filter and completion contracts
public enum FfcFilterState
{
    Any = 0,
    Completed = 1,
    Partial = 2,
    Pending = 3
}

public enum FfcCompletionState
{
    Pending = 0,
    Partial = 1,
    Complete = 2
}

public enum FfcUnitPosition
{
    Planned = 0,
    DeliveredAwaitingInstallation = 1,
    Installed = 2
}

public sealed record FfcPortfolioFilter(
    string? Query = null,
    short? Year = null,
    long? CountryId = null,
    FfcFilterState IpaStatus = FfcFilterState.Any,
    FfcFilterState GslStatus = FfcFilterState.Any,
    FfcFilterState DeliveryStatus = FfcFilterState.Any,
    FfcFilterState InstallationStatus = FfcFilterState.Any);

public sealed record FfcPortfolioSummary(
    int RecordCount,
    int CountryCount,
    int ProjectCount,
    int LinkedProjectCount,
    int InstalledUnits,
    int DeliveredNotInstalledUnits,
    int PlannedUnits)
{
    public int DeliveredUnits => InstalledUnits + DeliveredNotInstalledUnits;

    public int TotalUnits => InstalledUnits + DeliveredNotInstalledUnits + PlannedUnits;

    public int DeliveryPercent => TotalUnits == 0
        ? 0
        : (int)Math.Round(DeliveredUnits * 100d / TotalUnits, MidpointRounding.AwayFromZero);

    public int InstallationPercent => TotalUnits == 0
        ? 0
        : (int)Math.Round(InstalledUnits * 100d / TotalUnits, MidpointRounding.AwayFromZero);

    public double InstalledShare => ResolveShare(InstalledUnits);

    public double DeliveredNotInstalledShare => ResolveShare(DeliveredNotInstalledUnits);

    public double PlannedShare => ResolveShare(PlannedUnits);

    public static FfcPortfolioSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    private double ResolveShare(int value) => TotalUnits == 0 ? 0d : value * 100d / TotalUnits;
}

public sealed record FfcMilestoneSnapshot(
    bool IsCompleted,
    DateOnly? CompletedOn,
    string? Remarks);

public sealed record FfcPortfolioProjectRow(
    long FfcProjectId,
    long FfcRecordId,
    int? LinkedProjectId,
    string DisplayName,
    string FfcName,
    int Quantity,
    FfcUnitPosition Position,
    ProjectLifecycleStatus? LifecycleStatus,
    string? StageSummary,
    string? CurrentProgress);

public sealed record FfcPortfolioRecordRow(
    long RecordId,
    long CountryId,
    string CountryName,
    string IsoCode,
    short Year,
    FfcMilestoneSnapshot Ipa,
    FfcMilestoneSnapshot Gsl,
    FfcCompletionState DeliveryState,
    FfcCompletionState InstallationState,
    int ProjectCount,
    int AttachmentCount,
    int InstalledUnits,
    int DeliveredNotInstalledUnits,
    int PlannedUnits,
    string? OverallRemarks,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FfcPortfolioProjectRow> Projects)
{
    public int TotalUnits => InstalledUnits + DeliveredNotInstalledUnits + PlannedUnits;

    public double InstalledShare => ResolveShare(InstalledUnits);

    public double DeliveredNotInstalledShare => ResolveShare(DeliveredNotInstalledUnits);

    public double PlannedShare => ResolveShare(PlannedUnits);

    private double ResolveShare(int value) => TotalUnits == 0 ? 0d : value * 100d / TotalUnits;
}

public sealed record FfcPortfolioPageRequest(
    FfcPortfolioFilter Filter,
    int PageNumber = 1,
    int PageSize = 25);

public sealed record FfcPortfolioPageResult(
    FfcPortfolioSummary Summary,
    IReadOnlyList<FfcPortfolioRecordRow> Records,
    int TotalRecordCount,
    int PageNumber,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRecordCount / (double)Math.Max(1, PageSize)));

    public int FirstItemNumber => TotalRecordCount == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;

    public int LastItemNumber => TotalRecordCount == 0
        ? 0
        : Math.Min(TotalRecordCount, PageNumber * PageSize);
}

public interface IFfcPortfolioService
{
    Task<FfcPortfolioSummary> GetSummaryAsync(
        FfcPortfolioFilter filter,
        CancellationToken cancellationToken = default);

    Task<FfcPortfolioPageResult> GetPageAsync(
        FfcPortfolioPageRequest request,
        CancellationToken cancellationToken = default);
}

// SECTION: Shared query semantics
public static class FfcPortfolioQuery
{
    public static IQueryable<FfcRecord> ApplyFilters(
        IQueryable<FfcRecord> queryable,
        FfcPortfolioFilter filter)
    {
        ArgumentNullException.ThrowIfNull(queryable);
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Year.HasValue)
        {
            queryable = queryable.Where(record => record.Year == filter.Year.Value);
        }

        if (filter.CountryId.HasValue)
        {
            queryable = queryable.Where(record => record.CountryId == filter.CountryId.Value);
        }

        queryable = ApplyBooleanMilestoneFilter(queryable, filter.IpaStatus, isIpa: true);
        queryable = ApplyBooleanMilestoneFilter(queryable, filter.GslStatus, isIpa: false);
        queryable = ApplyDeliveryFilter(queryable, filter.DeliveryStatus);
        queryable = ApplyInstallationFilter(queryable, filter.InstallationStatus);

        var normalizedSearch = NormalizeSearch(filter.Query);
        if (normalizedSearch is not null)
        {
            var hasYear = short.TryParse(normalizedSearch, out var parsedYear);

            queryable = queryable.Where(record =>
                record.Country.Name.ToLower().Contains(normalizedSearch) ||
                record.Country.IsoCode.ToLower().Contains(normalizedSearch) ||
                (hasYear && record.Year == parsedYear) ||
                record.Projects.Any(project =>
                    project.Name.ToLower().Contains(normalizedSearch) ||
                    (project.LinkedProject != null &&
                     project.LinkedProject.Name.ToLower().Contains(normalizedSearch))));
        }

        return queryable;
    }

    public static FfcCompletionState ResolveDeliveryState(FfcProjectQuantitySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return ResolveState(summary.DeliveredNotInstalled + summary.Installed, summary.Total);
    }

    public static FfcCompletionState ResolveInstallationState(FfcProjectQuantitySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return ResolveState(summary.Installed, summary.Total);
    }

    public static FfcUnitPosition ResolvePosition(bool isInstalled, bool isDelivered)
    {
        if (isInstalled)
        {
            return FfcUnitPosition.Installed;
        }

        return isDelivered
            ? FfcUnitPosition.DeliveredAwaitingInstallation
            : FfcUnitPosition.Planned;
    }

    private static IQueryable<FfcRecord> ApplyBooleanMilestoneFilter(
        IQueryable<FfcRecord> queryable,
        FfcFilterState state,
        bool isIpa)
    {
        return (state, isIpa) switch
        {
            (FfcFilterState.Completed, true) => queryable.Where(record => record.IpaYes),
            (FfcFilterState.Pending, true) => queryable.Where(record => !record.IpaYes),
            (FfcFilterState.Partial, true) => queryable.Where(_ => false),
            (FfcFilterState.Completed, false) => queryable.Where(record => record.GslYes),
            (FfcFilterState.Pending, false) => queryable.Where(record => !record.GslYes),
            (FfcFilterState.Partial, false) => queryable.Where(_ => false),
            _ => queryable
        };
    }

    private static IQueryable<FfcRecord> ApplyDeliveryFilter(
        IQueryable<FfcRecord> queryable,
        FfcFilterState state)
    {
        return state switch
        {
            FfcFilterState.Completed => queryable.Where(record =>
                record.Projects.Any() &&
                record.Projects.All(project => project.IsDelivered || project.IsInstalled)),

            FfcFilterState.Partial => queryable.Where(record =>
                record.Projects.Any(project => project.IsDelivered || project.IsInstalled) &&
                record.Projects.Any(project => !project.IsDelivered && !project.IsInstalled)),

            FfcFilterState.Pending => queryable.Where(record =>
                !record.Projects.Any(project => project.IsDelivered || project.IsInstalled)),

            _ => queryable
        };
    }

    private static IQueryable<FfcRecord> ApplyInstallationFilter(
        IQueryable<FfcRecord> queryable,
        FfcFilterState state)
    {
        return state switch
        {
            FfcFilterState.Completed => queryable.Where(record =>
                record.Projects.Any() &&
                record.Projects.All(project => project.IsInstalled)),

            FfcFilterState.Partial => queryable.Where(record =>
                record.Projects.Any(project => project.IsInstalled) &&
                record.Projects.Any(project => !project.IsInstalled)),

            FfcFilterState.Pending => queryable.Where(record =>
                !record.Projects.Any(project => project.IsInstalled)),

            _ => queryable
        };
    }

    private static FfcCompletionState ResolveState(int completedUnits, int totalUnits)
    {
        if (totalUnits <= 0 || completedUnits <= 0)
        {
            return FfcCompletionState.Pending;
        }

        return completedUnits >= totalUnits
            ? FfcCompletionState.Complete
            : FfcCompletionState.Partial;
    }

    private static string? NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }
}

// SECTION: Portfolio aggregation and page service
public sealed class FfcPortfolioService : IFfcPortfolioService
{
    private const int MaximumPageSize = 100;

    private readonly ApplicationDbContext _db;
    private readonly IFfcProgressService _progressService;

    public FfcPortfolioService(
        ApplicationDbContext db,
        IFfcProgressService progressService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _progressService = progressService ?? throw new ArgumentNullException(nameof(progressService));
    }

    public async Task<FfcPortfolioSummary> GetSummaryAsync(
        FfcPortfolioFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var records = BuildFilteredRecords(filter);
        var recordCount = await records.CountAsync(cancellationToken);
        if (recordCount == 0)
        {
            return FfcPortfolioSummary.Empty;
        }

        var countryCount = await records
            .Select(record => record.CountryId)
            .Distinct()
            .CountAsync(cancellationToken);

        var projectAggregate = await records
            .SelectMany(record => record.Projects)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                ProjectCount = group.Count(),
                LinkedProjectCount = group.Count(project => project.LinkedProjectId.HasValue),
                InstalledUnits = group.Sum(project =>
                    project.IsInstalled ? project.Quantity : 0),
                DeliveredNotInstalledUnits = group.Sum(project =>
                    project.IsDelivered && !project.IsInstalled ? project.Quantity : 0),
                PlannedUnits = group.Sum(project =>
                    !project.IsDelivered && !project.IsInstalled ? project.Quantity : 0)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new FfcPortfolioSummary(
            RecordCount: recordCount,
            CountryCount: countryCount,
            ProjectCount: projectAggregate?.ProjectCount ?? 0,
            LinkedProjectCount: projectAggregate?.LinkedProjectCount ?? 0,
            InstalledUnits: projectAggregate?.InstalledUnits ?? 0,
            DeliveredNotInstalledUnits: projectAggregate?.DeliveredNotInstalledUnits ?? 0,
            PlannedUnits: projectAggregate?.PlannedUnits ?? 0);
    }

    public async Task<FfcPortfolioPageResult> GetPageAsync(
        FfcPortfolioPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Filter);

        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        var requestedPage = Math.Max(1, request.PageNumber);
        var summary = await GetSummaryAsync(request.Filter, cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(summary.RecordCount / (double)pageSize));
        var pageNumber = Math.Min(requestedPage, totalPages);

        if (summary.RecordCount == 0)
        {
            return new FfcPortfolioPageResult(
                Summary: summary,
                Records: Array.Empty<FfcPortfolioRecordRow>(),
                TotalRecordCount: 0,
                PageNumber: 1,
                PageSize: pageSize);
        }

        var recordProjections = await BuildFilteredRecords(request.Filter)
            .OrderByDescending(record => record.Year)
            .ThenBy(record => record.Country.Name)
            .ThenBy(record => record.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(record => new RecordProjection(
                record.Id,
                record.CountryId,
                record.Country.Name,
                record.Country.IsoCode,
                record.Year,
                record.IpaYes,
                record.IpaDate,
                record.IpaRemarks,
                record.GslYes,
                record.GslDate,
                record.GslRemarks,
                record.Projects.Count,
                record.Attachments.Count,
                record.Projects.Sum(project => project.IsInstalled ? project.Quantity : 0),
                record.Projects.Sum(project => project.IsDelivered && !project.IsInstalled ? project.Quantity : 0),
                record.Projects.Sum(project => !project.IsDelivered && !project.IsInstalled ? project.Quantity : 0),
                record.OverallRemarks,
                record.UpdatedAt))
            .ToListAsync(cancellationToken);

        var recordIds = recordProjections.Select(record => record.RecordId).ToArray();
        var projectProjections = await _db.FfcProjects
            .AsNoTracking()
            .Where(project => recordIds.Contains(project.FfcRecordId))
            .OrderBy(project => project.FfcRecordId)
            .ThenBy(project => project.Id)
            .Select(project => new ProjectProjection(
                project.Id,
                project.FfcRecordId,
                project.Name,
                project.Remarks,
                project.LinkedProjectId,
                project.Quantity,
                project.IsDelivered,
                project.IsInstalled,
                project.LinkedProject == null ? null : project.LinkedProject.Name,
                project.LinkedProject == null
                    ? null
                    : (ProjectLifecycleStatus?)project.LinkedProject.LifecycleStatus))
            .ToListAsync(cancellationToken);

        var linkedProjectIds = projectProjections
            .Where(project => project.LinkedProjectId.HasValue)
            .Select(project => project.LinkedProjectId!.Value)
            .Distinct()
            .ToArray();

        var stageSummaryByProjectId = new Dictionary<int, string?>();
        if (linkedProjectIds.Length > 0)
        {
            var stageRows = await _db.ProjectStages
                .AsNoTracking()
                .Where(stage => linkedProjectIds.Contains(stage.ProjectId))
                .Select(stage => new FfcProjectStageSnapshot(
                    stage.ProjectId,
                    stage.StageCode,
                    stage.SortOrder,
                    stage.Status,
                    stage.CompletedOn))
                .ToListAsync(cancellationToken);

            stageSummaryByProjectId = stageRows
                .GroupBy(stage => stage.ProjectId)
                .ToDictionary(
                    group => group.Key,
                    group => FfcProjectStageSummaryFormatter.Format(group));
        }

        var progressByFfcProjectId = await _progressService.GetCurrentProgressAsync(
            projectProjections
                .Select(project => new FfcProgressTarget(
                    project.FfcProjectId,
                    project.LinkedProjectId,
                    project.FfcRemarks))
                .ToArray(),
            cancellationToken);

        var projectsByRecord = projectProjections
            .Select(project =>
            {
                progressByFfcProjectId.TryGetValue(project.FfcProjectId, out var progress);
                var displayName = string.IsNullOrWhiteSpace(project.LinkedProjectName)
                    ? project.FfcName
                    : project.LinkedProjectName;

                string? stageSummary = null;
                if (project.LinkedProjectId.HasValue)
                {
                    stageSummaryByProjectId.TryGetValue(project.LinkedProjectId.Value, out stageSummary);
                }

                return new FfcPortfolioProjectRow(
                    FfcProjectId: project.FfcProjectId,
                    FfcRecordId: project.FfcRecordId,
                    LinkedProjectId: project.LinkedProjectId,
                    DisplayName: displayName,
                    FfcName: project.FfcName,
                    Quantity: project.Quantity,
                    Position: FfcPortfolioQuery.ResolvePosition(project.IsInstalled, project.IsDelivered),
                    LifecycleStatus: project.LinkedProjectLifecycleStatus,
                    StageSummary: stageSummary,
                    CurrentProgress: progress?.Text);
            })
            .GroupBy(project => project.FfcRecordId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FfcPortfolioProjectRow>)group.ToList());

        var rows = recordProjections
            .Select(record =>
            {
                projectsByRecord.TryGetValue(record.RecordId, out var projects);
                projects ??= Array.Empty<FfcPortfolioProjectRow>();

                var quantitySummary = new FfcProjectQuantitySummary(
                    record.InstalledUnits,
                    record.DeliveredNotInstalledUnits,
                    record.PlannedUnits);

                return new FfcPortfolioRecordRow(
                    RecordId: record.RecordId,
                    CountryId: record.CountryId,
                    CountryName: record.CountryName,
                    IsoCode: record.IsoCode,
                    Year: record.Year,
                    Ipa: new FfcMilestoneSnapshot(record.IpaYes, record.IpaDate, record.IpaRemarks),
                    Gsl: new FfcMilestoneSnapshot(record.GslYes, record.GslDate, record.GslRemarks),
                    DeliveryState: FfcPortfolioQuery.ResolveDeliveryState(quantitySummary),
                    InstallationState: FfcPortfolioQuery.ResolveInstallationState(quantitySummary),
                    ProjectCount: record.ProjectCount,
                    AttachmentCount: record.AttachmentCount,
                    InstalledUnits: record.InstalledUnits,
                    DeliveredNotInstalledUnits: record.DeliveredNotInstalledUnits,
                    PlannedUnits: record.PlannedUnits,
                    OverallRemarks: record.OverallRemarks,
                    UpdatedAt: record.UpdatedAt,
                    Projects: projects);
            })
            .ToList();

        return new FfcPortfolioPageResult(
            Summary: summary,
            Records: rows,
            TotalRecordCount: summary.RecordCount,
            PageNumber: pageNumber,
            PageSize: pageSize);
    }

    private IQueryable<FfcRecord> BuildFilteredRecords(FfcPortfolioFilter filter)
        => FfcPortfolioQuery.ApplyFilters(
            _db.FfcRecords
                .AsNoTracking()
                .Where(record => !record.IsDeleted),
            filter);

    private sealed record RecordProjection(
        long RecordId,
        long CountryId,
        string CountryName,
        string IsoCode,
        short Year,
        bool IpaYes,
        DateOnly? IpaDate,
        string? IpaRemarks,
        bool GslYes,
        DateOnly? GslDate,
        string? GslRemarks,
        int ProjectCount,
        int AttachmentCount,
        int InstalledUnits,
        int DeliveredNotInstalledUnits,
        int PlannedUnits,
        string? OverallRemarks,
        DateTimeOffset UpdatedAt);

    private sealed record ProjectProjection(
        long FfcProjectId,
        long FfcRecordId,
        string FfcName,
        string? FfcRemarks,
        int? LinkedProjectId,
        int Quantity,
        bool IsDelivered,
        bool IsInstalled,
        string? LinkedProjectName,
        ProjectLifecycleStatus? LinkedProjectLifecycleStatus);
}
