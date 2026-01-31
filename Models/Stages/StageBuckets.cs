using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Stages;

// SECTION: Stage bucket definitions
public enum StageBucket
{
    Approval,
    Aon,
    Procurement,
    Development,
    Unknown
}

// SECTION: Stage bucket mapping helper
public static class StageBuckets
{
    private static readonly HashSet<string> ApprovalCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        StageCodes.FS,
        StageCodes.SOW,
        StageCodes.IPA
    };

    private static readonly HashSet<string> AonCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        StageCodes.AON
    };

    private static readonly HashSet<string> ProcurementCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        StageCodes.BID,
        StageCodes.TEC,
        StageCodes.BM,
        StageCodes.COB,
        StageCodes.PNC,
        StageCodes.EAS,
        StageCodes.SO
    };

    private static readonly HashSet<string> DevelopmentCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        StageCodes.DEVP,
        StageCodes.ATP,
        StageCodes.PAYMENT,
        StageCodes.TOT
    };

    // SECTION: Bucket resolver
    public static StageBucket Of(string? stageCode)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return StageBucket.Unknown;
        }

        if (ApprovalCodes.Contains(stageCode))
        {
            return StageBucket.Approval;
        }

        if (AonCodes.Contains(stageCode))
        {
            return StageBucket.Aon;
        }

        if (ProcurementCodes.Contains(stageCode))
        {
            return StageBucket.Procurement;
        }

        if (DevelopmentCodes.Contains(stageCode))
        {
            return StageBucket.Development;
        }

        return StageBucket.Unknown;
    }
}
