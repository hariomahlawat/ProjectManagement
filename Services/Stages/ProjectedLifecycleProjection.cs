using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// The projected lifecycle state of a project's stages: official facts with the
/// latest pending lifecycle request per stage applied. Shared by lifecycle request
/// submission and the Actual-correction proposal contradiction check (Phase D F.6),
/// so both read a pending request the same way. Moved unchanged from
/// <see cref="StageRequestService"/>.
/// </summary>
internal static class ProjectedLifecycleProjection
{
    internal static void ApplyPublishedAuthority(
        IReadOnlyDictionary<string, ProjectedStageState> states,
        ArppIpaStageAuthority? authority)
    {
        if (authority is null || !states.TryGetValue(StageCodes.IPA, out var ipa))
        {
            return;
        }

        var effective = StageEffectiveState.Resolve(ipa.StageCode,
            new StageChronologyState(ipa.ProjectedStatus, ipa.ProjectedCompletionDate), authority)!;
        ipa.ProjectedStatus = effective.Status;
        ipa.ProjectedCompletionDate = effective.CompletionDate;
    }

    internal static Dictionary<string, ProjectedStageState> BuildProjectedStates(
        IReadOnlyCollection<ProjectStage> stages,
        IReadOnlyCollection<StageChangeRequest> pendingRequests,
        IReadOnlyDictionary<string, DateOnly?> carriedStartByStage)
    {
        var states = stages.ToDictionary(
            stage => stage.StageCode,
            stage => new ProjectedStageState(
                stage.StageCode,
                stage.Status,
                stage.Status,
                stage.ActualStart,
                stage.CompletedOn,
                stage.ActualStart,
                stage.CompletedOn),
            StringComparer.OrdinalIgnoreCase);

        foreach (var state in states.Values)
        {
            if (!state.ProjectedStartDate.HasValue
                && carriedStartByStage.TryGetValue(state.StageCode, out var carriedStart)
                && carriedStart.HasValue)
            {
                state.ProjectedStartDate = carriedStart;
            }
        }

        foreach (var pending in pendingRequests)
        {
            if (!states.TryGetValue(pending.StageCode, out var state)
                || !Enum.TryParse<StageStatus>(pending.RequestedStatus, ignoreCase: true, out var status))
            {
                continue;
            }

            ApplyProjection(state, status, pending.RequestedDate, pending.RequestedStartDate);
        }

        return states;
    }

    internal static void ResetToOfficialProjection(ProjectedStageState state)
    {
        state.ProjectedStatus = state.OfficialStatus;
        state.ProjectedStartDate = state.OfficialActualStart;
        state.ProjectedCompletionDate = state.OfficialCompletionDate;
        state.ProjectedDate = null;
    }

    internal static void ApplyProjection(
        ProjectedStageState state,
        StageStatus requestedStatus,
        DateOnly? requestedDate,
        DateOnly? requestedStartDate)
    {
        state.ProjectedStatus = requestedStatus;
        state.ProjectedDate = requestedDate;

        switch (requestedStatus)
        {
            case StageStatus.Completed:
                if (requestedStartDate.HasValue)
                {
                    state.ProjectedStartDate = requestedStartDate.Value;
                }
                state.ProjectedCompletionDate = requestedDate ?? state.OfficialCompletionDate;
                break;
            case StageStatus.InProgress:
                state.ProjectedStartDate = requestedDate ?? state.OfficialActualStart;
                state.ProjectedCompletionDate = null;
                break;
            case StageStatus.Blocked:
                state.ProjectedCompletionDate = null;
                break;
            case StageStatus.NotStarted:
                state.ProjectedStartDate = null;
                state.ProjectedCompletionDate = null;
                break;
            case StageStatus.Skipped:
                state.ProjectedCompletionDate = null;
                break;
        }
    }
}

internal sealed class ProjectedStageState
{
    public ProjectedStageState(
        string stageCode,
        StageStatus officialStatus,
        StageStatus projectedStatus,
        DateOnly? officialActualStart,
        DateOnly? officialCompletionDate,
        DateOnly? projectedStartDate,
        DateOnly? projectedCompletionDate)
    {
        StageCode = stageCode;
        OfficialStatus = officialStatus;
        ProjectedStatus = projectedStatus;
        OfficialActualStart = officialActualStart;
        OfficialCompletionDate = officialCompletionDate;
        ProjectedStartDate = projectedStartDate;
        ProjectedCompletionDate = projectedCompletionDate;
    }

    public string StageCode { get; }
    public StageStatus OfficialStatus { get; }
    public StageStatus ProjectedStatus { get; set; }
    public DateOnly? OfficialActualStart { get; }
    public DateOnly? OfficialCompletionDate { get; }
    public DateOnly? ProjectedStartDate { get; set; }
    public DateOnly? ProjectedCompletionDate { get; set; }
    public DateOnly? ProjectedDate { get; set; }
}
