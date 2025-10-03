using System;
using System.Collections.Generic;
using System.Linq;
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
            [StageCodes.BID] = new[] { StageCodes.AON },
            [StageCodes.TEC] = new[] { StageCodes.BID },
            [StageCodes.BM] = new[] { StageCodes.BID },
            [StageCodes.COB] = new[] { StageCodes.TEC, StageCodes.BM },
            [StageCodes.PNC] = new[] { StageCodes.COB },
            [StageCodes.EAS] = new[] { StageCodes.COB, StageCodes.PNC },
            [StageCodes.SO] = new[] { StageCodes.EAS },
            [StageCodes.DEVP] = new[] { StageCodes.SO },
            [StageCodes.ATP] = new[] { StageCodes.DEVP },
            [StageCodes.PAYMENT] = new[] { StageCodes.ATP }
        };

    public static IReadOnlyList<string> RequiredPredecessors(string stageCode)
        => Required.TryGetValue(stageCode, out var deps) ? deps : Array.Empty<string>();

    public static IReadOnlyList<string> RequiredPredecessors(string stageCode, bool pncApplicable)
    {
        var required = RequiredPredecessors(stageCode);

        if (pncApplicable || required.Count == 0)
        {
            return required;
        }

        var filtered = required
            .Where(code => !string.Equals(code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return filtered.Length == required.Count ? required : filtered;
    }
}
