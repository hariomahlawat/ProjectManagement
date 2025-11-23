using System;
using System.Collections.Generic;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Models.Stages;

public static class ProcurementWorkflow
{
    // SECTION: Workflow Versions
    public const string VersionV1 = PlanConstants.StageTemplateVersionV1;
    public const string VersionV2 = PlanConstants.StageTemplateVersionV2;

    // SECTION: Stage Sequences
    public static readonly string[] V1Stages =
    {
        StageCodes.FS,
        StageCodes.IPA,
        StageCodes.SOW,
        StageCodes.AON,
        StageCodes.BID,
        StageCodes.TEC,
        StageCodes.BM,
        StageCodes.COB,
        StageCodes.PNC,
        StageCodes.EAS,
        StageCodes.SO,
        StageCodes.DEVP,
        StageCodes.ATP,
        StageCodes.PAYMENT,
        StageCodes.TOT
    };

    public static readonly string[] V2Stages =
    {
        StageCodes.FS,
        StageCodes.SOW,
        StageCodes.IPA,
        StageCodes.AON,
        StageCodes.BID,
        StageCodes.TEC,
        StageCodes.BM,
        StageCodes.COB,
        StageCodes.PNC,
        StageCodes.EAS,
        StageCodes.SO,
        StageCodes.DEVP,
        StageCodes.ATP,
        StageCodes.PAYMENT,
        StageCodes.TOT
    };

    // SECTION: Helpers
    public static string[] StageCodesFor(string? workflowVersion) =>
        string.Equals(workflowVersion, VersionV1, StringComparison.OrdinalIgnoreCase)
            ? V1Stages
            : V2Stages;

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
        var codes = StageCodesFor(workflowVersion);
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < codes.Length; i++)
        {
            lookup[codes[i]] = i;
        }

        return lookup;
    }
}
