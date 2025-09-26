using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services;

public class StageRulesService
{
    private readonly ApplicationDbContext _db;
    private readonly SemaphoreSlim _dependencyLock = new(1, 1);
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _dependencyCache;

    public StageRulesService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<StageRulesContext> GetContextAsync(int projectId, CancellationToken cancellationToken)
    {
        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(ps => ps.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        return await BuildContextAsync(stages, cancellationToken);
    }

    public async Task<StageRulesContext> BuildContextAsync(IEnumerable<ProjectStage> stages, CancellationToken cancellationToken)
    {
        if (stages == null)
        {
            throw new ArgumentNullException(nameof(stages));
        }

        var stageSnapshots = stages
            .Select(ps => new StageSnapshot(ps.StageCode, ps.Status, ps.ActualStart, ps.CompletedOn))
            .ToList();

        var dependencies = await GetDependencyMapAsync(cancellationToken);
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

        foreach (var dependencyCode in context.GetDependencies(stageCode))
        {
            if (!context.TryGetStage(dependencyCode, out var dependency))
            {
                return StageGuardResult.Deny($"Dependency {dependencyCode} is missing for stage {stage.Code}.");
            }

            if (dependency.Status is not StageStatus.Completed and not StageStatus.Skipped)
            {
                return StageGuardResult.Deny($"Complete or skip {dependency.Code} before starting {stage.Code}.");
            }
        }

        if (string.Equals(stage.Code, StageCodes.EAS, StringComparison.OrdinalIgnoreCase))
        {
            if (context.TryGetStage(StageCodes.PNC, out var pnc) && pnc.Status is not StageStatus.Completed and not StageStatus.Skipped)
            {
                return StageGuardResult.Deny("EAS cannot start until PNC is completed or skipped.");
            }
        }

        return StageGuardResult.Allow();
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

        if (string.Equals(stage.Code, StageCodes.COB, StringComparison.OrdinalIgnoreCase))
        {
            if (!context.TryGetStage(StageCodes.TEC, out var tec) || tec.Status != StageStatus.Completed)
            {
                return StageGuardResult.Deny("COB cannot complete until TEC is completed.");
            }

            if (!context.TryGetStage(StageCodes.BM, out var bench) || bench.Status != StageStatus.Completed)
            {
                return StageGuardResult.Deny("COB cannot complete until Benchmarking (BM) is completed.");
            }
        }

        if (string.Equals(stage.Code, StageCodes.EAS, StringComparison.OrdinalIgnoreCase))
        {
            if (context.TryGetStage(StageCodes.PNC, out var pnc) && pnc.Status is not StageStatus.Completed and not StageStatus.Skipped)
            {
                return StageGuardResult.Deny("EAS cannot complete until PNC is completed or skipped.");
            }
        }

        return StageGuardResult.Allow();
    }

    public StageGuardResult CanSkip(StageRulesContext context, string stageCode)
    {
        if (!string.Equals(stageCode, StageCodes.PNC, StringComparison.OrdinalIgnoreCase))
        {
            return StageGuardResult.Deny("Only PNC can be skipped.");
        }

        if (!context.TryGetStage(stageCode, out var stage))
        {
            return StageGuardResult.Deny("PNC stage is not configured for this project.");
        }

        if (stage.Status == StageStatus.Completed)
        {
            return StageGuardResult.Deny("PNC is already completed.");
        }

        if (stage.Status == StageStatus.Skipped)
        {
            return StageGuardResult.Deny("PNC has already been skipped.");
        }

        return StageGuardResult.Allow();
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetDependencyMapAsync(CancellationToken cancellationToken)
    {
        if (_dependencyCache != null)
        {
            return _dependencyCache;
        }

        await _dependencyLock.WaitAsync(cancellationToken);
        try
        {
            if (_dependencyCache == null)
            {
                var dependencies = await _db.StageDependencyTemplates
                    .AsNoTracking()
                    .Where(d => d.Version == PlanConstants.StageTemplateVersion)
                    .ToListAsync(cancellationToken);

                _dependencyCache = dependencies
                    .GroupBy(d => d.FromStageCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => (IReadOnlyList<string>)g.Select(x => x.DependsOnStageCode).ToList(),
                        StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            _dependencyLock.Release();
        }

        return _dependencyCache;
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
