using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Ffc;

// SECTION: Contracts
public interface IFfcQueryService
{
    Task<IReadOnlyList<FfcDetailedGroupVm>> GetDetailedGroupsAsync(
        DateOnly from,
        DateOnly to,
        bool incompleteOnly,
        long? countryId = null,
        short? year = null,
        bool applyYearFilter = true,
        CancellationToken cancellationToken = default);
}

public sealed record FfcDetailedGroupVm(
    long FfcRecordId,
    string CountryName,
    string CountryCode,
    int Year,
    string? OverallRemarks,
    string? OverallRemarksDisplay,
    IReadOnlyList<FfcDetailedRowVm> Rows,
    bool HasIncomplete);

public sealed record FfcDetailedRowVm(
    long FfcProjectId,
    int Serial,
    string ProjectName,
    int? LinkedProjectId,
    decimal? CostInCr,
    int Quantity,
    string Status,
    string? ProgressText,
    string? ProgressTextRaw,
    int? ExternalRemarkId,
    FfcProgressSource ProgressSource,
    bool IsProgressEditable);

// SECTION: Query service
public sealed class FfcQueryService : IFfcQueryService
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectCostResolver _costResolver;
    private readonly IFfcProgressService _progressService;

    public FfcQueryService(
        ApplicationDbContext db,
        IProjectCostResolver costResolver,
        IFfcProgressService progressService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _costResolver = costResolver ?? throw new ArgumentNullException(nameof(costResolver));
        _progressService = progressService ?? throw new ArgumentNullException(nameof(progressService));
    }

    public async Task<IReadOnlyList<FfcDetailedGroupVm>> GetDetailedGroupsAsync(
        DateOnly from,
        DateOnly to,
        bool incompleteOnly,
        long? countryId = null,
        short? year = null,
        bool applyYearFilter = true,
        CancellationToken cancellationToken = default)
    {
        var projects = await BuildBaseQuery(from, to, countryId, year, applyYearFilter)
            .Select(project => new FfcProjectProjection(
                project.Id,
                project.Name,
                project.Remarks,
                project.Quantity,
                project.IsDelivered,
                project.IsInstalled,
                project.DeliveredOn,
                project.InstalledOn,
                project.LinkedProjectId,
                project.LinkedProject != null ? project.LinkedProject.Name : null,
                project.Record.Id,
                project.Record.Year,
                project.Record.OverallRemarks,
                project.Record.Country.IsoCode,
                project.Record.Country.Name))
            .ToListAsync(cancellationToken);

        if (projects.Count == 0)
        {
            return Array.Empty<FfcDetailedGroupVm>();
        }

        var linkedProjectIds = projects
            .Where(row => row.LinkedProjectId.HasValue)
            .Select(row => row.LinkedProjectId!.Value)
            .Distinct()
            .ToArray();

        var projectNameMap = new Dictionary<int, string>(linkedProjectIds.Length);
        var projectCostMap = new Dictionary<int, decimal?>(linkedProjectIds.Length);
        if (linkedProjectIds.Length > 0)
        {
            var projectSnapshots = await _db.Projects
                .AsNoTracking()
                .Where(project => linkedProjectIds.Contains(project.Id))
                .Select(project => new
                {
                    project.Id,
                    project.Name
                })
                .ToListAsync(cancellationToken);

            projectNameMap = projectSnapshots
                .ToDictionary(x => x.Id, x => x.Name ?? string.Empty);

            // SECTION: Resolve project costs (CostLakhs -> PNC -> L1 -> AON -> IPA)
            var costResolutions = await _costResolver.ResolveCostInCrAsync(
                linkedProjectIds,
                cancellationToken);

            projectCostMap = costResolutions
                .ToDictionary(entry => entry.Key, entry => entry.Value.CostInCr);
        }

        var progressByFfcProject = await _progressService.GetCurrentProgressAsync(
            projects
                .Where(project => project.LinkedProjectId.HasValue)
                .Select(project => new FfcProgressTarget(
                    FfcProjectId: project.Id,
                    LinkedProjectId: project.LinkedProjectId,
                    FfcProjectRemarks: project.Remarks))
                .ToArray(),
            cancellationToken);

        var groups = projects
            .GroupBy(project => new
            {
                project.RecordId,
                project.Year,
                project.CountryName,
                project.CountryIso3,
                project.OverallRemarks
            })
            .OrderByDescending(group => group.Key.Year)
            .ThenBy(group => group.Key.CountryName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<FfcDetailedGroupVm>(groups.Count);

        foreach (var group in groups)
        {
            var hasIncomplete = group.Any(project => !project.IsInstalled);
            var orderedProjects = group
                .OrderBy(project => project.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var projectRows = new List<FfcDetailedRowVm>(orderedProjects.Count);
            for (var index = 0; index < orderedProjects.Count; index++)
            {
                var project = orderedProjects[index];
                var (bucket, quantity) = FfcProjectBucketHelper.Classify(
                    project.IsInstalled,
                    project.IsDelivered,
                    project.Quantity);

                var bucketLabel = FfcProjectBucketHelper.GetBucketLabel(bucket);
                var effectiveName = ResolveProjectName(project, projectNameMap);
                var costInCr = ResolveProjectCost(project.LinkedProjectId, projectCostMap);
                FfcProgressSnapshot? progress = null;
                if (project.LinkedProjectId.HasValue)
                {
                    progressByFfcProject.TryGetValue(project.Id, out progress);
                }

                projectRows.Add(new FfcDetailedRowVm(
                    FfcProjectId: project.Id,
                    Serial: index + 1,
                    ProjectName: effectiveName,
                    LinkedProjectId: project.LinkedProjectId,
                    CostInCr: costInCr,
                    Quantity: quantity,
                    Status: bucketLabel,
                    ProgressText: progress?.Text,
                    ProgressTextRaw: progress?.Text,
                    ExternalRemarkId: progress?.ExternalRemarkId,
                    ProgressSource: project.LinkedProjectId.HasValue
                        ? progress?.Source ?? FfcProgressSource.ExternalProjectRemark
                        : FfcProgressSource.FfcProjectRemark,
                    IsProgressEditable: progress?.IsEditable ?? false));
            }

            if (projectRows.Count == 0)
            {
                continue;
            }

            if (incompleteOnly && !hasIncomplete)
            {
                continue;
            }

            result.Add(new FfcDetailedGroupVm(
                FfcRecordId: group.Key.RecordId,
                CountryName: group.Key.CountryName ?? string.Empty,
                CountryCode: (group.Key.CountryIso3 ?? string.Empty).ToUpperInvariant(),
                Year: group.Key.Year,
                OverallRemarks: group.Key.OverallRemarks,
                OverallRemarksDisplay: FormatRemark(group.Key.OverallRemarks),
                Rows: projectRows,
                HasIncomplete: hasIncomplete));
        }

        return result;
    }

    // SECTION: Query builders
    private IQueryable<FfcProject> BuildBaseQuery(
        DateOnly from,
        DateOnly to,
        long? countryId,
        short? year,
        bool applyYearFilter)
    {
        var queryable = _db.FfcProjects
            .AsNoTracking()
            .Where(project => !project.Record.IsDeleted && project.Record.Country.IsActive)
            .AsQueryable();

        if (countryId.HasValue)
        {
            queryable = queryable.Where(project => project.Record.CountryId == countryId.Value);
        }

        if (applyYearFilter)
        {
            queryable = queryable.Where(project =>
                project.Record.Year >= from.Year &&
                project.Record.Year <= to.Year);
        }

        if (year.HasValue)
        {
            queryable = queryable.Where(project => project.Record.Year == year.Value);
        }

        return queryable;
    }

    // SECTION: Projection helpers
    private static string ResolveProjectName(
        FfcProjectProjection project,
        IReadOnlyDictionary<int, string> projectNameMap)
    {
        var displayName = project.Name ?? string.Empty;
        if (project.LinkedProjectId is int linkedId)
        {
            if (projectNameMap.TryGetValue(linkedId, out var linkedName) &&
                !string.IsNullOrWhiteSpace(linkedName))
            {
                return linkedName;
            }

            if (!string.IsNullOrWhiteSpace(project.LinkedProjectName))
            {
                return project.LinkedProjectName;
            }
        }

        return displayName;
    }

    private static decimal? ResolveProjectCost(
        int? linkedProjectId,
        IReadOnlyDictionary<int, decimal?> costMap)
    {
        if (linkedProjectId is not int id)
        {
            return null;
        }

        return costMap.TryGetValue(id, out var cost) ? cost : null;
    }

    private static string FormatRemark(string? remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
        {
            return string.Empty;
        }

        var text = remark.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        const int limit = 200;
        return text.Length <= limit
            ? text
            : string.Concat(text.AsSpan(0, limit), "…");
    }

    // SECTION: Data transfers
    private sealed record FfcProjectProjection(
        long Id,
        string? Name,
        string? Remarks,
        int Quantity,
        bool IsDelivered,
        bool IsInstalled,
        DateOnly? DeliveredOn,
        DateOnly? InstalledOn,
        int? LinkedProjectId,
        string? LinkedProjectName,
        long RecordId,
        int Year,
        string? OverallRemarks,
        string? CountryIso3,
        string? CountryName);
}
