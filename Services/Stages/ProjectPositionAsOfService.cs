using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Stages;

/// <summary>A project's lifecycle on a past date.</summary>
public enum LifecycleAsOf
{
    Active,
    Completed,
    Cancelled,
    /// <summary>Completed or cancelled today, but the record cannot say whether that was by the date (legacy year-only data).</summary>
    NotKnown
}

/// <summary>Where one project stood on a past date (see <see cref="StageStateAsOfResolver"/>).</summary>
public sealed record ProjectPositionAsOf(
    int ProjectId,
    LifecycleAsOf Lifecycle,
    StagePositionAsOf Stage,
    /// <summary>True when AoN was completed (or skipped) by the date; null when that cannot be told.</summary>
    bool? PastAon,
    /// <summary>The AoN completion date, when it is recorded and on or before the date.</summary>
    DateOnly? AonDate,
    bool AonSkipped,
    /// <summary>True when a supply order had been placed by the date; null when that cannot be told.</summary>
    bool? SupplyOrderPlaced,
    DateOnly? SupplyOrderDate);

public interface IProjectPositionAsOfService
{
    /// <summary>Where each project stood at the end of <paramref name="asOf"/> (IST), from today's records.</summary>
    Task<IReadOnlyDictionary<int, ProjectPositionAsOf>> ResolveAsync(
        IReadOnlyCollection<int> projectIds,
        DateOnly asOf,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectPositionAsOfService : IProjectPositionAsOfService
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    private readonly ApplicationDbContext _db;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IWorkflowStageMetadataProvider _workflowStages;

    public ProjectPositionAsOfService(
        ApplicationDbContext db,
        IProjectStageWorkflowPolicy workflowPolicy,
        IWorkflowStageMetadataProvider workflowStages)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _workflowStages = workflowStages ?? throw new ArgumentNullException(nameof(workflowStages));
    }

