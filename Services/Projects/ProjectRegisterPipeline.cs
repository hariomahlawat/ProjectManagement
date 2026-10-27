using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

/// <summary>The Active register's quick filters (docs/ux/projects-register.md).</summary>
public enum ProjectRegisterAttention
{
    Late,
    Blocked,
    Stale
}

/// <summary>
/// The Completed tab's record filter: projects recorded in PRISM, or entries imported from the
/// earlier archive (the old Legacy archive view).
/// </summary>
public enum ProjectRegisterRecord
{
    Prism,
    Legacy
}

public static class ProjectRegisterFilters
{
    public static bool Matches(this ProjectRegisterActiveFacts facts, ProjectRegisterAttention attention) => attention switch
    {
        ProjectRegisterAttention.Late => facts.IsLate,
        ProjectRegisterAttention.Blocked => facts.IsBlocked,
        ProjectRegisterAttention.Stale => facts.IsStale,
        _ => true
    };

    /// <summary>The project is working on (or waiting to start) this stage; finished projects are in no stage.</summary>
    public static bool IsCurrentlyIn(this ProjectRegisterActiveFacts facts, string stageCode)
        => facts.CurrentStageState != ProjectRegisterStageState.Finished
           && string.Equals(facts.CurrentStageCode, stageCode, StringComparison.OrdinalIgnoreCase);

    public static string Label(this ProjectRegisterAttention attention) => attention switch
    {
        ProjectRegisterAttention.Late => "Late against plan",
        ProjectRegisterAttention.Blocked => "Blocked",
        ProjectRegisterAttention.Stale => $"Not updated in {ProjectRegisterActiveFacts.StaleAfterDays}+ days",
        _ => attention.ToString()
    };
}

/// <summary>
/// Where the active projects stand: one column per workflow stage with its on-plan, late and
/// blocked counts, and the attention counts. Each control's counts follow the other's selection
/// but not its own, so a selected stage still shows every stage's count.
/// </summary>
public sealed record ProjectRegisterPipeline(
    IReadOnlyList<ProjectRegisterPipelineStage> Stages,
    IReadOnlyList<ProjectRegisterAttentionCount> Attention)
{
    public static ProjectRegisterPipeline Build(
        IReadOnlyCollection<ProjectRegisterActiveFacts> facts,
        IReadOnlyList<WorkflowStageDefinition> stageOrder,
        string? currentStage,
        ProjectRegisterAttention? attention)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(stageOrder);

        var inStages = facts
            .Where(f => f.CurrentStageState != ProjectRegisterStageState.Finished)
            .Where(f => attention is null || f.Matches(attention.Value))
            .ToList();

        // The default workflow's order, then any other stage a project is in.
        var columns = stageOrder.Select(stage => (stage.Code, stage.Name)).ToList();
        foreach (var extra in inStages
                     .Where(f => columns.All(c => !string.Equals(c.Code, f.CurrentStageCode, StringComparison.OrdinalIgnoreCase)))
                     .Select(f => (Code: f.CurrentStageCode, Name: f.CurrentStageName))
                     .DistinctBy(c => c.Code, StringComparer.OrdinalIgnoreCase))
        {
            columns.Add(extra);
        }

        var stages = columns
            .Select(column =>
            {
                var here = inStages.Where(f => f.IsCurrentlyIn(column.Code)).ToList();
                var blocked = here.Count(f => f.IsBlocked);
                var late = here.Count(f => f.IsLate && !f.IsBlocked);
                return new ProjectRegisterPipelineStage(
                    column.Code,
                    column.Name,
                    here.Count,
                    here.Count - blocked - late,
                    late,
                    blocked);
            })
            .ToList();

        var forAttention = facts
            .Where(f => string.IsNullOrWhiteSpace(currentStage) || f.IsCurrentlyIn(currentStage))
            .ToList();

        var counts = Enum.GetValues<ProjectRegisterAttention>()
            .Select(kind => new ProjectRegisterAttentionCount(kind, kind.Label(), forAttention.Count(f => f.Matches(kind))))
            .ToList();

        return new ProjectRegisterPipeline(stages, counts);
    }
}

public sealed record ProjectRegisterPipelineStage(string Code, string Name, int Total, int OnPlan, int Late, int Blocked);

public sealed record ProjectRegisterAttentionCount(ProjectRegisterAttention Attention, string Label, int Count);
