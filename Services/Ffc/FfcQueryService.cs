using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models.Remarks;
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

public enum FfcProgressSource
{
    ExternalProjectRemark = 0,
    FfcProjectRemark = 1,
    Computed = 2
}

// SECTION: Query service
public sealed class FfcQueryService : IFfcQueryService
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectCostResolver _costResolver;

    public FfcQueryService(ApplicationDbContext db, IProjectCostResolver costResolver)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _costResolver = costResolver ?? throw new ArgumentNullException(nameof(costResolver));
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
            var costResolutions = await _costResolver.ResolveCostInCrAsync(linkedProjectIds, cancellationToken);
            projectCostMap = costResolutions
                .ToDictionary(entry => entry.Key, entry => entry.Value.CostInCr);

        }

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
                var (bucket, quantity) = FfcProjectBucketHelper.Classify(project.IsInstalled, project.IsDelivered, project.Quantity);
                var bucketLabel = FfcProjectBucketHelper.GetBucketLabel(bucket);
                var effectiveName = ResolveProjectName(project, projectNameMap);
                var costInCr = ResolveProjectCost(project.LinkedProjectId, projectCostMap);
                var remarkProjectId = project.LinkedProjectId;
                var progressText = remarkProjectId.HasValue
                    ? await FetchLatestRemarkAsync(remarkProjectId.Value, RemarkType.External, cancellationToken)
                    : null;
                var latestExternalRemarkId = remarkProjectId.HasValue
                    ? await FetchLatestRemarkIdAsync(remarkProjectId.Value, RemarkType.External, cancellationToken)
                    : null;

                projectRows.Add(new FfcDetailedRowVm(
                    FfcProjectId: project.Id,
                    Serial: index + 1,
                    ProjectName: effectiveName,
                    LinkedProjectId: project.LinkedProjectId,
                    CostInCr: costInCr,
                    Quantity: quantity,
                    Status: bucketLabel,
                    ProgressText: progressText,
                    ProgressTextRaw: progressText,
                    ExternalRemarkId: latestExternalRemarkId,
                    ProgressSource: FfcProgressSource.ExternalProjectRemark,
                    IsProgressEditable: project.LinkedProjectId.HasValue
                ));
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
    private IQueryable<FfcProject> BuildBaseQuery(DateOnly from, DateOnly to, long? countryId, short? year, bool applyYearFilter)
    {
        var queryable = _db.FfcProjects
            .AsNoTracking()
            .Where(project => !project.Record.IsDeleted && project.Record.Country.IsActive)
            .Include(project => project.Record)
                .ThenInclude(record => record.Country)
            .AsQueryable();

        if (countryId.HasValue)
        {
            queryable = queryable.Where(project => project.Record.CountryId == countryId.Value);
        }

        if (applyYearFilter)
        {
            queryable = queryable.Where(project => project.Record.Year >= from.Year && project.Record.Year <= to.Year);
        }

        if (year.HasValue)
        {
            queryable = queryable.Where(project => project.Record.Year == year.Value);
        }

        return queryable;
    }

    // SECTION: Projection helpers
    private static string ResolveProjectName(FfcProjectProjection project, IReadOnlyDictionary<int, string> projectNameMap)
    {
        var displayName = project.Name ?? string.Empty;
        if (project.LinkedProjectId is int linkedId)
        {
            if (projectNameMap.TryGetValue(linkedId, out var linkedName) && !string.IsNullOrWhiteSpace(linkedName))
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

    private static decimal? ResolveProjectCost(int? linkedProjectId, IReadOnlyDictionary<int, decimal?> costMap)
    {
        if (linkedProjectId is not int id)
        {
            return null;
        }

        return costMap.TryGetValue(id, out var cost) ? cost : null;
    }

    private static string? NormalizeRemarkBody(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
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
        return text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit), "…");
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

    // SECTION: External remarks
    private async Task<string?> FetchLatestRemarkAsync(
        int projectId,
        RemarkType remarkType,
        CancellationToken cancellationToken)
    {
        var latest = await _db.Remarks
            .AsNoTracking()
            .Where(remark => remark.ProjectId == projectId
                && !remark.IsDeleted
                && remark.Type == remarkType
                && remark.Body != null)
            .Select(remark => new
            {
                remark.Body,
                SortTimestamp = remark.LastEditedAtUtc ?? remark.CreatedAtUtc,
                remark.Id
            })
            .OrderByDescending(remark => remark.SortTimestamp)
            .ThenByDescending(remark => remark.Id)
            .ToListAsync(cancellationToken);

        foreach (var item in latest)
        {
            var normalized = NormalizeRemarkBody(item.Body);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private async Task<int?> FetchLatestRemarkIdAsync(
        int projectId,
        RemarkType remarkType,
        CancellationToken cancellationToken)
    {
        var latest = await _db.Remarks
            .AsNoTracking()
            .Where(remark => remark.ProjectId == projectId
                && !remark.IsDeleted
                && remark.Type == remarkType
                && remark.Body != null)
            .Select(remark => new
            {
                remark.Id,
                remark.Body,
                SortTimestamp = remark.LastEditedAtUtc ?? remark.CreatedAtUtc
            })
            .OrderByDescending(remark => remark.SortTimestamp)
            .ThenByDescending(remark => remark.Id)
            .ToListAsync(cancellationToken);

        foreach (var item in latest)
        {
            if (!string.IsNullOrWhiteSpace(NormalizeRemarkBody(item.Body)))
            {
                return item.Id;
            }
        }

        return null;
    }
}