    public async Task<IReadOnlyDictionary<int, ProjectPositionAsOf>> ResolveAsync(
        IReadOnlyCollection<int> projectIds,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        var ids = projectIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, ProjectPositionAsOf>();
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => ids.Contains(project.Id) && !project.IsDeleted)
            .Select(project => new
            {
                project.Id,
                project.WorkflowVersion,
                project.LifecycleStatus,
                project.CompletedOn,
                project.CompletedYear,
                project.CompletedMonth,
                project.CancelledOn
            })
            .ToListAsync(cancellationToken);
        var stages = (await _db.ProjectStages
                .AsNoTracking()
                .Where(stage => ids.Contains(stage.ProjectId))
                .ToListAsync(cancellationToken))
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var logs = (await _db.StageChangeLogs
                .AsNoTracking()
                .Where(log => ids.Contains(log.ProjectId))
                .Select(log => new { log.ProjectId, log.StageCode, log.ToStatus, log.At })
                .ToListAsync(cancellationToken))
            .GroupBy(log => log.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StageLogFact>)group
                    .Select(log => new StageLogFact(log.StageCode, log.ToStatus, DateOnly.FromDateTime(log.At.ToOffset(Ist).DateTime)))
                    .ToList());
        var supplyOrderDates = (await _db.ProjectSupplyOrderFacts
                .AsNoTracking()
                .Where(fact => ids.Contains(fact.ProjectId))
                .Select(fact => new { fact.ProjectId, fact.SupplyOrderDate, fact.CreatedOnUtc, fact.Id })
                .ToListAsync(cancellationToken))
            .GroupBy(fact => fact.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(fact => fact.CreatedOnUtc).ThenByDescending(fact => fact.Id).First().SupplyOrderDate);
        var workflows = await _workflowPolicy.GetManyAsync(ids, cancellationToken);

        var result = new Dictionary<int, ProjectPositionAsOf>(projects.Count);
        foreach (var project in projects)
        {
            var rows = stages.GetValueOrDefault(project.Id) ?? new List<ProjectStage>();
            workflows.TryGetValue(project.Id, out var workflow);
            var lifecycle = LifecycleOn(project.LifecycleStatus, project.CompletedOn, project.CompletedYear, project.CompletedMonth, project.CancelledOn, asOf);
            var soDate = supplyOrderDates.TryGetValue(project.Id, out var recordedSo) ? recordedSo : (DateOnly?)null;
            var aonRow = rows.FirstOrDefault(stage => string.Equals(stage.StageCode, StageCodes.AON, StringComparison.OrdinalIgnoreCase));

            if (lifecycle != LifecycleAsOf.Active)
            {
                // Completed by the date: every stage was behind it, AoN by the same rule as today's view.
                var pastAon = lifecycle == LifecycleAsOf.Completed
                    ? aonRow is null || aonRow.Status is StageStatus.Completed or StageStatus.Skipped
                    : (bool?)null;
                result[project.Id] = new ProjectPositionAsOf(
                    project.Id,
                    lifecycle,
                    StagePositionAsOf.None,
                    lifecycle == LifecycleAsOf.Cancelled ? null : pastAon,
                    aonRow?.CompletedOn is { } aonEnd && aonEnd <= asOf ? aonEnd : null,
                    aonRow?.Status == StageStatus.Skipped,
                    null,
                    soDate is { } so && so <= asOf ? so : null);
                continue;
            }

            var facts = rows
                .Select(stage => new StageFactNow(stage.StageCode, stage.Status, stage.ActualStart, stage.CompletedOn))
                .ToList();
            var states = StageStateAsOfResolver.Resolve(facts, logs.GetValueOrDefault(project.Id) ?? Array.Empty<StageLogFact>(), workflow, asOf);
            var lifecycleStages = ProjectLifecyclePositionResolver.BuildProjectStages(
                new Project { Id = project.Id, WorkflowVersion = project.WorkflowVersion, LifecycleStatus = project.LifecycleStatus, ProjectStages = rows },
                _workflowStages);
            var position = StageStateAsOfResolver.ResolvePosition(lifecycleStages, states);

            result[project.Id] = new ProjectPositionAsOf(
                project.Id,
                LifecycleAsOf.Active,
                position,
                PastAonOn(states, position),
                aonRow?.CompletedOn is { } aonDate && aonDate <= asOf ? aonDate : null,
                states.TryGetValue(StageCodes.AON, out var aonState) && aonState == StageStateAsOf.Skipped,
                SupplyOrderOn(states, position, soDate, asOf),
                soDate is { } placedOn && placedOn <= asOf ? placedOn : null);
        }

        return result;
    }

    /// <summary>
    /// Completed or cancelled by the date: a recorded date decides; a completion month or year decides
    /// only when the whole month or year lies on one side of the date; otherwise not known.
    /// </summary>
    public static LifecycleAsOf LifecycleOn(
        ProjectLifecycleStatus status,
        DateOnly? completedOn,
        int? completedYear,
        short? completedMonth,
        DateOnly? cancelledOn,
        DateOnly asOf)
    {
        switch (status)
        {
            case ProjectLifecycleStatus.Completed:
                if (completedOn is { } on)
                {
                    return on <= asOf ? LifecycleAsOf.Completed : LifecycleAsOf.Active;
                }

                if (completedYear is { } year)
                {
                    var (first, last) = completedMonth is { } month and >= 1 and <= 12
                        ? (new DateOnly(year, month, 1), new DateOnly(year, month, 1).AddMonths(1).AddDays(-1))
                        : (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
                    return last <= asOf ? LifecycleAsOf.Completed : first > asOf ? LifecycleAsOf.Active : LifecycleAsOf.NotKnown;
                }

                return LifecycleAsOf.NotKnown;
            case ProjectLifecycleStatus.Cancelled:
                return cancelledOn is { } cancelled
                    ? cancelled <= asOf ? LifecycleAsOf.Cancelled : LifecycleAsOf.Active
                    : LifecycleAsOf.NotKnown;
            default:
                return LifecycleAsOf.Active;
        }
    }

    private static bool? PastAonOn(IReadOnlyDictionary<string, StageStateAsOf> states, StagePositionAsOf position)
    {
        if (states.TryGetValue(StageCodes.AON, out var aon))
        {
            return aon switch
            {
                StageStateAsOf.Finished or StageStateAsOf.Skipped => true,
                StageStateAsOf.Unknown => null,
                _ => false
            };
        }

        // No AoN stage in the workflow: past it once the stage is beyond the Pre-AoN phase.
        return position.Candidates.Count == 0
            ? position.IsConcluded ? true : null
            : position.Candidates.All(stage => StagePhases.Of(stage.Code) != StagePhase.PreAon) ? true
            : position.Candidates.All(stage => StagePhases.Of(stage.Code) == StagePhase.PreAon) ? false
            : null;
    }

    private static bool? SupplyOrderOn(
        IReadOnlyDictionary<string, StageStateAsOf> states,
        StagePositionAsOf position,
        DateOnly? supplyOrderDate,
        DateOnly asOf)
    {
        // The recorded supply-order date decides when there is one.
        if (supplyOrderDate is { } placed)
        {
            return placed <= asOf;
        }

        if (states.TryGetValue(StageCodes.SO, out var so))
        {
            switch (so)
            {
                case StageStateAsOf.Finished:
                    return true;
                case StageStateAsOf.Unknown:
                    return null;
                case StageStateAsOf.Skipped:
                    break;
                default:
                    return false;
            }
        }

        // No supply-order stage (or it was skipped): placed once every candidate stage is beyond it.
        if (position.IsConcluded && position.Candidates.Count == 0)
        {
            return true;
        }

        var maturities = position.Candidates
            .Select(stage => ProjectStageMaturityOrder.Resolve(ProjectLifecycleStatus.Active, stage.Code))
            .ToList();
        if (maturities.Count == 0 || maturities.Any(order => order == ProjectStageMaturityOrder.Unknown))
        {
            return null;
        }

        return maturities.All(order => order < ProjectStageMaturityOrder.SupplyOrder) ? true
            : maturities.All(order => order >= ProjectStageMaturityOrder.SupplyOrder) ? false
            : null;
    }
}
