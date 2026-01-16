using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;

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

    public FfcQueryService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
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
        var stageSummaryMap = new Dictionary<int, string?>(linkedProjectIds.Length);

        if (linkedProjectIds.Length > 0)
        {
            var projectSnapshots = await _db.Projects
                .AsNoTracking()
                .Where(project => linkedProjectIds.Contains(project.Id))
                .Select(project => new
                {
                    project.Id,
                    project.Name,
                    project.CostLakhs,
                    Stages = project.ProjectStages
                        .Select(stage => new
                        {
                            stage.StageCode,
                            stage.SortOrder,
                            stage.Status,
                            stage.CompletedOn
                        })
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            projectNameMap = projectSnapshots
                .ToDictionary(x => x.Id, x => x.Name ?? string.Empty);

            projectCostMap = projectSnapshots
                .ToDictionary(x => x.Id, x => ConvertLakhsToCr(x.CostLakhs));

            stageSummaryMap = projectSnapshots
                .ToDictionary(
                    x => x.Id,
                    x => BuildStageSummary(x.Stages.Select(stage => new ProjectStage
                    {
                        StageCode = stage.StageCode,
                        SortOrder = stage.SortOrder,
                        Status = stage.Status,
                        CompletedOn = stage.CompletedOn
                    })));
        }

        var remarkMap = linkedProjectIds.Length == 0
            ? new Dictionary<int, string?>()
            : await LoadRemarkSummariesAsync(linkedProjectIds, from, to, cancellationToken);

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
            var projectRows = group
                .OrderBy(project => project.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select((project, index) =>
                {
                    var (bucket, quantity) = FfcProjectBucketHelper.Classify(project.IsInstalled, project.IsDelivered, project.Quantity);
                    var bucketLabel = FfcProjectBucketHelper.GetBucketLabel(bucket);
                    var effectiveName = ResolveProjectName(project, projectNameMap);
                    var costInCr = ResolveProjectCost(project.LinkedProjectId, projectCostMap);
                    var progressInfo = BuildProgressInfo(project, bucket, stageSummaryMap, remarkMap);

                    return new FfcDetailedRowVm(
                        FfcProjectId: project.Id,
                        Serial: index + 1,
                        ProjectName: effectiveName,
                        LinkedProjectId: project.LinkedProjectId,
                        CostInCr: costInCr,
                        Quantity: quantity,
                        Status: bucketLabel,
                        ProgressText: progressInfo.Text,
                        ProgressSource: progressInfo.Source,
                        IsProgressEditable: progressInfo.Source != FfcProgressSource.Computed
                    );
                })
                .ToList();

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

    private static ProgressInfo BuildProgressInfo(
        FfcProjectProjection project,
        FfcDeliveryBucket bucket,
        IReadOnlyDictionary<int, string?> stageSummaryMap,
        IReadOnlyDictionary<int, string?> remarkMap)
    {
        string? remarkFromFfc = FormatRemark(project.Remarks);

        if (bucket == FfcDeliveryBucket.Planned)
        {
            if (project.LinkedProjectId is int linkedId && TryGetNonEmpty(remarkMap, linkedId, out var externalRemark))
            {
                return new ProgressInfo(externalRemark, FfcProgressSource.ExternalProjectRemark);
            }

            return new ProgressInfo(remarkFromFfc, FfcProgressSource.FfcProjectRemark);
        }

        if (project.LinkedProjectId is int deliveredId)
        {
            if (TryGetNonEmpty(stageSummaryMap, deliveredId, out var stageSummary))
            {
                return new ProgressInfo(stageSummary, FfcProgressSource.Computed);
            }

            if (TryGetNonEmpty(remarkMap, deliveredId, out var externalRemark))
            {
                return new ProgressInfo(externalRemark, FfcProgressSource.ExternalProjectRemark);
            }
        }

        if (bucket == FfcDeliveryBucket.Installed && project.InstalledOn is DateOnly installedOn)
        {
            return new ProgressInfo($"Installed on {FormatDate(installedOn)}", FfcProgressSource.Computed);
        }

        if (bucket == FfcDeliveryBucket.DeliveredNotInstalled && project.DeliveredOn is DateOnly deliveredOn)
        {
            return new ProgressInfo($"Delivered on {FormatDate(deliveredOn)}", FfcProgressSource.Computed);
        }

        return new ProgressInfo(remarkFromFfc, FfcProgressSource.FfcProjectRemark);
    }

    private sealed record ProgressInfo(string? Text, FfcProgressSource Source);

    private static bool TryGetNonEmpty(IReadOnlyDictionary<int, string?> source, int key, out string value)
    {
        if (source.TryGetValue(key, out var raw) && raw is not null)
        {
            var text = raw;
            if (!string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static decimal? ConvertLakhsToCr(decimal? costLakhs)
    {
        if (!costLakhs.HasValue)
        {
            return null;
        }

        return decimal.Divide(costLakhs.Value, 100m);
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

    private static string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string? BuildStageSummary(IEnumerable<ProjectStage> projectStages)
    {
        static string FmtDate(DateOnly? value) => value?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? string.Empty;

        var stages = projectStages?
            .Where(s => !StageCodes.IsTot(s.StageCode))
            .OrderBy(s => s.SortOrder)
            .ToList() ?? new List<ProjectStage>();

        if (stages.Count == 0)
        {
            return null;
        }

        var paymentStage = stages.FirstOrDefault(s => StageCodes.IsPayment(s.StageCode));
        if (paymentStage is not null)
        {
            var cutoff = paymentStage.SortOrder;
            stages = stages.Where(s => s.SortOrder <= cutoff).ToList();
        }

        var topCompleted = stages
            .Where(s => s.Status == StageStatus.Completed)
            .OrderByDescending(s => s.SortOrder)
            .ThenByDescending(s => s.CompletedOn ?? DateOnly.MinValue)
            .FirstOrDefault();

        var started = stages.FirstOrDefault(s => s.Status is StageStatus.InProgress or StageStatus.Blocked);

        var missed = topCompleted is null
            ? Array.Empty<string>()
            : stages
                .Where(s => s.SortOrder < topCompleted.SortOrder && s.Status != StageStatus.Completed)
                .Select(s => StageCodes.DisplayNameOf(s.StageCode))
                .ToArray();

        if (started is not null)
        {
            var previous = stages.LastOrDefault(s => s.SortOrder < started.SortOrder && s.Status == StageStatus.Completed);
            var previousLabel = previous is null ? null : StageCodes.DisplayNameOf(previous.StageCode);
            var previousDate = previous is null ? string.Empty : FmtDate(previous.CompletedOn);
            var nowLabel = StageCodes.DisplayNameOf(started.StageCode);
            var nowState = started.Status == StageStatus.Blocked ? "Blocked" : "In progress";
            var missedPart = missed.Length > 0 ? $" — missed: {string.Join(", ", missed)}" : string.Empty;

            if (!string.IsNullOrWhiteSpace(previousLabel))
            {
                return $"Last: {previousLabel} ({previousDate}) → {nowLabel} ({nowState}){missedPart}";
            }

            return $"Now: {nowLabel} ({nowState}){missedPart}";
        }

        if (topCompleted is null)
        {
            return null;
        }

        var topLabel = StageCodes.DisplayNameOf(topCompleted.StageCode);
        var topDate = FmtDate(topCompleted.CompletedOn);
        var trailing = missed.Length > 0 ? $" — pending: {string.Join(", ", missed)}" : string.Empty;

        return $"Completed: {topLabel} ({topDate}){trailing}";
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
    private async Task<Dictionary<int, string?>> LoadRemarkSummariesAsync(
        int[] projectIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var fromDateTime = from.ToDateTime(TimeOnly.MinValue);
        var toDateTime = to.ToDateTime(new TimeOnly(23, 59, 59));

        var remarks = await _db.Remarks
            .AsNoTracking()
            .Where(remark => projectIds.Contains(remark.ProjectId)
                && !remark.IsDeleted
                && remark.Type == RemarkType.External)
            .Where(remark =>
                (remark.EventDate != default && remark.EventDate >= from && remark.EventDate <= to)
                || (remark.EventDate == default && remark.CreatedAtUtc >= fromDateTime && remark.CreatedAtUtc <= toDateTime))
            .Select(remark => new
            {
                remark.ProjectId,
                remark.Id,
                remark.CreatedAtUtc,
                remark.Body
            })
            .ToListAsync(cancellationToken);

        return remarks
            .GroupBy(remark => remark.ProjectId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group
                        .OrderByDescending(item => item.CreatedAtUtc)
                        .ThenByDescending(item => item.Id)
                        .FirstOrDefault();

                    return latest is null ? null : FormatRemark(latest.Body);
                });
    }
}
