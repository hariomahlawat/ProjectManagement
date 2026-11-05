using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot
{
    [Authorize(Policy = ProjectOfficeReportsPolicies.ViewTotTracker)]
    public sealed class SummaryModel : PageModel
    {
        private readonly ProjectTotTrackerReadService _trackerService;

        public SummaryModel(ProjectTotTrackerReadService trackerService)
        {
            _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        }

        public TotSummaryViewModel Summary { get; private set; } = TotSummaryViewModel.Empty;

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var projects = await _trackerService.GetAsync(
                new ProjectTotTrackerFilter(),
                cancellationToken);

            Summary = TotSummaryViewModel.FromProjects(projects);
        }

        // GET: /Tot/Summary?handler=Yearly
        // returns: [{ "year": 2021, "count": 4 }, ...]
        public async Task<JsonResult> OnGetYearlyAsync(CancellationToken cancellationToken)
        {
            var projects = await _trackerService.GetAsync(new ProjectTotTrackerFilter(), cancellationToken);

            return new JsonResult(BuildYearlyCompletions(projects)
                .Select(point => new { year = point.Year, count = point.Count })
                .ToList());
        }

        // One point per year from the first to the last completion, so a year with no completions
        // shows as zero instead of being left off the axis.
        internal static IReadOnlyList<(int Year, int Count)> BuildYearlyCompletions(IReadOnlyList<ProjectTotTrackerRow> projects)
        {
            var counts = projects
                .Where(p => p.TotStatus == ProjectTotStatus.Completed && p.TotCompletedOn.HasValue)
                .GroupBy(p => p.TotCompletedOn!.Value.Year)
                .ToDictionary(g => g.Key, g => g.Count());

            if (counts.Count == 0)
            {
                return Array.Empty<(int, int)>();
            }

            var first = counts.Keys.Min();
            var last = counts.Keys.Max();
            return Enumerable.Range(first, last - first + 1)
                .Select(year => (year, counts.GetValueOrDefault(year)))
                .ToList();
        }

        public sealed record TotSummaryViewModel(
            string Narrative,
            IReadOnlyList<TotSummaryEntry> Completed,
            IReadOnlyList<TotSummaryEntry> NotStarted,
            IReadOnlyList<TotSummaryEntry> InProgressMetComplete,
            IReadOnlyList<TotSummaryEntry> InProgressMetIncomplete,
            IReadOnlyList<TotSummaryEntry> NotRequired,
            int TotalProjects,
            IReadOnlyList<TotSummaryEntry>? NotKnown = null)
        {
            public static TotSummaryViewModel Empty { get; } = new(
                string.Empty,
                Array.Empty<TotSummaryEntry>(),
                Array.Empty<TotSummaryEntry>(),
                Array.Empty<TotSummaryEntry>(),
                Array.Empty<TotSummaryEntry>(),
                Array.Empty<TotSummaryEntry>(),
                0);

            public int CompletedCount => Completed.Count;
            public int NotStartedCount => NotStarted.Count;
            public int InProgressMetCompleteCount => InProgressMetComplete.Count;
            public int InProgressMetIncompleteCount => InProgressMetIncomplete.Count;
            public int InProgressCount => InProgressMetCompleteCount + InProgressMetIncompleteCount;
            public int NotRequiredCount => NotRequired.Count;
            public IReadOnlyList<TotSummaryEntry> NotKnownProjects => NotKnown ?? Array.Empty<TotSummaryEntry>();
            public int NotKnownCount => NotKnownProjects.Count;

            public static TotSummaryViewModel FromProjects(IReadOnlyList<ProjectTotTrackerRow>? projects)
            {
                var items = projects ?? Array.Empty<ProjectTotTrackerRow>();

                var completed = new List<TotSummaryEntry>();
                var notStarted = new List<TotSummaryEntry>();
                var inProgressMetComplete = new List<TotSummaryEntry>();
                var inProgressMetIncomplete = new List<TotSummaryEntry>();
                var notRequired = new List<TotSummaryEntry>();
                var notKnown = new List<TotSummaryEntry>();

                foreach (var row in items)
                {
                    var status = row.TotStatus ?? ProjectTotStatus.NotStarted;
                    var entry = new TotSummaryEntry(
                        row.ProjectId,
                        row.ProjectName,
                        row.ProjectCompletedOn,
                        row.ProjectCompletedYear,
                        row.TotCompletedOn);

                    switch (status)
                    {
                        case ProjectTotStatus.Completed:
                            completed.Add(entry);
                            break;
                        case ProjectTotStatus.NotStarted:
                            notStarted.Add(entry);
                            break;
                        case ProjectTotStatus.NotRequired:
                            notRequired.Add(entry);
                            break;
                        case ProjectTotStatus.NotKnown:
                            notKnown.Add(entry);
                            break;
                        case ProjectTotStatus.InProgress:
                        default:
                            if (row.TotMetCompletedOn.HasValue)
                            {
                                inProgressMetComplete.Add(entry);
                            }
                            else
                            {
                                inProgressMetIncomplete.Add(entry);
                            }

                            break;
                    }
                }

                // "Not required" projects are not ToT-applicable: they are listed, but not counted in the total.
                var applicable = items.Count - notRequired.Count;
                var narrative = BuildNarrative(
                    applicable,
                    completed.Count,
                    notStarted.Count,
                    inProgressMetComplete.Count,
                    inProgressMetIncomplete.Count,
                    notRequired.Count,
                    notKnown.Count);

                return new TotSummaryViewModel(
                    narrative,
                    completed,
                    notStarted,
                    inProgressMetComplete,
                    inProgressMetIncomplete,
                    notRequired,
                    applicable,
                    notKnown);
            }

            public static string? FormatCompletionLabel(TotSummaryEntry entry)
            {
                var projectYear = entry.ProjectCompletedOn?.Year ?? entry.ProjectCompletedYear;
                var totYear = entry.TotCompletedOn?.Year;

                if (projectYear.HasValue && totYear.HasValue && projectYear.Value != totYear.Value)
                {
                    return $"{projectYear.Value} (ToT {totYear.Value})";
                }

                if (projectYear.HasValue && totYear.HasValue)
                {
                    return projectYear.Value.ToString(CultureInfo.InvariantCulture);
                }

                if (projectYear.HasValue)
                {
                    return projectYear.Value.ToString(CultureInfo.InvariantCulture);
                }

                if (totYear.HasValue)
                {
                    return $"ToT {totYear.Value.ToString(CultureInfo.InvariantCulture)}";
                }

                return null;
            }

            private static string BuildNarrative(
                int total,
                int completed,
                int notStarted,
                int inProgressMetComplete,
                int inProgressMetIncomplete,
                int notRequired,
                int notKnown = 0)
            {
                var notRequiredNote = notRequired switch
                {
                    0 => string.Empty,
                    1 => " A further 1 project does not require Transfer of Technology.",
                    _ => $" A further {notRequired} projects do not require Transfer of Technology."
                };

                if (total == 0)
                {
                    return "No ToT-applicable completed projects have Transfer of Technology progress to report yet." + notRequiredNote;
                }

                var segments = new List<string>();

                if (completed > 0)
                {
                    segments.Add($"{FormatCount(completed, "project has", "projects have")} completed Transfer of Technology");
                }

                if (notStarted > 0)
                {
                    segments.Add($"{FormatCount(notStarted, "project has", "projects have")} not yet started Transfer of Technology work");
                }

                var inProgressSegments = new List<string>();

                if (inProgressMetComplete > 0)
                {
                    inProgressSegments.Add($"{FormatCount(inProgressMetComplete, "project is", "projects are")} in progress with MET completed");
                }

                if (inProgressMetIncomplete > 0)
                {
                    inProgressSegments.Add($"{FormatCount(inProgressMetIncomplete, "project is", "projects are")} in progress without MET completed");
                }

                if (inProgressSegments.Count > 0)
                {
                    segments.Add(string.Join(" and ", inProgressSegments));
                }

                if (notKnown > 0)
                {
                    segments.Add(notKnown == 1
                        ? "for 1 project it is not known whether Transfer of Technology took place"
                        : $"for {notKnown} projects it is not known whether Transfer of Technology took place");
                }

                var prefix = total == 1
                    ? "There is 1 ToT-applicable completed project"
                    : $"Across {total} ToT-applicable completed projects";

                if (segments.Count == 0)
                {
                    return $"{prefix}, but no projects have reported Transfer of Technology progress yet.{notRequiredNote}";
                }

                var summary = segments.Count switch
                {
                    1 => segments[0],
                    2 => string.Join(" and ", segments),
                    _ => string.Join(", ", segments.Take(segments.Count - 1)) + ", and " + segments[^1]
                };

                return $"{prefix}, {summary}.{notRequiredNote}";
            }

            private static string FormatCount(int count, string singular, string plural)
            {
                return count == 1 ? $"1 {singular}" : $"{count} {plural}";
            }
        }

        public sealed record TotSummaryEntry(
            int ProjectId,
            string ProjectName,
            DateOnly? ProjectCompletedOn,
            int? ProjectCompletedYear,
            DateOnly? TotCompletedOn);
    }
}
