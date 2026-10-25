using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Analytics
{
    /// <summary>
    /// Arithmetic and stage ordering shared by the analytics services, so every mode takes a median
    /// and orders stages the same way: workflow order first, then ad hoc stages in first-seen order.
    /// </summary>
    public static class AnalyticsAggregation
    {
        /// <summary>The median of the values (0 when there are none); the input need not be sorted.</summary>
        public static double CalculateMedian(IEnumerable<double> values)
        {
            if (values is null)
            {
                return 0;
            }

            var ordered = values.OrderBy(value => value).ToList();
            if (ordered.Count == 0)
            {
                return 0;
            }

            var midpoint = ordered.Count / 2;
            return ordered.Count % 2 == 1
                ? ordered[midpoint]
                : (ordered[midpoint - 1] + ordered[midpoint]) / 2.0;
        }

        /// <summary>The distinct stage codes present, workflow stages first in workflow order, then any others.</summary>
        public static IReadOnlyList<string> BuildOrderedStageCodes(IEnumerable<string> stageCodes)
        {
            var present = stageCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return StageCodes.All
                .Where(code => present.Contains(code, StringComparer.OrdinalIgnoreCase))
                .Concat(present.Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyDictionary<string, int> BuildStageOrderLookup(IReadOnlyList<string> orderedStageCodes)
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < orderedStageCodes.Count; index++)
            {
                lookup[orderedStageCodes[index]] = index;
            }

            return lookup;
        }

        /// <summary>The position of a stage in the lookup; unknown stages sort last.</summary>
        public static int ResolveStageOrder(string stageCode, IReadOnlyDictionary<string, int> stageOrderLookup) =>
            stageOrderLookup.TryGetValue(stageCode, out var order) ? order : int.MaxValue;
    }
}
