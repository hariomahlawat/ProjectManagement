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
            var projects = await _trackerService.GetAsync(new ProjectTotTrackerFilter(), cancellationToken);
            Summary = TotSummaryViewModel.FromProjects(projects);
        }

        // GET: /Tot/Summary?handler=Yearly
        // returns: [{ "year": 2021, "count": 4 }, ...]
        public async Task<JsonResult> OnGetYearlyAsync(CancellationToken cancellationToken)
        {
            var projects = await _trackerService.GetAsync(new ProjectTotTrackerFilter(), cancellationToken);

            var data = projects
                .Where(p => p.TotStatus == ProjectTotStatus.Completed && p.ProjectCompletedYear.HasValue)
                .GroupBy(p => p.ProjectCompletedYear!.Value)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    year = g.Key,
                    count = g.Count()
                })
                .ToList();

            return new JsonResult(data);
        }

        public sealed record TotSummaryViewModel(
            string Narrative,
            IReadOnlyList<TotSummaryEntry> Completed,
            IReadOnlyList<TotSummaryEntry> NotStarted,
            IReadOnlyList<TotSummaryEntry> InProgressMetComplete,
            IReadOnlyList<TotSummaryEntry> InProgressMetIncomplete,
            IReadOnlyList<TotSummaryEntry> NotRequired,
            int TotalProjects)
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

            public static TotSummaryViewModel FromProjects(IReadOnlyList<ProjectTotTrackerRow>? projects)
            {
                var items = projects ?? Array.Empty<ProjectTotTrackerRow>();

                var completed = new List<TotSummaryEntry>();
                var notStarted = new List<TotSummaryEntry>();
                var inProgressMetComplete = new List<TotSummaryEntry>();
                var inProgressMetIncomplete = new List<TotSummaryEntry>();
                var notRequired = new List<TotSummaryEntry>();

                foreach (var row in items)
                {
                    var status = row.TotStatus ?? ProjectTotStatus.NotStarted;
                    var entry = new TotSummaryEntry(
                        row.ProjectId,
                        row.ProjectName,
                        row.ProjectCompletedOn,
                        row.ProjectCompletedYear);

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

                var narrative = BuildNarrative(
                    items.Count,
                    completed.Count,
                    notStarted.Count,
                    inProgressMetComplete.Count,
                    inProgressMetIncomplete.Count,
                    notRequired.Count);

                return new TotSummaryViewModel(
                    narrative,
                    completed,
                    notStarted,
                    inProgressMetComplete,
                    inProgressMetIncomplete,
                    notRequired,
                    items.Count);
            }

            public static string? FormatCompletionLabel(TotSummaryEntry entry)
            {
                if (entry.ProjectCompletedOn.HasValue)
                {
                    return entry.ProjectCompletedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                }

                if (entry.ProjectCompletedYear.HasValue)
                {
                    return entry.ProjectCompletedYear.Value.ToString(CultureInfo.InvariantCulture);
                }

                return null;
            }

            private static string BuildNarrative(
                int total,
                int completed,
                int notStarted,
                int inProgressMetComplete,
                int inProgressMetIncomplete,
                int notRequired)
            {
                if (total == 0)
                {
                    return "No completed projects have Transfer of Technology progress to report yet.";
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

                if (notRequired > 0)
                {
                    segments.Add($"{FormatCount(notRequired, "project does", "projects do")} not require Transfer of Technology");
                }

                var prefix = total == 1
                    ? "There is 1 completed project"
                    : $"Across {total} completed projects";

                if (segments.Count == 0)
                {
                    return $"{prefix}, but no projects have reported Transfer of Technology progress yet.";
                }

                var summary = segments.Count switch
                {
                    1 => segments[0],
                    2 => string.Join(" and ", segments),
                    _ => string.Join(", ", segments.Take(segments.Count - 1)) + ", and " + segments[^1]
                };

                return $"{prefix}, {summary}.";
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
            int? ProjectCompletedYear);
    }
}
