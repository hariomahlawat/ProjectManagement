using System;
using System.Collections.Generic;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Helpers
{
    public static class ProjectOverviewHelpers
    {
        public static string BuildCategoryPath(ProjectCategory category, IReadOnlyDictionary<int, ProjectCategory> categories)
        {
            ArgumentNullException.ThrowIfNull(category);
            ArgumentNullException.ThrowIfNull(categories);

            var segments = new List<string>();
            var current = category;
            var visited = new HashSet<int>();

            while (current is not null)
            {
                segments.Add(current.Name);

                if (current.ParentId is int parentId && categories.TryGetValue(parentId, out var parent))
                {
                    if (!visited.Add(parentId))
                    {
                        break;
                    }

                    current = parent;
                }
                else
                {
                    current = null;
                }
            }

            segments.Reverse();
            return string.Join(" \u203A ", segments);
        }

        public static StageDurationInfo CalculateStageDurations(ProjectStage stage, DateOnly? actualThroughDate = null)
        {
            ArgumentNullException.ThrowIfNull(stage);

            static int? CalculateDays(DateOnly? start, DateOnly? end)
            {
                if (!start.HasValue || !end.HasValue)
                {
                    return null;
                }

                if (end.Value < start.Value)
                {
                    return 0;
                }

                return end.Value.DayNumber - start.Value.DayNumber + 1;
            }

            var planned = CalculateDays(stage.PlannedStart, stage.PlannedDue);
            var actualEnd = stage.CompletedOn ?? actualThroughDate;
            var actual = CalculateDays(stage.ActualStart, actualEnd);

            int? slip = null;
            if (actual.HasValue && planned.HasValue)
            {
                slip = actual.Value - planned.Value;
            }

            return new StageDurationInfo(planned, actual, slip);
        }

        public readonly record struct StageDurationInfo(int? PlannedDays, int? ActualDays, int? SlipDays);
    }
}
