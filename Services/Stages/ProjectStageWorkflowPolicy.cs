using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Resolves the authoritative stage sequence, dependencies and optional-stage
/// rules for a project from its workflow version. The service is scoped so a
/// project snapshot is loaded once per request and reused by all stage services.
/// </summary>
public interface IProjectStageWorkflowPolicy
{
    Task<ProjectStageWorkflowSnapshot> GetAsync(int projectId, CancellationToken cancellationToken = default);
}

public sealed class ProjectStageWorkflowPolicy : IProjectStageWorkflowPolicy
{
    private readonly ApplicationDbContext _db;
    private readonly IWorkflowStageMetadataProvider _metadataProvider;
    private readonly Dictionary<int, ProjectStageWorkflowSnapshot> _cache = new();

    public ProjectStageWorkflowPolicy(
        ApplicationDbContext db,
        IWorkflowStageMetadataProvider metadataProvider)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
    }

    public async Task<ProjectStageWorkflowSnapshot> GetAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(projectId, out var cached))
        {
            return cached;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Where(item => item.Id == projectId)
            .Select(item => new
            {
                item.WorkflowVersion,
                item.ActivePlanVersionNo
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Project {projectId} was not found.");

        var workflowVersion = string.IsNullOrWhiteSpace(project.WorkflowVersion)
            ? PlanConstants.DefaultStageTemplateVersion
            : project.WorkflowVersion;

        var stageDefinitions = _metadataProvider
            .GetStages(workflowVersion)
            .ToArray();

        var configuredDependencies = await _db.StageDependencyTemplates
            .AsNoTracking()
            .Where(item => item.Version == workflowVersion)
            .Select(item => new { item.FromStageCode, item.DependsOnStageCode })
            .ToListAsync(cancellationToken);

        var dependencies = configuredDependencies.Count > 0
            ? configuredDependencies
                .GroupBy(item => item.FromStageCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group
                        .Select(item => item.DependsOnStageCode)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase)
            : BuildFallbackDependencies(workflowVersion);

        var configuredOptionalStages = await _db.StageTemplates
            .AsNoTracking()
            .Where(item => item.Version == workflowVersion && item.Optional)
            .Select(item => item.Code)
            .ToListAsync(cancellationToken);

        var optionalStageCodes = configuredOptionalStages.Count > 0
            ? configuredOptionalStages.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : BuildFallbackOptionalStages();

        var pncApplicable = true;
        if (project.ActivePlanVersionNo.HasValue)
        {
            pncApplicable = await _db.PlanVersions
                .AsNoTracking()
                .Where(item => item.ProjectId == projectId && item.VersionNo == project.ActivePlanVersionNo.Value)
                .Select(item => (bool?)item.PncApplicable)
                .SingleOrDefaultAsync(cancellationToken)
                ?? true;
        }

        var snapshot = new ProjectStageWorkflowSnapshot(
            workflowVersion,
            stageDefinitions,
            dependencies,
            optionalStageCodes,
            pncApplicable);

        _cache[projectId] = snapshot;
        return snapshot;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildFallbackDependencies(string workflowVersion)
    {
        var dependencies = string.Equals(workflowVersion, ProcurementWorkflow.VersionV1, StringComparison.OrdinalIgnoreCase)
            ? new (string Stage, string Predecessor)[]
            {
                (StageCodes.IPA, StageCodes.FS),
                (StageCodes.SOW, StageCodes.IPA),
                (StageCodes.AON, StageCodes.SOW),
                (StageCodes.BID, StageCodes.AON),
                (StageCodes.TEC, StageCodes.BID),
                (StageCodes.BM, StageCodes.BID),
                (StageCodes.COB, StageCodes.TEC),
                (StageCodes.COB, StageCodes.BM),
                (StageCodes.PNC, StageCodes.COB),
                (StageCodes.EAS, StageCodes.COB),
                (StageCodes.EAS, StageCodes.PNC),
                (StageCodes.SO, StageCodes.EAS),
                (StageCodes.DEVP, StageCodes.SO),
                (StageCodes.ATP, StageCodes.DEVP),
                (StageCodes.PAYMENT, StageCodes.ATP),
                (StageCodes.TOT, StageCodes.PAYMENT)
            }
            : new (string Stage, string Predecessor)[]
            {
                (StageCodes.SOW, StageCodes.FS),
                (StageCodes.IPA, StageCodes.SOW),
                (StageCodes.AON, StageCodes.IPA),
                (StageCodes.BID, StageCodes.AON),
                (StageCodes.TEC, StageCodes.BID),
                (StageCodes.BM, StageCodes.TEC),
                (StageCodes.COB, StageCodes.BM),
                (StageCodes.PNC, StageCodes.COB),
                (StageCodes.EAS, StageCodes.COB),
                (StageCodes.EAS, StageCodes.PNC),
                (StageCodes.SO, StageCodes.EAS),
                (StageCodes.DEVP, StageCodes.SO),
                (StageCodes.ATP, StageCodes.DEVP),
                (StageCodes.PAYMENT, StageCodes.ATP),
                (StageCodes.TOT, StageCodes.PAYMENT)
            };

        return dependencies
            .GroupBy(item => item.Stage, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(item => item.Predecessor)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildFallbackOptionalStages() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            StageCodes.PNC,
            StageCodes.TOT
        };
}

public sealed class ProjectStageWorkflowSnapshot
{
    private readonly IReadOnlyDictionary<string, int> _orderLookup;

    public ProjectStageWorkflowSnapshot(
        string workflowVersion,
        IReadOnlyList<WorkflowStageDefinition> stages,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
        IReadOnlySet<string> optionalStageCodes,
        bool pncApplicable)
    {
        WorkflowVersion = workflowVersion;
        Stages = stages;
        Dependencies = dependencies;
        OptionalStageCodes = optionalStageCodes;
        PncApplicable = pncApplicable;
        _orderLookup = stages
            .Select((stage, index) => new { stage.Code, Index = index })
            .ToDictionary(item => item.Code, item => item.Index, StringComparer.OrdinalIgnoreCase);
    }

    public string WorkflowVersion { get; }

    public IReadOnlyList<WorkflowStageDefinition> Stages { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; }

    public IReadOnlySet<string> OptionalStageCodes { get; }

    public bool PncApplicable { get; }

    public bool ContainsStage(string? stageCode) =>
        !string.IsNullOrWhiteSpace(stageCode) && _orderLookup.ContainsKey(stageCode);

    public int OrderOf(string? stageCode) =>
        !string.IsNullOrWhiteSpace(stageCode) && _orderLookup.TryGetValue(stageCode, out var order)
            ? order
            : int.MaxValue;

    public IReadOnlyList<string> RequiredPredecessors(string? stageCode)
    {
        if (string.IsNullOrWhiteSpace(stageCode)
            || !Dependencies.TryGetValue(stageCode, out var predecessors))
        {
            return Array.Empty<string>();
        }

        if (PncApplicable)
        {
            return predecessors;
        }

        return predecessors
            .Where(code => !string.Equals(code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Returns the transitive predecessor set in workflow order. This is used
    /// when validating or force-completing a later stage so every unresolved
    /// ancestor is surfaced, not only the immediate dependency.
    /// </summary>
    public IReadOnlyList<string> RequiredPredecessorClosure(string? stageCode)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return Array.Empty<string>();
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string code)
        {
            foreach (var predecessor in RequiredPredecessors(code))
            {
                if (!visited.Add(predecessor))
                {
                    continue;
                }

                Visit(predecessor);
            }
        }

        Visit(stageCode);
        return visited
            .OrderBy(OrderOf)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
