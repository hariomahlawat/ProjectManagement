using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProjectTotTrackerReadService
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

        try
        {
            var snapshots = await BuildProjectSnapshotQuery(filter, includeExtendedColumns: true)
                .ToListAsync(cancellationToken);
            return await BuildRowsAsync(snapshots, cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            var snapshots = await BuildProjectSnapshotQuery(filter, includeExtendedColumns: false)
                .ToListAsync(cancellationToken);
            return await BuildRowsAsync(snapshots, cancellationToken);
        }
    }

    private IQueryable<ProjectSnapshot> BuildProjectSnapshotQuery(
        ProjectTotTrackerFilter filter,
        bool includeExtendedColumns)
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

        if (filter.OnlyPendingRequests)
        {
            query = query.Where(p => p.TotRequest != null && p.TotRequest.DecisionState == ProjectTotRequestDecisionState.Pending);
        }
        else if (filter.RequestState.HasValue)
        {
            var state = filter.RequestState.Value;
            query = query.Where(p => p.TotRequest != null && p.TotRequest.DecisionState == state);
        }

        query = query.OrderBy(p => p.Name);

        return query.Select(p => new ProjectSnapshot(
            p.Id,
            p.Name,
            p.SponsoringUnit != null ? p.SponsoringUnit.Name : null,
            p.LeadPoUserId,
            p.Tot != null ? p.Tot.Status : (ProjectTotStatus?)null,
            p.Tot != null ? p.Tot.StartedOn : null,
            p.Tot != null ? p.Tot.CompletedOn : null,
            includeExtendedColumns && p.Tot != null ? p.Tot.MetDetails : null,
            includeExtendedColumns && p.Tot != null ? p.Tot.MetCompletedOn : null,
            includeExtendedColumns && p.Tot != null ? p.Tot.FirstProductionModelManufactured : null,
            includeExtendedColumns && p.Tot != null ? p.Tot.FirstProductionModelManufacturedOn : null,
            p.Tot != null ? p.Tot.Remarks : null,
            p.Tot != null ? p.Tot.LastApprovedByUserId : null,
            p.Tot != null ? p.Tot.LastApprovedByUser != null ? p.Tot.LastApprovedByUser.FullName : null : null,
            p.Tot != null ? p.Tot.LastApprovedOnUtc : null,
            p.TotRequest != null ? p.TotRequest.DecisionState : (ProjectTotRequestDecisionState?)null,
            p.TotRequest != null ? p.TotRequest.ProposedStatus : (ProjectTotStatus?)null,
            p.TotRequest != null ? p.TotRequest.ProposedStartedOn : null,
            p.TotRequest != null ? p.TotRequest.ProposedCompletedOn : null,
            includeExtendedColumns && p.TotRequest != null ? p.TotRequest.ProposedMetDetails : null,
            includeExtendedColumns && p.TotRequest != null ? p.TotRequest.ProposedMetCompletedOn : null,
            includeExtendedColumns && p.TotRequest != null ? p.TotRequest.ProposedFirstProductionModelManufactured : null,
            includeExtendedColumns && p.TotRequest != null ? p.TotRequest.ProposedFirstProductionModelManufacturedOn : null,
            p.TotRequest != null ? p.TotRequest.ProposedRemarks : null,
            p.TotRequest != null ? p.TotRequest.SubmittedByUserId : null,
            p.TotRequest != null ? p.TotRequest.SubmittedByUser != null ? p.TotRequest.SubmittedByUser.FullName : null : null,
            p.TotRequest != null ? p.TotRequest.SubmittedOnUtc : (DateTime?)null,
            p.TotRequest != null ? p.TotRequest.DecidedByUserId : null,
            p.TotRequest != null ? p.TotRequest.DecidedByUser != null ? p.TotRequest.DecidedByUser.FullName : null : null,
            p.TotRequest != null ? p.TotRequest.DecidedOnUtc : (DateTime?)null,
            p.TotRequest != null ? p.TotRequest.DecisionRemarks : null,
            p.TotRequest != null ? p.TotRequest.RowVersion : null));
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
        var latestApprovedMap = await LoadLatestApprovedUpdatesAsync(projectIds, cancellationToken);
        var pendingCounts = await LoadPendingCountsAsync(projectIds, cancellationToken);

        var rows = new List<ProjectTotTrackerRow>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            latestApprovedMap.TryGetValue(snapshot.ProjectId, out var latestApproved);
            pendingCounts.TryGetValue(snapshot.ProjectId, out var pendingCount);

            var totRemarks = string.IsNullOrWhiteSpace(snapshot.TotRemarks) ? null : snapshot.TotRemarks;
            var totMetDetails = string.IsNullOrWhiteSpace(snapshot.TotMetDetails) ? null : snapshot.TotMetDetails;
            var totLastApprovedName = !string.IsNullOrWhiteSpace(snapshot.TotLastApprovedByFullName)
                ? snapshot.TotLastApprovedByFullName
                : snapshot.TotLastApprovedByUserId;

            var requestRemarks = string.IsNullOrWhiteSpace(snapshot.RequestedRemarks) ? null : snapshot.RequestedRemarks;
            var requestMetDetails = string.IsNullOrWhiteSpace(snapshot.RequestedMetDetails) ? null : snapshot.RequestedMetDetails;
            var requestSubmittedBy = !string.IsNullOrWhiteSpace(snapshot.RequestedByFullName)
                ? snapshot.RequestedByFullName
                : snapshot.RequestedByUserId;
            var requestDecidedBy = !string.IsNullOrWhiteSpace(snapshot.DecidedByFullName)
                ? snapshot.DecidedByFullName
                : snapshot.DecidedByUserId;
            var decisionRemarks = string.IsNullOrWhiteSpace(snapshot.DecisionRemarks) ? null : snapshot.DecisionRemarks;

            rows.Add(new ProjectTotTrackerRow(
                snapshot.ProjectId,
                snapshot.ProjectName,
                snapshot.SponsoringUnit,
                snapshot.TotStatus,
                snapshot.TotStartedOn,
                snapshot.TotCompletedOn,
                totMetDetails,
                snapshot.TotMetCompletedOn,
                snapshot.TotFirstProductionModelManufactured,
                snapshot.TotFirstProductionModelManufacturedOn,
                totRemarks,
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
                requestRemarks,
                requestSubmittedBy,
                snapshot.RequestedOnUtc,
                requestDecidedBy,
                snapshot.DecidedOnUtc,
                decisionRemarks,
                snapshot.RequestRowVersion,
                latestApproved?.Body,
                latestApproved?.EventDate,
                latestApproved?.PublishedOnUtc,
                pendingCount,
                snapshot.LeadPoUserId));
        }

        return rows;
    }

    private async Task<Dictionary<int, LatestApprovedUpdate>> LoadLatestApprovedUpdatesAsync(
        int[] projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Length == 0)
        {
            return new Dictionary<int, LatestApprovedUpdate>();
        }

        var latestApprovedEntries = await _db.ProjectTotProgressUpdates
            .AsNoTracking()
            .Where(u => projectIds.Contains(u.ProjectId)
                && u.State == ProjectTotProgressUpdateState.Approved)
            .OrderByDescending(u => u.PublishedOnUtc ?? u.SubmittedOnUtc)
            .ThenByDescending(u => u.Id)
            .Select(u => new LatestApprovedUpdate(
                u.ProjectId,
                u.Body,
                u.EventDate,
                u.PublishedOnUtc ?? u.SubmittedOnUtc))
            .ToListAsync(cancellationToken);

        var latestApprovedMap = new Dictionary<int, LatestApprovedUpdate>(latestApprovedEntries.Count);
        foreach (var entry in latestApprovedEntries)
        {
            if (!latestApprovedMap.ContainsKey(entry.ProjectId))
            {
                latestApprovedMap[entry.ProjectId] = entry;
            }
        }

        return latestApprovedMap;
    }

    private Task<Dictionary<int, int>> LoadPendingCountsAsync(int[] projectIds, CancellationToken cancellationToken)
    {
        if (projectIds.Length == 0)
        {
            return Task.FromResult(new Dictionary<int, int>());
        }

        return _db.ProjectTotProgressUpdates
            .AsNoTracking()
            .Where(u => projectIds.Contains(u.ProjectId)
                && u.State == ProjectTotProgressUpdateState.Pending)
            .GroupBy(u => u.ProjectId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    private sealed record LatestApprovedUpdate(
        int ProjectId,
        string Body,
        DateOnly? EventDate,
        DateTime? PublishedOnUtc);
}

public sealed record ProjectTotTrackerFilter
{
    public ProjectTotStatus? TotStatus { get; init; }
    public ProjectTotRequestDecisionState? RequestState { get; init; }
    public bool OnlyPendingRequests { get; init; }
}

public sealed record ProjectTotTrackerRow(
    int ProjectId,
    string ProjectName,
    string? SponsoringUnit,
    ProjectTotStatus? TotStatus,
    DateOnly? TotStartedOn,
    DateOnly? TotCompletedOn,
    string? TotMetDetails,
    DateOnly? TotMetCompletedOn,
    bool? TotFirstProductionModelManufactured,
    DateOnly? TotFirstProductionModelManufacturedOn,
    string? TotRemarks,
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
    string? RequestedRemarks,
    string? RequestedBy,
    DateTime? RequestedOnUtc,
    string? DecidedBy,
    DateTime? DecidedOnUtc,
    string? DecisionRemarks,
    byte[]? RequestRowVersion,
    string? LatestApprovedUpdateBody,
    DateOnly? LatestApprovedUpdateEventDate,
    DateTime? LatestApprovedUpdatePublishedOnUtc,
    int PendingUpdateCount,
    string? LeadProjectOfficerUserId);

private sealed record ProjectSnapshot(
    int ProjectId,
    string ProjectName,
    string? SponsoringUnit,
    string? LeadPoUserId,
    ProjectTotStatus? TotStatus,
    DateOnly? TotStartedOn,
    DateOnly? TotCompletedOn,
    string? TotMetDetails,
    DateOnly? TotMetCompletedOn,
    bool? TotFirstProductionModelManufactured,
    DateOnly? TotFirstProductionModelManufacturedOn,
    string? TotRemarks,
    string? TotLastApprovedByUserId,
    string? TotLastApprovedByFullName,
    DateTime? TotLastApprovedOnUtc,
    ProjectTotRequestDecisionState? RequestState,
    ProjectTotStatus? RequestedStatus,
    DateOnly? RequestedStartedOn,
    DateOnly? RequestedCompletedOn,
    string? RequestedMetDetails,
    DateOnly? RequestedMetCompletedOn,
    bool? RequestedFirstProductionModelManufactured,
    DateOnly? RequestedFirstProductionModelManufacturedOn,
    string? RequestedRemarks,
    string? RequestedByUserId,
    string? RequestedByFullName,
    DateTime? RequestedOnUtc,
    string? DecidedByUserId,
    string? DecidedByFullName,
    DateTime? DecidedOnUtc,
    string? DecisionRemarks,
    byte[]? RequestRowVersion);
