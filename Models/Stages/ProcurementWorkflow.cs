using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Models.Stages;

public static class ProcurementWorkflow
{
    // SECTION: Workflow Versions
    public const string VersionV1 = PlanConstants.StageTemplateVersionV1;
    public const string VersionV2 = PlanConstants.StageTemplateVersionV2;

    // SECTION: Stage Sequences
    public static readonly WorkflowStageDefinition[] V1Stages =
    {
        new(StageCodes.FS, "Feasibility Study"),
        new(StageCodes.IPA, "In-Principle Approval"),
        new(StageCodes.SOW, "SOW Vetting"),
        new(StageCodes.AON, "Acceptance of Necessity"),
        new(StageCodes.BID, "Bidding/ Tendering"),
        new(StageCodes.TEC, "Technical Evaluation"),
        new(StageCodes.BM, "Benchmarking"),
        new(StageCodes.COB, "Commercial Bid Opening"),
        new(StageCodes.PNC, "PNC"),
        new(StageCodes.EAS, "EAS Approval"),
        new(StageCodes.SO, "Supply Order"),
        new(StageCodes.DEVP, "Development"),
        new(StageCodes.ATP, "Acceptance Testing/ Trials"),
        new(StageCodes.PAYMENT, "Payment"),
        new(StageCodes.TOT, "Transfer of Technology")
    };

    public static readonly WorkflowStageDefinition[] V2Stages =
    {
        new(StageCodes.FS, "Feasibility Study"),
        new(StageCodes.SOW, "SOW Vetting"),
        new(StageCodes.IPA, "In-Principle Approval"),
        new(StageCodes.AON, "Acceptance of Necessity"),
        new(StageCodes.BID, "Bidding/ Tendering"),
        new(StageCodes.TEC, "Technical Evaluation"),
        new(StageCodes.BM, "Benchmarking"),
        new(StageCodes.COB, "Commercial Bid Opening"),
        new(StageCodes.PNC, "PNC"),
        new(StageCodes.EAS, "EAS Approval"),
        new(StageCodes.SO, "Supply Order"),
        new(StageCodes.DEVP, "Development"),
        new(StageCodes.ATP, "Acceptance Testing/ Trials"),
        new(StageCodes.PAYMENT, "Payment"),
        new(StageCodes.TOT, "Transfer of Technology")
    };

    // SECTION: Helpers
    public static WorkflowStageDefinition[] StageDefinitionsFor(string? workflowVersion) =>
        string.Equals(workflowVersion, VersionV1, StringComparison.OrdinalIgnoreCase)
            ? V1Stages
            : V2Stages;

    public static string[] StageCodesFor(string? workflowVersion)
        => StageDefinitionsFor(workflowVersion).Select(stage => stage.Code).ToArray();

    public static int OrderOf(string? workflowVersion, string? stageCode)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return int.MaxValue;
        }

        var list = StageCodesFor(workflowVersion);
        var idx = Array.IndexOf(list, stageCode);
        return idx >= 0 ? idx : int.MaxValue;
    }

    public static Dictionary<string, int> BuildOrderLookup(string? workflowVersion)
    {
        var definitions = StageDefinitionsFor(workflowVersion);
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < definitions.Length; i++)
        {
            lookup[definitions[i].Code] = i;
        }

        return lookup;
    }
}
