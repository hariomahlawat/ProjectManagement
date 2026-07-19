using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

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

    public static FfcPortfolioSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

public interface IFfcPortfolioService
{
    Task<FfcPortfolioSummary> GetSummaryAsync(
        FfcPortfolioFilter filter,
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

// SECTION: Portfolio aggregation service
public sealed class FfcPortfolioService : IFfcPortfolioService
{
    private readonly ApplicationDbContext _db;

    public FfcPortfolioService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<FfcPortfolioSummary> GetSummaryAsync(
        FfcPortfolioFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var records = FfcPortfolioQuery.ApplyFilters(
            _db.FfcRecords
                .AsNoTracking()
                .Where(record => !record.IsDeleted),
            filter);

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
}
