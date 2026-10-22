using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.Projects
{
    public sealed record OngoingCategoryNode(int Id, string Name, int? ParentId);

    /// <summary>
    /// The category filter in effect on the Ongoing projects page: the top-level chips selected,
    /// an optional sub-category that narrows within them, and the ids the query should use.
    /// </summary>
    public sealed record OngoingCategorySelection(
        IReadOnlyList<int> TopLevelIds,
        int? SubCategoryId,
        IReadOnlyList<int> EffectiveIds);

    /// <summary>
    /// Category rules for the Ongoing projects header. Chips are generated for every top-level
    /// category, so categories added in Admin get one too; a sub-category narrows within the
    /// selected chips, the way the exact stage narrows within the selected stage chips.
    /// </summary>
    public static class OngoingCategoryFilter
    {
        public static OngoingCategorySelection Resolve(
            IReadOnlyCollection<OngoingCategoryNode> categories,
            IEnumerable<int> chipIds,
            int? legacyCategoryId,
            int? subCategoryId)
        {
            var byId = categories.ToDictionary(category => category.Id);
            var topLevelIds = categories.Where(category => IsTopLevel(category, byId)).Select(category => category.Id).ToHashSet();

            // A legacy single category is a chip when it is top level, otherwise a sub-category.
            var legacyIsTopLevel = legacyCategoryId is { } legacyId && topLevelIds.Contains(legacyId);
            var chips = chipIds
                .Concat(legacyIsTopLevel ? new[] { legacyCategoryId!.Value } : Array.Empty<int>())
                .Where(topLevelIds.Contains)
                .Distinct()
                .OrderBy(id => byId[id].Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidate = subCategoryId ?? (legacyIsTopLevel ? null : legacyCategoryId);
            int? sub = candidate is { } subId
                && byId.ContainsKey(subId)
                && !topLevelIds.Contains(subId)
                && (chips.Count == 0 || chips.Contains(TopLevelOf(subId, byId)))
                    ? subId
                    : null;

            return new OngoingCategorySelection(chips, sub, sub is { } narrowed ? new[] { narrowed } : chips);
        }

        /// <summary>Sub-categories under the selected chips (all of them when none is selected), labelled with their path.</summary>
        public static IReadOnlyList<(int Id, string Label)> SubCategoryOptions(
            IReadOnlyCollection<OngoingCategoryNode> categories,
            IReadOnlyCollection<int> selectedTopLevelIds)
        {
            var byId = categories.ToDictionary(category => category.Id);
            return categories
                .Where(category => !IsTopLevel(category, byId))
                .Where(category => selectedTopLevelIds.Count == 0 || selectedTopLevelIds.Contains(TopLevelOf(category.Id, byId)))
                .Select(category => (category.Id, Label: PathOf(category.Id, byId)))
                .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Short chip label: "DCD Projects" reads as "DCD".</summary>
        public static string ChipLabel(string name)
        {
            const string suffix = " Projects";
            var trimmed = name.Trim();
            return trimmed.Length > suffix.Length && trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^suffix.Length]
                : trimmed;
        }

        public static int TopLevelOf(int categoryId, IReadOnlyDictionary<int, OngoingCategoryNode> byId)
        {
            var current = byId[categoryId];
            var visited = new HashSet<int> { current.Id };
            while (current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) && visited.Add(parent.Id))
            {
                current = parent;
            }

            return current.Id;
        }

        private static bool IsTopLevel(OngoingCategoryNode category, IReadOnlyDictionary<int, OngoingCategoryNode> byId)
            => category.ParentId is not { } parentId || !byId.ContainsKey(parentId);

        private static string PathOf(int categoryId, IReadOnlyDictionary<int, OngoingCategoryNode> byId)
        {
            var names = new List<string>();
            var visited = new HashSet<int>();
            for (var current = byId[categoryId]; visited.Add(current.Id);)
            {
                names.Insert(0, current.Name);
                if (current.ParentId is not { } parentId || !byId.TryGetValue(parentId, out var parent)) break;
                current = parent;
            }

            return string.Join(" › ", names);
        }
    }
}
