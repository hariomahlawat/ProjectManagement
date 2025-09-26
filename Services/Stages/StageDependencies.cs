using System;
using System.Collections.Generic;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services;

public static class StageDependencies
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Required =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.IPA] = new[] { StageCodes.FS },
            [StageCodes.SOW] = new[] { StageCodes.IPA },
            [StageCodes.AON] = new[] { StageCodes.SOW },
            [StageCodes.BM] = new[] { StageCodes.AON },
            [StageCodes.COB] = new[] { StageCodes.BM },
            [StageCodes.PNC] = new[] { StageCodes.COB },
            [StageCodes.SO] = new[] { StageCodes.PNC },
            [StageCodes.DEVP] = new[] { StageCodes.SO },
            [StageCodes.ATP] = new[] { StageCodes.DEVP },
            [StageCodes.PAYMENT] = new[] { StageCodes.ATP }
        };

    public static IReadOnlyList<string> RequiredPredecessors(string stageCode)
        => Required.TryGetValue(stageCode, out var deps) ? deps : Array.Empty<string>();
}
