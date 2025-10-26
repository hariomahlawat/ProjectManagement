using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public class ProjectTotTrackerReadService
{
    private readonly ApplicationDbContext _db;

    public ProjectTotTrackerReadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<ProjectTotTrackerRow>> GetAsync(
        ProjectTotTrackerFilter filter,
        CancellationToken cancellationToken = default)
    {
        filter ??= new ProjectTotTrackerFilter();

        var snapshots = await TryLoadSnapshotsAsync(filter, includeTotDetailColumns: true, includeRequestDetailColumns: true, cancellationToken)
            ?? await TryLoadSnapshotsAsync(filter, includeTotDetailColumns: true, includeRequestDetailColumns: false, cancellationToken)
            ?? await TryLoadSnapshotsAsync(filter, includeTotDetailColumns: false, includeRequestDetailColumns: false, cancellationToken)
            ?? new List<ProjectSnapshot>();

        return await BuildRowsAsync(snapshots, cancellationToken);
    }

    private async Task<List<ProjectSnapshot>?> TryLoadSnapshotsAsync(
        ProjectTotTrackerFilter filter,
        bool includeTotDetailColumns,
        bool includeRequestDetailColumns,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = BuildProjectSnapshotQuery(filter, includeTotDetailColumns, includeRequestDetailColumns);

            if (ShouldSimulateUndefinedColumn(filter, includeTotDetailColumns, includeRequestDetailColumns))
            {
                throw new PostgresException(
                    "Undefined column",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.UndefinedColumn);
            }

            return await ExecuteSnapshotQueryAsync(
                query,
                filter,
                includeTotDetailColumns,
                includeRequestDetailColumns,
                cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            return null;
        }
    }

    protected virtual Task<List<ProjectSnapshot>> ExecuteSnapshotQueryAsync(
        IQueryable<ProjectSnapshot> query,
        ProjectTotTrackerFilter filter,
        bool includeTotDetailColumns,
        bool includeRequestDetailColumns,
        CancellationToken cancellationToken)
        => query.ToListAsync(cancellationToken);

    protected virtual bool ShouldSimulateUndefinedColumn(
        ProjectTotTrackerFilter filter,
        bool includeTotDetailColumns,
        bool includeRequestDetailColumns) => false;

    private IQueryable<ProjectSnapshot> BuildProjectSnapshotQuery(
        ProjectTotTrackerFilter filter,
        bool includeTotDetailColumns,
        bool includeRequestDetailColumns)
    {
        var query = _db.Projects
            .AsNoTracking()
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .AsQueryable();

        if (filter.TotStatus.HasValue)
        {
            var status = filter.TotStatus.Value;
            query = query.Where(p => p.Tot != null ? p.Tot.Status == status : status == ProjectTotStatus.NotStarted);
        }

        if (filter.RequiresTotOnly)
        {
            query = query.Where(p => p.Tot == null || p.Tot.Status != ProjectTotStatus.NotRequired);
        }

        if (filter.MetCompletedOnly)
        {
            query = query.Where(p => p.Tot != null && p.Tot.MetCompletedOn.HasValue);
        }

        if (filter.OnlyPendingRequests)
        {
            query = query.Where(p => p.TotRequest != null && p.TotRequest.DecisionState == ProjectTotRequestDecisionState.Pending);
        }
        else if (filter.RequestState.HasValue)
        {
            var state = filter.RequestState.Value;
            query = query.Where(p => p.TotRequest != null && p.TotRequest.DecisionState == state);
        }

        if (filter.StartedFrom.HasValue)
        {
            var startedFrom = filter.StartedFrom.Value;
            query = query.Where(p => p.Tot != null && p.Tot.StartedOn.HasValue && p.Tot.StartedOn.Value >= startedFrom);
        }

        if (filter.StartedTo.HasValue)
        {
            var startedTo = filter.StartedTo.Value;
            query = query.Where(p => p.Tot != null && p.Tot.StartedOn.HasValue && p.Tot.StartedOn.Value <= startedTo);
        }

        if (filter.CompletedFrom.HasValue)
        {
            var completedFrom = filter.CompletedFrom.Value;
            query = query.Where(p => p.Tot != null && p.Tot.CompletedOn.HasValue && p.Tot.CompletedOn.Value >= completedFrom);
        }

        if (filter.CompletedTo.HasValue)
        {
            var completedTo = filter.CompletedTo.Value;
            query = query.Where(p => p.Tot != null && p.Tot.CompletedOn.HasValue && p.Tot.CompletedOn.Value <= completedTo);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                var pattern = $"%{EscapeLikePattern(term)}%";
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, pattern, "\\")
                    || (p.SponsoringUnit != null && EF.Functions.ILike(p.SponsoringUnit.Name, pattern, "\\")));
            }
        }

        query = query.OrderBy(p => p.Name);

        return query.Select(p => new ProjectSnapshot(
                p.Id,
                p.Name,
                p.SponsoringUnit != null ? p.SponsoringUnit.Name : null,
                p.CompletedOn,
                p.CompletedYear,
                p.LeadPoUserId,
                p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                p.LeadPoUser != null ? p.LeadPoUser.UserName : null,
                p.Tot != null ? p.Tot.Status : (ProjectTotStatus?)null,
                p.Tot != null ? p.Tot.StartedOn : null,
                p.Tot != null ? p.Tot.CompletedOn : null,
                includeTotDetailColumns && p.Tot != null ? p.Tot.MetDetails : null,
                includeTotDetailColumns && p.Tot != null ? p.Tot.MetCompletedOn : null,
                includeTotDetailColumns && p.Tot != null ? p.Tot.FirstProductionModelManufactured : null,
                includeTotDetailColumns && p.Tot != null ? p.Tot.FirstProductionModelManufacturedOn : null,
                includeTotDetailColumns && p.Tot != null ? p.Tot.LastApprovedByUserId : null,
                includeTotDetailColumns && p.Tot != null && p.Tot.LastApprovedByUser != null
                    ? p.Tot.LastApprovedByUser.FullName
                    : null,
                includeTotDetailColumns && p.Tot != null && p.Tot.LastApprovedByUser != null
                    ? p.Tot.LastApprovedByUser.UserName
                    : null,
                includeTotDetailColumns && p.Tot != null && p.Tot.LastApprovedByUser != null
                    ? p.Tot.LastApprovedByUser.Email
                    : null,
                includeTotDetailColumns && p.Tot != null ? p.Tot.LastApprovedOnUtc : null,
                p.TotRequest != null ? p.TotRequest.DecisionState : (ProjectTotRequestDecisionState?)null,
                p.TotRequest != null ? p.TotRequest.ProposedStatus : (ProjectTotStatus?)null,
                p.TotRequest != null ? p.TotRequest.ProposedStartedOn : null,
                p.TotRequest != null ? p.TotRequest.ProposedCompletedOn : null,
                includeRequestDetailColumns && p.TotRequest != null ? p.TotRequest.ProposedMetDetails : null,
                includeRequestDetailColumns && p.TotRequest != null ? p.TotRequest.ProposedMetCompletedOn : null,
                includeRequestDetailColumns && p.TotRequest != null ? p.TotRequest.ProposedFirstProductionModelManufactured : null,
                includeRequestDetailColumns && p.TotRequest != null ? p.TotRequest.ProposedFirstProductionModelManufacturedOn : null,
                p.TotRequest != null ? p.TotRequest.SubmittedByUserId : null,
                p.TotRequest != null && p.TotRequest.SubmittedByUser != null ? p.TotRequest.SubmittedByUser.FullName : null,
                p.TotRequest != null && p.TotRequest.SubmittedByUser != null ? p.TotRequest.SubmittedByUser.UserName : null,
                p.TotRequest != null && p.TotRequest.SubmittedByUser != null ? p.TotRequest.SubmittedByUser.Email : null,
                p.TotRequest != null ? p.TotRequest.SubmittedOnUtc : (DateTime?)null,
                p.TotRequest != null ? p.TotRequest.DecidedByUserId : null,
                p.TotRequest != null && p.TotRequest.DecidedByUser != null ? p.TotRequest.DecidedByUser.FullName : null,
                p.TotRequest != null && p.TotRequest.DecidedByUser != null ? p.TotRequest.DecidedByUser.UserName : null,
                p.TotRequest != null && p.TotRequest.DecidedByUser != null ? p.TotRequest.DecidedByUser.Email : null,
                p.TotRequest != null ? p.TotRequest.DecidedOnUtc : (DateTime?)null,
                includeRequestDetailColumns && p.TotRequest != null ? p.TotRequest.RowVersion : null,
                includeRequestDetailColumns && p.TotRequest != null));
    }

    private async Task<IReadOnlyList<ProjectTotTrackerRow>> BuildRowsAsync(
        List<ProjectSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
        {
            return Array.Empty<ProjectTotTrackerRow>();
        }

        var projectIds = snapshots.Select(s => s.ProjectId).ToArray();
        var remarkMap = await LoadLatestRemarksAsync(projectIds, cancellationToken);

        var rows = new List<ProjectTotTrackerRow>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            remarkMap.TryGetValue(snapshot.ProjectId, out var remarks);

            var totMetDetails = string.IsNullOrWhiteSpace(snapshot.TotMetDetails) ? null : snapshot.TotMetDetails;
            var totLastApprovedName = FormatUser(
                snapshot.TotLastApprovedByFullName,
                snapshot.TotLastApprovedByUserName,
                snapshot.TotLastApprovedByEmail);

            var requestMetDetails = string.IsNullOrWhiteSpace(snapshot.RequestedMetDetails) ? null : snapshot.RequestedMetDetails;
            var requestSubmittedBy = FormatUser(
                snapshot.RequestedByFullName,
                snapshot.RequestedByUserName,
                snapshot.RequestedByEmail);
            var requestDecidedBy = FormatUser(
                snapshot.DecidedByFullName,
                snapshot.DecidedByUserName,
                snapshot.DecidedByEmail);

            var leadProjectOfficer = !string.IsNullOrWhiteSpace(snapshot.LeadPoFullName)
                ? snapshot.LeadPoFullName
                : !string.IsNullOrWhiteSpace(snapshot.LeadPoUserName)
                    ? snapshot.LeadPoUserName
                    : snapshot.LeadPoUserId;

            rows.Add(new ProjectTotTrackerRow(
                snapshot.ProjectId,
                snapshot.ProjectName,
                snapshot.SponsoringUnit,
                snapshot.ProjectCompletedOn,
                snapshot.ProjectCompletedYear,
                snapshot.TotStatus,
                snapshot.TotStartedOn,
                snapshot.TotCompletedOn,
                totMetDetails,
                snapshot.TotMetCompletedOn,
                snapshot.TotFirstProductionModelManufactured,
                snapshot.TotFirstProductionModelManufacturedOn,
                totLastApprovedName,
                snapshot.TotLastApprovedOnUtc,
                snapshot.RequestState,
                snapshot.RequestedStatus,
                snapshot.RequestedStartedOn,
                snapshot.RequestedCompletedOn,
                requestMetDetails,
                snapshot.RequestedMetCompletedOn,
                snapshot.RequestedFirstProductionModelManufactured,
                snapshot.RequestedFirstProductionModelManufacturedOn,
                requestSubmittedBy,
                snapshot.RequestedOnUtc,
                requestDecidedBy,
                snapshot.DecidedOnUtc,
                snapshot.RequestRowVersion,
                snapshot.RequestMetadataAvailable,
                remarks?.External,
                remarks?.Internal,
                leadProjectOfficer));
        }

        return rows;
    }

    private async Task<Dictionary<int, ProjectTotRemarkPair>> LoadLatestRemarksAsync(
        int[] projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Length == 0)
        {
            return new Dictionary<int, ProjectTotRemarkPair>();
        }

        var remarkSnapshots = await _db.Remarks
            .AsNoTracking()
            .Where(r => projectIds.Contains(r.ProjectId)
                && !r.IsDeleted
                && r.Scope == RemarkScope.TransferOfTechnology
                && (r.Type == RemarkType.Internal || r.Type == RemarkType.External))
            .GroupBy(r => new { r.ProjectId, r.Type })
            .Select(g => g
                .OrderByDescending(r => r.CreatedAtUtc)
                .ThenByDescending(r => r.Id)
                .Select(r => new ProjectTotRemarkSummary(
                    r.ProjectId,
                    r.Body,
                    r.EventDate,
                    r.CreatedAtUtc,
                    r.Type))
                .FirstOrDefault())
            .ToListAsync(cancellationToken);

        var result = new Dictionary<int, ProjectTotRemarkPair>();
        foreach (var snapshot in remarkSnapshots)
        {
            if (snapshot is null)
            {
                continue;
            }

            if (!result.TryGetValue(snapshot.ProjectId, out var pair))
            {
                pair = new ProjectTotRemarkPair(null, null);
            }

            if (snapshot.Type == RemarkType.External)
            {
                pair = pair with { External = snapshot };
            }
            else
            {
                pair = pair with { Internal = snapshot };
            }

            result[snapshot.ProjectId] = pair;
        }

        return result;
    }

    private static string? FormatUser(string? fullName, string? userName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email;
        }

        return null;
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    protected sealed record ProjectSnapshot(
        int ProjectId,
        string ProjectName,
        string? SponsoringUnit,
        DateOnly? ProjectCompletedOn,
        int? ProjectCompletedYear,
        string? LeadPoUserId,
        string? LeadPoFullName,
        string? LeadPoUserName,
        ProjectTotStatus? TotStatus,
        DateOnly? TotStartedOn,
        DateOnly? TotCompletedOn,
        string? TotMetDetails,
        DateOnly? TotMetCompletedOn,
        bool? TotFirstProductionModelManufactured,
        DateOnly? TotFirstProductionModelManufacturedOn,
        string? TotLastApprovedByUserId,
        string? TotLastApprovedByFullName,
        string? TotLastApprovedByUserName,
        string? TotLastApprovedByEmail,
        DateTime? TotLastApprovedOnUtc,
        ProjectTotRequestDecisionState? RequestState,
        ProjectTotStatus? RequestedStatus,
        DateOnly? RequestedStartedOn,
        DateOnly? RequestedCompletedOn,
        string? RequestedMetDetails,
        DateOnly? RequestedMetCompletedOn,
        bool? RequestedFirstProductionModelManufactured,
        DateOnly? RequestedFirstProductionModelManufacturedOn,
        string? RequestedByUserId,
        string? RequestedByFullName,
        string? RequestedByUserName,
        string? RequestedByEmail,
        DateTime? RequestedOnUtc,
        string? DecidedByUserId,
        string? DecidedByFullName,
        string? DecidedByUserName,
        string? DecidedByEmail,
        DateTime? DecidedOnUtc,
        byte[]? RequestRowVersion,
        bool RequestMetadataAvailable);

    private sealed record ProjectTotRemarkPair(
        ProjectTotRemarkSummary? External,
        ProjectTotRemarkSummary? Internal);
}

