using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Services;

public class StageRulesService
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;

    public StageRulesService(
        ApplicationDbContext db,
        IProjectStageWorkflowPolicy workflowPolicy,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db);
    }

    public async Task<StageRulesContext> GetContextAsync(int projectId, CancellationToken cancellationToken)
    {
        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(ps => ps.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        return await BuildContextAsync(projectId, stages, cancellationToken);
    }

    public async Task<StageRulesContext> BuildContextAsync(int projectId, IEnumerable<ProjectStage> stages, CancellationToken cancellationToken)
    {
        if (stages == null)
        {
            throw new ArgumentNullException(nameof(stages));
        }

        var ipaAuthority = await _arppIpaStageAuthority.ResolveAsync(projectId, cancellationToken);
        var stageSnapshots = stages
            .Select(ps =>
            {
                var effective = StageEffectiveState.Resolve(
                    ps.StageCode,
                    new StageChronologyState(ps.Status, ps.CompletedOn),
                    ipaAuthority)!;

                return new StageSnapshot(
                    ps.StageCode,
                    effective.Status,
                    ps.ActualStart,
                    effective.CompletionDate);
            })
            .ToList();

        // SECTION: Workflow resolution
        var workflow = await _workflowPolicy.GetAsync(projectId, cancellationToken);
        var dependencies = workflow.Dependencies.ToDictionary(
            item => item.Key,
            item => workflow.RequiredPredecessors(item.Key),
            StringComparer.OrdinalIgnoreCase);

        return new StageRulesContext(stageSnapshots, dependencies);
    }

    public StageGuardResult CanStart(StageRulesContext context, string stageCode)
    {
        if (!context.TryGetStage(stageCode, out var stage))
        {
            return StageGuardResult.Deny($"Stage {stageCode} is not configured for this project.");
        }

        if (stage.Status == StageStatus.Completed)
        {
            return StageGuardResult.Deny($"Stage {stage.Code} is already completed.");
        }

        if (stage.Status == StageStatus.InProgress)
        {
            return StageGuardResult.Deny($"Stage {stage.Code} has already started.");
        }

        if (stage.Status == StageStatus.Skipped)
        {
            return StageGuardResult.Deny($"Stage {stage.Code} was skipped.");
        }

        return CheckDependencies(context, stage, "starting");
    }

    public StageGuardResult CanComplete(StageRulesContext context, string stageCode)
    {
        if (!context.TryGetStage(stageCode, out var stage))
        {
            return StageGuardResult.Deny($"Stage {stageCode} is not configured for this project.");
        }

        if (stage.Status == StageStatus.Completed)
        {
            return StageGuardResult.Deny($"Stage {stage.Code} is already completed.");
        }

        if (stage.Status == StageStatus.Skipped)
        {
            return StageGuardResult.Deny($"Stage {stage.Code} was skipped.");
        }

        if (stage.Status != StageStatus.InProgress)
        {
            return StageGuardResult.Deny($"Start {stage.Code} before completing it.");
        }

        return CheckDependencies(context, stage, "completing");
    }

    public StageGuardResult CanSkip(StageRulesContext context, string stageCode)
    {
        if (!context.TryGetStage(stageCode, out var stage))
        {
            return StageGuardResult.Deny($"Stage {stageCode} is not configured for this project.");
        }

        if (stage.Status == StageStatus.Completed)
        {
            return StageGuardResult.Deny($"Stage {stage.Code} is already completed.");
        }

        if (stage.Status == StageStatus.Skipped)
        {
            return StageGuardResult.Deny($"Stage {stage.Code} has already been skipped.");
        }

        return CheckDependencies(context, stage, "skipping");
    }

    private static StageGuardResult CheckDependencies(StageRulesContext context, StageSnapshot stage, string operation)
    {
        foreach (var dependencyCode in context.GetDependencies(stage.Code))
        {
            if (!context.TryGetStage(dependencyCode, out var dependency))
            {
                return StageGuardResult.Deny($"Dependency {dependencyCode} is missing for stage {stage.Code}.");
            }
            if (dependency.Status is not StageStatus.Completed and not StageStatus.Skipped)
            {
                return StageGuardResult.Deny($"Complete or skip {dependency.Code} before {operation} {stage.Code}.");
            }
        }
        return StageGuardResult.Allow();
    }

}

public sealed class StageRulesContext
{
    private readonly Dictionary<string, StageSnapshot> _stages;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _dependencies;

    public StageRulesContext(IEnumerable<StageSnapshot> stages, IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
    {
        _stages = stages.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
        _dependencies = dependencies;
    }

    public bool TryGetStage(string stageCode, out StageSnapshot stage)
        => _stages.TryGetValue(stageCode, out stage!);

    public IReadOnlyList<string> GetDependencies(string stageCode)
        => _dependencies.TryGetValue(stageCode, out var deps) ? deps : Array.Empty<string>();
}

public record StageSnapshot(string Code, StageStatus Status, DateOnly? ActualStart, DateOnly? CompletedOn);

public record StageGuardResult(bool Allowed, string? Reason)
{
    public static StageGuardResult Allow() => new(true, null);
    public static StageGuardResult Deny(string reason) => new(false, reason);
}
