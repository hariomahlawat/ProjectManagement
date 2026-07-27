using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Arpp;

/// <summary>
/// Applies the lifecycle consequence of a published ARPP position:
/// the IPA stage is completed on the earliest HQ-issued document date in which
/// the linked project appears. Later addenda may change the authoritative IPA
/// amount, while the stage date continues to be recalculated from the earliest
/// published appearance of the project.
/// </summary>
public sealed class ArppIpaStageSynchronizer : IArppIpaStageSynchronizer
{
    private readonly ApplicationDbContext _db;

    public ArppIpaStageSynchronizer(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<ArppIpaStageSynchronizationResult> SynchronizeAllAsync(
        CancellationToken cancellationToken = default)
    {
        var projectIds = await _db.ArppPublishedEntries
            .AsNoTracking()
            .Where(entry => entry.ProjectId.HasValue)
            .Select(entry => entry.ProjectId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return await SynchronizeProjectsAsync(projectIds, cancellationToken);
    }

    public async Task<ArppIpaStageSynchronizationResult> SynchronizeProjectsAsync(
        IEnumerable<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);

        var requestedIds = projectIds
            .Where(projectId => projectId > 0)
            .Distinct()
            .ToArray();

        if (requestedIds.Length == 0)
        {
            return ArppIpaStageSynchronizationResult.Empty;
        }

        // Materialize the compact authority projection and calculate the minimum in
        // memory. This keeps DateOnly aggregation provider-neutral for PostgreSQL and
        // the EF Core in-memory provider used by the regression tests.
        var publishedPositions = await _db.ArppPublishedEntries
            .AsNoTracking()
            .Where(entry =>
                entry.ProjectId.HasValue &&
                requestedIds.Contains(entry.ProjectId.Value))
            .Select(entry => new
            {
                ProjectId = entry.ProjectId!.Value,
                entry.PublishedIssue.IssueDate
            })
            .ToListAsync(cancellationToken);

        var earliestIssueDateByProject = publishedPositions
            .GroupBy(position => position.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group.Min(position => position.IssueDate));

        if (earliestIssueDateByProject.Count == 0)
        {
            return new ArppIpaStageSynchronizationResult(requestedIds.Length, []);
        }

        var authoritativeProjectIds = earliestIssueDateByProject.Keys.ToArray();
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project =>
                authoritativeProjectIds.Contains(project.Id) &&
                !project.IsDeleted)
            .Select(project => new
            {
                project.Id,
                project.WorkflowVersion
            })
            .ToListAsync(cancellationToken);

        var existingStages = await _db.ProjectStages
            .Where(stage =>
                authoritativeProjectIds.Contains(stage.ProjectId) &&
                stage.StageCode == StageCodes.IPA)
            .ToDictionaryAsync(stage => stage.ProjectId, cancellationToken);

        var changes = new List<ArppIpaStageSynchronizationChange>();

        foreach (var project in projects)
        {
            var completionDate = earliestIssueDateByProject[project.Id];
            var stageCreated = !existingStages.TryGetValue(project.Id, out var stage);

            if (stageCreated)
            {
                stage = new ProjectStage
                {
                    ProjectId = project.Id,
                    StageCode = StageCodes.IPA,
                    SortOrder = ProcurementWorkflow.OrderOf(project.WorkflowVersion, StageCodes.IPA),
                    Status = StageStatus.NotStarted
                };
                _db.ProjectStages.Add(stage);
                existingStages[project.Id] = stage;
            }

            var previousStatus = stage!.Status;
            var previousCompletedOn = stage.CompletedOn;
            var previousActualStart = stage.ActualStart;
            var previousIsAutoCompleted = stage.IsAutoCompleted;
            var previousAutoCompletedFromCode = stage.AutoCompletedFromCode;
            var previousRequiresBackfill = stage.RequiresBackfill;

            // A completed stage cannot start after it completed. Preserve any genuine
            // earlier start; otherwise use the authoritative issue date for both fields.
            var actualStart = stage.ActualStart.HasValue && stage.ActualStart.Value <= completionDate
                ? stage.ActualStart.Value
                : completionDate;

            stage.Status = StageStatus.Completed;
            stage.CompletedOn = completionDate;
            stage.ActualStart = actualStart;
            stage.IsAutoCompleted = false;
            stage.AutoCompletedFromCode = null;
            stage.RequiresBackfill = false;

            var changed = stageCreated ||
                          previousStatus != stage.Status ||
                          previousCompletedOn != stage.CompletedOn ||
                          previousActualStart != stage.ActualStart ||
                          previousIsAutoCompleted != stage.IsAutoCompleted ||
                          !string.Equals(
                              previousAutoCompletedFromCode,
                              stage.AutoCompletedFromCode,
                              StringComparison.OrdinalIgnoreCase) ||
                          previousRequiresBackfill != stage.RequiresBackfill;

            if (!changed)
            {
                continue;
            }

            changes.Add(new ArppIpaStageSynchronizationChange(
                project.Id,
                completionDate,
                previousStatus,
                previousCompletedOn,
                previousActualStart,
                stageCreated));
        }

        if (changes.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new ArppIpaStageSynchronizationResult(requestedIds.Length, changes);
    }
}

public sealed record ArppIpaStageSynchronizationResult(
    int EvaluatedProjectCount,
    IReadOnlyList<ArppIpaStageSynchronizationChange> Changes)
{
    public static ArppIpaStageSynchronizationResult Empty { get; } = new(0, []);

    public int ChangedProjectCount => Changes.Count;
}

public sealed record ArppIpaStageSynchronizationChange(
    int ProjectId,
    DateOnly CompletionDate,
    StageStatus PreviousStatus,
    DateOnly? PreviousCompletedOn,
    DateOnly? PreviousActualStart,
    bool StageCreated);
