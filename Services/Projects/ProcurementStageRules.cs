using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Services.Projects;

public static class ProcurementStageRules
{
    public const string StageForIpaCost = StageCodes.IPA;
    public const string StageForAonCost = StageCodes.AON;
    public const string StageForBenchmarkCost = StageCodes.BM;
    public const string StageForL1Cost = StageCodes.COB;
    public const string StageForPncCost = StageCodes.PNC;
    public const string StageForSupplyOrder = StageCodes.SO;

    // SECTION: When a stage's procurement figure can be recorded
    // A stage's figure can be recorded once the stage has started, or while it has not started
    // but every required predecessor is complete or skipped (it can be completed directly).
    // A skipped stage, or one whose predecessors are still open, stays locked. Completion never
    // waits for the figure; the figure is recorded whenever it is known.
    public static bool CanRecordFact(
        ProjectStageWorkflowSnapshot workflow,
        string stageCode,
        Func<string, StageStatus?> statusOf)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(statusOf);

        return statusOf(stageCode) switch
        {
            StageStatus.InProgress or StageStatus.Blocked or StageStatus.Completed => true,
            StageStatus.NotStarted => workflow
                .RequiredPredecessorClosure(stageCode)
                .All(code => statusOf(code) is StageStatus.Completed or StageStatus.Skipped),
            _ => false
        };
    }

    public static string StageName(string stageCode) => stageCode switch
    {
        StageForIpaCost => "In-Principle Approval",
        StageForAonCost => "Acceptance of Necessity",
        StageForBenchmarkCost => "Benchmarking",
        StageForL1Cost => "Commercial Bid Opening",
        StageForPncCost => "PNC",
        StageForSupplyOrder => "Supply Order",
        _ => stageCode
    };

    public static string LockedNote(string stageCode)
        => $"Unlocks when {StageName(stageCode)} has started.";
}
