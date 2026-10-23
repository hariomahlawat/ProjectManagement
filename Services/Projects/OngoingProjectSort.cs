using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.Projects
{
    /// <summary>
    /// Column sorting for the Ongoing projects table (and its Excel export). Without a sort the
    /// read service's operational order stands. Each column's first click puts the rows that most
    /// need attention on top; a second click reverses it. Rows without a value always go last,
    /// except under Last update, where a project with no update at all counts as the stalest.
    /// </summary>
    public static class OngoingProjectSort
    {
        public const string Name = "name";
        public const string Stage = "stage";
        public const string Age = "age";
        public const string Pdc = "pdc";
        public const string Update = "update";

        public const string Ascending = "asc";
        public const string Descending = "desc";

        private static readonly IReadOnlyDictionary<string, string> FirstDirection = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Name] = Ascending,      // A to Z
            [Stage] = Descending,    // later stages first, as the default order
            [Age] = Descending,      // longest in the current stage first
            [Pdc] = Ascending,       // earliest (most overdue) PDC first
            [Update] = Ascending     // stalest internal update first
        };

        /// <summary>A known sort key, or null for the default order.</summary>
        public static string? NormalizeKey(string? value)
        {
            var key = value?.Trim().ToLowerInvariant();
            return key is not null && FirstDirection.ContainsKey(key) ? key : null;
        }

        /// <summary>The direction to use: the requested one when valid, else the column's first direction.</summary>
        public static string NormalizeDirection(string? key, string? value)
        {
            var normalizedKey = NormalizeKey(key);
            if (normalizedKey is null)
            {
                return Ascending;
            }

            var direction = value?.Trim().ToLowerInvariant();
            return direction is Ascending or Descending ? direction : FirstDirection[normalizedKey];
        }

        /// <summary>The direction a header link should request: the column's first direction, or the reverse when it is already sorted that way.</summary>
        public static string NextDirection(string column, string? currentKey, string? currentDirection)
        {
            var first = FirstDirection[column];
            if (!string.Equals(NormalizeKey(currentKey), column, StringComparison.Ordinal))
            {
                return first;
            }

            return string.Equals(NormalizeDirection(currentKey, currentDirection), first, StringComparison.Ordinal)
                ? (first == Ascending ? Descending : Ascending)
                : first;
        }

        public static IReadOnlyList<OngoingProjectRowDto> Apply(IReadOnlyList<OngoingProjectRowDto> rows, string? key, string? direction)
        {
            ArgumentNullException.ThrowIfNull(rows);

            var normalizedKey = NormalizeKey(key);
            if (normalizedKey is null)
            {
                return rows;
            }

            var descending = NormalizeDirection(normalizedKey, direction) == Descending;
            IOrderedEnumerable<OngoingProjectRowDto> ordered = normalizedKey switch
            {
                Name => By(rows.OrderBy(_ => 0), row => row.ProjectName, descending, StringComparer.OrdinalIgnoreCase),
                Stage => By(rows.OrderBy(_ => 0), row => row.CurrentStageSortOrder, descending),
                Age => By(rows.OrderBy(row => row.DaysInCurrentStage.HasValue ? 0 : 1), row => row.DaysInCurrentStage ?? 0, descending),
                Pdc => By(rows.OrderBy(row => row.PresentStagePdc.HasValue ? 0 : 1), row => row.PresentStagePdc ?? DateOnly.MinValue, descending),
                _ => By(rows.OrderBy(_ => 0), row => LatestInternalUpdate(row) ?? DateTime.MinValue, descending)
            };

            return ordered
                .ThenBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>When the project's latest internal update was posted, if it has one.</summary>
        public static DateTime? LatestInternalUpdate(OngoingProjectRowDto row)
            => row.RecentInternalRemarks.Count == 0
                ? null
                : row.RecentInternalRemarks.Max(remark => remark.CreatedAtUtc);

        private static IOrderedEnumerable<OngoingProjectRowDto> By<TKey>(
            IOrderedEnumerable<OngoingProjectRowDto> rows,
            Func<OngoingProjectRowDto, TKey> key,
            bool descending,
            IComparer<TKey>? comparer = null)
            => descending ? rows.ThenByDescending(key, comparer) : rows.ThenBy(key, comparer);
    }
}
