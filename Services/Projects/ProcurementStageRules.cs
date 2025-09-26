using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

public static class ProcurementStageRules
{
    public const string StageForIpaCost = StageCodes.IPA;
    public const string StageForAonCost = StageCodes.AON;
    public const string StageForBenchmarkCost = StageCodes.BM;
    public const string StageForL1Cost = StageCodes.COB;
    public const string StageForPncCost = StageCodes.PNC;
    public const string StageForSupplyOrder = StageCodes.SO;
}
