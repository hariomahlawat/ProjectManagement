using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Ffc;

public sealed class FfcFootprintService : IFfcFootprintService
{
    private readonly ApplicationDbContext _db;
    private readonly IFfcProgressService _progressService;

    public FfcFootprintService(
        ApplicationDbContext db,
        IFfcProgressService progressService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _progressService = progressService ?? throw new ArgumentNullException(nameof(progressService));
    }

    public async Task<FfcFootprintResult> GetAsync(
        FfcFootprintRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var activeRecords = _db.FfcRecords
            .AsNoTracking()
            .Where(record => !record.IsDeleted && record.Country.IsActive);

        var availableYears = await activeRecords
            .TagWith("FFC Footprint: available years")
            .Select(record => record.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        // Query the country table directly. This naturally returns one row per country and
        // avoids Distinct over a custom DTO constructor, which PostgreSQL cannot translate.
        var countryOptionRows = await _db.FfcCountries
            .AsNoTracking()
            .TagWith("FFC Footprint: country options")
            .Where(country =>
                country.IsActive &&
                _db.FfcRecords.Any(record =>
                    !record.IsDeleted &&
                    record.CountryId == country.Id))
            .OrderBy(country => country.Name)
            .ThenBy(country => country.IsoCode)
            .Select(country => new
            {
                CountryId = country.Id,
                CountryName = country.Name,
                IsoCode = country.IsoCode
            })
            .ToListAsync(cancellationToken);

        var countryOptions = countryOptionRows
            .Select(country => new FfcFootprintCountryOption(
                country.CountryId,
                country.CountryName,
                country.IsoCode))
            .ToList();

        var filteredRecords = ApplyFilters(activeRecords, request);

        // Keep the SQL projection scalar-only. Materialise first and construct application
        // DTOs in memory so query translation does not depend on provider support for custom
        // record constructors.
        var recordData = await filteredRecords
            .TagWith("FFC Footprint: filtered country-year records")
            .OrderBy(record => record.Country.Name)
            .ThenByDescending(record => record.Year)
            .ThenBy(record => record.Id)
            .Select(record => new
            {
                RecordId = record.Id,
                record.CountryId,
                CountryName = record.Country.Name,
                IsoCode = record.Country.IsoCode,
                record.Year,
                ProjectCount = record.Projects.Count(),
                InstalledUnits = record.Projects
                    .Where(project => project.IsInstalled)
                    .Sum(project => (int?)project.Quantity) ?? 0,
                DeliveredNotInstalledUnits = record.Projects
                    .Where(project => project.IsDelivered && !project.IsInstalled)
                    .Sum(project => (int?)project.Quantity) ?? 0,
                PlannedUnits = record.Projects
                    .Where(project => !project.IsDelivered && !project.IsInstalled)
                    .Sum(project => (int?)project.Quantity) ?? 0,
                OverallPosition = record.OverallRemarks,
                record.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var recordRows = recordData
            .Select(record => new RecordProjection(
                record.RecordId,
                record.CountryId,
                record.CountryName,
                record.IsoCode,
                record.Year,
                record.ProjectCount,
                record.InstalledUnits,
                record.DeliveredNotInstalledUnits,
                record.PlannedUnits,
                record.OverallPosition,
                record.UpdatedAt))
            .ToList();

        if (recordRows.Count == 0)
        {
            return FfcFootprintResult.Empty(availableYears, countryOptions);
        }

        var recordIds = recordRows
            .Select(record => record.RecordId)
            .ToArray();

        var projectData = await _db.FfcProjects
            .AsNoTracking()
            .TagWith("FFC Footprint: projects for filtered records")
            .Where(project => recordIds.Contains(project.FfcRecordId))
            .OrderBy(project => project.FfcRecordId)
            .ThenBy(project => project.Id)
            .Select(project => new
            {
                FfcProjectId = project.Id,
                project.FfcRecordId,
                FfcName = project.Name,
                FfcRemarks = project.Remarks,
                project.LinkedProjectId,
                project.Quantity,
                project.IsDelivered,
                project.IsInstalled,
                LinkedProjectName = project.LinkedProject == null
                    ? null
                    : project.LinkedProject.Name
            })
            .ToListAsync(cancellationToken);

        var projectRows = projectData
            .Select(project => new ProjectProjection(
                project.FfcProjectId,
                project.FfcRecordId,
                project.FfcName,
                project.FfcRemarks,
                project.LinkedProjectId,
                project.Quantity,
                project.IsDelivered,
                project.IsInstalled,
                project.LinkedProjectName))
            .ToList();

        var linkedProjectIds = projectRows
            .Where(project => project.LinkedProjectId.HasValue)
            .Select(project => project.LinkedProjectId!.Value)
            .Distinct()
            .ToArray();

        var stageSummaryByProjectId = new Dictionary<int, string?>();
        if (linkedProjectIds.Length > 0)
        {
            var stageData = await _db.ProjectStages
                .AsNoTracking()
                .TagWith("FFC Footprint: linked project stages")
                .Where(stage => linkedProjectIds.Contains(stage.ProjectId))
                .Select(stage => new
                {
                    stage.ProjectId,
                    stage.StageCode,
                    stage.SortOrder,
                    stage.Status,
                    stage.CompletedOn
                })
                .ToListAsync(cancellationToken);

            var stageRows = stageData
                .Select(stage => new FfcProjectStageSnapshot(
                    stage.ProjectId,
                    stage.StageCode,
                    stage.SortOrder,
                    stage.Status,
                    stage.CompletedOn))
                .ToList();

            stageSummaryByProjectId = stageRows
                .GroupBy(stage => stage.ProjectId)
                .ToDictionary(
                    group => group.Key,
                    group => FfcProjectStageSummaryFormatter.Format(group));
        }

        var progressByFfcProjectId = await _progressService.GetCurrentProgressAsync(
            projectRows
                .Select(project => new FfcProgressTarget(
                    project.FfcProjectId,
                    project.LinkedProjectId,
                    project.FfcRemarks))
                .ToArray(),
            cancellationToken);

        var projectsByRecordId = projectRows
            .Select(project =>
            {
                progressByFfcProjectId.TryGetValue(project.FfcProjectId, out var progress);
                string? stageSummary = null;
                if (project.LinkedProjectId.HasValue)
                {
                    stageSummaryByProjectId.TryGetValue(project.LinkedProjectId.Value, out stageSummary);
                }

                return new
                {
                    project.FfcRecordId,
                    Project = new FfcFootprintProject(
                        FfcProjectId: project.FfcProjectId,
                        LinkedProjectId: project.LinkedProjectId,
                        DisplayName: string.IsNullOrWhiteSpace(project.LinkedProjectName)
                            ? project.FfcName
                            : project.LinkedProjectName,
                        FfcName: project.FfcName,
                        Quantity: project.Quantity,
                        Position: FfcPortfolioQuery.ResolvePosition(project.IsInstalled, project.IsDelivered),
                        StageSummary: stageSummary,
                        CurrentProgress: progress?.Text)
                };
            })
            .GroupBy(item => item.FfcRecordId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FfcFootprintProject>)group
                    .Select(item => item.Project)
                    .ToList());

        var yearsByCountryId = recordRows
            .Select(record =>
            {
                projectsByRecordId.TryGetValue(record.RecordId, out var projects);
                projects ??= Array.Empty<FfcFootprintProject>();

                return new
                {
                    Record = record,
                    Year = new FfcFootprintYear(
                        RecordId: record.RecordId,
                        Year: record.Year,
                        ProjectCount: record.ProjectCount,
                        InstalledUnits: record.InstalledUnits,
                        DeliveredNotInstalledUnits: record.DeliveredNotInstalledUnits,
                        PlannedUnits: record.PlannedUnits,
                        OverallPosition: record.OverallPosition,
                        UpdatedAt: record.UpdatedAt,
                        Projects: projects)
                };
            })
            .GroupBy(item => item.Record.CountryId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FfcFootprintYear>)group
                    .OrderByDescending(item => item.Year.Year)
                    .ThenByDescending(item => item.Year.UpdatedAt)
                    .Select(item => item.Year)
                    .ToList());

        var countryRows = recordRows
            .GroupBy(record => new
            {
                record.CountryId,
                record.CountryName,
                record.IsoCode
            })
            .Select(group =>
            {
                yearsByCountryId.TryGetValue(group.Key.CountryId, out var years);
                years ??= Array.Empty<FfcFootprintYear>();

                return new FfcFootprintCountry(
                    CountryId: group.Key.CountryId,
                    CountryName: group.Key.CountryName,
                    IsoCode: group.Key.IsoCode.ToUpperInvariant(),
                    RecordCount: group.Count(),
                    ProjectCount: group.Sum(record => record.ProjectCount),
                    InstalledUnits: group.Sum(record => record.InstalledUnits),
                    DeliveredNotInstalledUnits: group.Sum(record => record.DeliveredNotInstalledUnits),
                    PlannedUnits: group.Sum(record => record.PlannedUnits),
                    LastUpdated: group.Max(record => record.UpdatedAt),
                    Years: years);
            });

        var countries = SortCountries(countryRows, request.Sort).ToList();

        var summary = new FfcFootprintSummary(
            CountryCount: countries.Count,
            RecordCount: recordRows.Count,
            ProjectCount: projectRows.Count,
            InstalledUnits: recordRows.Sum(record => record.InstalledUnits),
            DeliveredNotInstalledUnits: recordRows.Sum(record => record.DeliveredNotInstalledUnits),
            PlannedUnits: recordRows.Sum(record => record.PlannedUnits));

        return new FfcFootprintResult(
            Summary: summary,
            Countries: countries,
            AvailableYears: availableYears,
            CountryOptions: countryOptions);
    }

    private static IOrderedEnumerable<FfcFootprintCountry> SortCountries(
        IEnumerable<FfcFootprintCountry> countries,
        FfcFootprintSort sort)
    {
        return sort switch
        {
            FfcFootprintSort.CountryName => countries
                .OrderBy(country => country.CountryName, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(country => country.TotalUnits),

            FfcFootprintSort.InstalledUnits => countries
                .OrderByDescending(country => country.InstalledUnits)
                .ThenByDescending(country => country.TotalUnits)
                .ThenBy(country => country.CountryName, StringComparer.OrdinalIgnoreCase),

            FfcFootprintSort.PlannedUnits => countries
                .OrderByDescending(country => country.PlannedUnits)
                .ThenByDescending(country => country.TotalUnits)
                .ThenBy(country => country.CountryName, StringComparer.OrdinalIgnoreCase),

            FfcFootprintSort.MostRecentActivity => countries
                .OrderByDescending(country => country.LastUpdated)
                .ThenByDescending(country => country.TotalUnits)
                .ThenBy(country => country.CountryName, StringComparer.OrdinalIgnoreCase),

            _ => countries
                .OrderByDescending(country => country.TotalUnits)
                .ThenBy(country => country.CountryName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IQueryable<FfcRecord> ApplyFilters(
        IQueryable<FfcRecord> records,
        FfcFootprintRequest request)
    {
        if (request.Year.HasValue)
        {
            records = records.Where(record => record.Year == request.Year.Value);
        }

        if (request.CountryId.HasValue)
        {
            records = records.Where(record => record.CountryId == request.CountryId.Value);
        }

        var normalizedSearch = NormalizeSearch(request.Search);
        if (normalizedSearch is not null)
        {
            records = records.Where(record =>
                record.Country.Name.ToLower().Contains(normalizedSearch) ||
                record.Country.IsoCode.ToLower().Contains(normalizedSearch) ||
                record.Projects.Any(project =>
                    project.Name.ToLower().Contains(normalizedSearch) ||
                    (project.LinkedProject != null &&
                     project.LinkedProject.Name.ToLower().Contains(normalizedSearch))));
        }

        return records;
    }

    private static string? NormalizeSearch(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();

    private sealed record RecordProjection(
        long RecordId,
        long CountryId,
        string CountryName,
        string IsoCode,
        short Year,
        int ProjectCount,
        int InstalledUnits,
        int DeliveredNotInstalledUnits,
        int PlannedUnits,
        string? OverallPosition,
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
        string? LinkedProjectName);
}