public sealed record ProjectTotTrackerFilter
{
    public ProjectTotStatus? TotStatus { get; init; }
    public ProjectTotRequestDecisionState? RequestState { get; init; }
    public bool OnlyPendingRequests { get; init; }
    public bool RequiresTotOnly { get; init; }
    public bool MetCompletedOnly { get; init; }
    public string? SearchTerm { get; init; }
    public DateOnly? StartedFrom { get; init; }
    public DateOnly? StartedTo { get; init; }
    public DateOnly? CompletedFrom { get; init; }
    public DateOnly? CompletedTo { get; init; }
}

public sealed record ProjectTotRemarkSummary(
    int ProjectId,
    string Body,
    DateOnly? EventDate,
    DateTime CreatedAtUtc,
    RemarkType Type)
{
    public string TypeLabel => Type == RemarkType.External ? "External" : "Internal";
}

public sealed record ProjectTotTrackerRow(
    int ProjectId,
    string ProjectName,
    string? SponsoringUnit,
    DateOnly? ProjectCompletedOn,
    int? ProjectCompletedYear,
    ProjectTotStatus? TotStatus,
    DateOnly? TotStartedOn,
    DateOnly? TotCompletedOn,
    string? TotMetDetails,
    DateOnly? TotMetCompletedOn,
    bool? TotFirstProductionModelManufactured,
    DateOnly? TotFirstProductionModelManufacturedOn,
    string? TotLastApprovedBy,
    DateTime? TotLastApprovedOnUtc,
    ProjectTotRequestDecisionState? RequestState,
    ProjectTotStatus? RequestedStatus,
    DateOnly? RequestedStartedOn,
    DateOnly? RequestedCompletedOn,
    string? RequestedMetDetails,
    DateOnly? RequestedMetCompletedOn,
    bool? RequestedFirstProductionModelManufactured,
    DateOnly? RequestedFirstProductionModelManufacturedOn,
    string? RequestedBy,
    DateTime? RequestedOnUtc,
    string? DecidedBy,
    DateTime? DecidedOnUtc,
    byte[]? RequestRowVersion,
    bool RequestMetadataAvailable,
    ProjectTotRemarkSummary? LatestExternalRemark,
    ProjectTotRemarkSummary? LatestInternalRemark,
    string? LeadProjectOfficer);
