using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Read-only lifecycle projection. Published ARPP completion supersedes local and
/// pending IPA completion state; absent publication, the normal timeline applies.
/// Resolving effective facts never repairs or mutates stored project stages.
/// </summary>
public static class StageEffectiveState
{
    public static StageChronologyState? Resolve(
        string? stageCode,
        StageChronologyState? storedState,
        ArppIpaStageAuthority? ipaAuthority)
        => ipaAuthority is not null &&
           string.Equals(stageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase)
            ? new StageChronologyState(StageStatus.Completed, ipaAuthority.IssueDate)
            : storedState;
}
