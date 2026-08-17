using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

public sealed record ProjectFormalUpdateFacts(
    DateOnly? AonDate,
    DateOnly? SupplyOrderDate,
    DateOnly? DevelopmentPdcDate);

public interface IProjectFormalUpdateFactsResolver
{
    Task<IReadOnlyDictionary<int, ProjectFormalUpdateFacts>> ResolveAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves procurement facts that are reused by formal reports and briefing
/// update sheets. The resolver intentionally returns raw factual values only;
/// each consumer decides whether a value is applicable to its current lifecycle
/// context (for example, a Development PDC is shown only while Development is
/// the current stage). AoN is resolved strictly as a completed-stage milestone:
/// only an AoN row whose Status is Completed may contribute CompletedOn.
/// </summary>
public sealed class ProjectFormalUpdateFactsResolver : IProjectFormalUpdateFactsResolver
{
    private readonly ApplicationDbContext _db;

    public ProjectFormalUpdateFactsResolver(ApplicationDbContext db)
        => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<IReadOnlyDictionary<int, ProjectFormalUpdateFacts>> ResolveAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);

        var ids = projectIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, ProjectFormalUpdateFacts>();
        }

        var stageRows = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => ids.Contains(stage.ProjectId)
                && (stage.StageCode == StageCodes.AON || stage.StageCode == StageCodes.DEVP))
            .Select(stage => new
            {
                stage.Id,
                stage.ProjectId,
                stage.StageCode,
                stage.SortOrder,
                stage.Status,
                stage.CompletedOn,
                stage.PlannedDue
            })
            .ToListAsync(cancellationToken);

        // AoN is a completed-stage milestone, not merely a date-like field.
        // A stale CompletedOn value on an InProgress/Blocked/Skipped AoN row must
        // never surface in formal reports or briefing outputs.
        var aonDates = stageRows
            .Where(stage => stage.StageCode == StageCodes.AON
                && stage.Status == StageStatus.Completed
                && stage.CompletedOn.HasValue)
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(stage => stage.CompletedOn)
                    .ThenByDescending(stage => stage.SortOrder)
                    .ThenByDescending(stage => stage.Id)
                    .Select(stage => stage.CompletedOn)
                    .FirstOrDefault());

        var developmentPdcs = stageRows
            .Where(stage => stage.StageCode == StageCodes.DEVP)
            .GroupBy(stage => stage.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(stage => stage.PlannedDue.HasValue)
                    .ThenByDescending(stage => stage.PlannedDue)
                    .ThenByDescending(stage => stage.SortOrder)
                    .ThenByDescending(stage => stage.Id)
                    .Select(stage => stage.PlannedDue)
                    .FirstOrDefault());

        var supplyOrderRows = await _db.ProjectSupplyOrderFacts
            .AsNoTracking()
            .Where(fact => ids.Contains(fact.ProjectId))
            .Select(fact => new
            {
                fact.Id,
                fact.ProjectId,
                fact.SupplyOrderDate,
                fact.CreatedOnUtc
            })
            .ToListAsync(cancellationToken);

        var supplyOrderDates = supplyOrderRows
            .GroupBy(fact => fact.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(fact => fact.CreatedOnUtc)
                    .ThenByDescending(fact => fact.Id)
                    .Select(fact => (DateOnly?)fact.SupplyOrderDate)
                    .FirstOrDefault());

        return ids.ToDictionary(
            projectId => projectId,
            projectId => new ProjectFormalUpdateFacts(
                aonDates.GetValueOrDefault(projectId),
                supplyOrderDates.GetValueOrDefault(projectId),
                developmentPdcs.GetValueOrDefault(projectId)));
    }
}
