using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// The start a stage is measured from when its own start was never recorded. Stages run
/// back to back, so a stage that has begun started the day after its required predecessors
/// finished (<see cref="StageDateSuggestionResolver"/>): any wait between stages is the
/// stage's own delay. A completed stage never starts after its own completion, so a
/// same-day handoff counts as one day. Inference is read-only and never stored.
/// </summary>
public static class StageStartInference
{
    /// <summary>
    /// The inferred start, or null when the stage has not begun, its predecessors give no
    /// date, or it is an IPA whose completion comes from a published ARPP record (that
    /// stage's start stays a recorded fact only).
    /// </summary>
    public static DateOnly? Infer(
        string? stageCode,
        StageStatus status,
        DateOnly? completedOn,
        StageDateSuggestion suggestion,
        ArppIpaStageAuthority? ipaAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        if (ipaAuthority is not null
            && string.Equals(stageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (suggestion.SuggestedStartDate is not DateOnly suggested)
        {
            return null;
        }

        return status switch
        {
            StageStatus.Completed when completedOn is DateOnly end => suggested > end ? end : suggested,
            StageStatus.InProgress => suggested,
            _ => null
        };
    }
}

/// <summary>One stored stage, as the start inference reads it.</summary>
public sealed record StageStartFact(
    string StageCode,
    StageStatus Status,
    DateOnly? ActualStart,
    DateOnly? CompletedOn);

/// <summary>The start a stage is measured from: recorded, or inferred by <see cref="StageStartInference"/>.</summary>
public readonly record struct EffectiveStageStart(DateOnly Date, bool IsInferred);

/// <summary>
/// Applies <see cref="StageStartInference"/> across many projects at once, for analytics
/// that span the portfolio. Each project is read against its own workflow graph and ARPP
/// IPA authority.
/// </summary>
public interface IStageStartInferenceService
{
    /// <summary>
    /// The effective start of every stage that has one, keyed by project and then by stage
    /// code. A recorded start always wins; a stage with neither is left out.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, EffectiveStageStart>>> ResolveAsync(
        IReadOnlyDictionary<int, IReadOnlyList<StageStartFact>> stagesByProject,
        CancellationToken cancellationToken = default);
}

public sealed class StageStartInferenceService : IStageStartInferenceService
{
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IArppIpaStageAuthorityService _ipaAuthority;

    public StageStartInferenceService(
        IProjectStageWorkflowPolicy workflowPolicy,
        IArppIpaStageAuthorityService ipaAuthority)
    {
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _ipaAuthority = ipaAuthority ?? throw new ArgumentNullException(nameof(ipaAuthority));
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, EffectiveStageStart>>> ResolveAsync(
        IReadOnlyDictionary<int, IReadOnlyList<StageStartFact>> stagesByProject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stagesByProject);

        // Only a project with a begun stage that lacks a recorded start needs its workflow read.
        var needsInference = stagesByProject
            .Where(pair => pair.Value.Any(stage => !stage.ActualStart.HasValue
                && stage.Status is StageStatus.Completed or StageStatus.InProgress))
            .Select(pair => pair.Key)
            .ToArray();

        var workflows = needsInference.Length == 0
            ? new Dictionary<int, ProjectStageWorkflowSnapshot>()
            : await _workflowPolicy.GetManyAsync(needsInference, cancellationToken);
        var authorities = needsInference.Length == 0
            ? new Dictionary<int, ArppIpaStageAuthority>()
            : await _ipaAuthority.ResolveManyAsync(needsInference, cancellationToken);

        var result = new Dictionary<int, IReadOnlyDictionary<string, EffectiveStageStart>>();
        foreach (var (projectId, stages) in stagesByProject)
        {
            workflows.TryGetValue(projectId, out var workflow);
            authorities.TryGetValue(projectId, out var authority);

            var stateLookup = stages
                .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
                .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => new StageChronologyState(group.First().Status, group.First().CompletedOn),
                    StringComparer.OrdinalIgnoreCase);

            var starts = new Dictionary<string, EffectiveStageStart>(StringComparer.OrdinalIgnoreCase);
            foreach (var stage in stages)
            {
                if (string.IsNullOrWhiteSpace(stage.StageCode) || starts.ContainsKey(stage.StageCode))
                {
                    continue;
                }

                if (stage.ActualStart is DateOnly recorded)
                {
                    starts[stage.StageCode] = new EffectiveStageStart(recorded, IsInferred: false);
                    continue;
                }

                if (workflow is null)
                {
                    continue;
                }

                var suggestion = StageDateSuggestionResolver.Resolve(
                    workflow,
                    stage.StageCode,
                    code => stateLookup.TryGetValue(code, out var state) ? state : null,
                    authority);

                if (StageStartInference.Infer(stage.StageCode, stage.Status, stage.CompletedOn, suggestion, authority) is DateOnly inferred)
                {
                    starts[stage.StageCode] = new EffectiveStageStart(inferred, IsInferred: true);
                }
            }

            result[projectId] = starts;
        }

        return result;
    }
}
