using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

        var query = _db.Projects
            .AsNoTracking()
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .Include(p => p.SponsoringUnit)
            .Include(p => p.Tot)
                .ThenInclude(t => t.LastApprovedByUser)
            .Include(p => p.TotRequest)
                .ThenInclude(r => r.SubmittedByUser)
            .Include(p => p.TotRequest)
                .ThenInclude(r => r.DecidedByUser)
            .OrderBy(p => p.Name)
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

        var projects = await query.ToListAsync(cancellationToken);

        var rows = new List<ProjectTotTrackerRow>(projects.Count);
        foreach (var project in projects)
        {
            var tot = project.Tot;
            var request = project.TotRequest;

            rows.Add(new ProjectTotTrackerRow(
                project.Id,
                project.Name,
                project.SponsoringUnit?.Name,
                tot?.Status,
                tot?.StartedOn,
                tot?.CompletedOn,
                string.IsNullOrWhiteSpace(tot?.Remarks) ? null : tot!.Remarks,
                string.IsNullOrWhiteSpace(tot?.LastApprovedByUser?.FullName) ? tot?.LastApprovedByUserId : tot?.LastApprovedByUser!.FullName,
                tot?.LastApprovedOnUtc,
                request?.DecisionState,
                request?.ProposedStatus,
                request?.ProposedStartedOn,
                request?.ProposedCompletedOn,
                string.IsNullOrWhiteSpace(request?.ProposedRemarks) ? null : request!.ProposedRemarks,
                string.IsNullOrWhiteSpace(request?.SubmittedByUser?.FullName) ? request?.SubmittedByUserId : request?.SubmittedByUser!.FullName,
                request?.SubmittedOnUtc,
                string.IsNullOrWhiteSpace(request?.DecidedByUser?.FullName) ? request?.DecidedByUserId : request?.DecidedByUser!.FullName,
                request?.DecidedOnUtc,
                string.IsNullOrWhiteSpace(request?.DecisionRemarks) ? null : request!.DecisionRemarks,
                request?.RowVersion));
        }

        return rows;
    }
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
    string? TotRemarks,
    string? TotLastApprovedBy,
    DateTime? TotLastApprovedOnUtc,
    ProjectTotRequestDecisionState? RequestState,
    ProjectTotStatus? RequestedStatus,
    DateOnly? RequestedStartedOn,
    DateOnly? RequestedCompletedOn,
    string? RequestedRemarks,
    string? RequestedBy,
    DateTime? RequestedOnUtc,
    string? DecidedBy,
    DateTime? DecidedOnUtc,
    string? DecisionRemarks,
    byte[]? RequestRowVersion);
