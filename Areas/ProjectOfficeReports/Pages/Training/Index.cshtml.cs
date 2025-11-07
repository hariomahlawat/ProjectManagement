// -----------------------------------------------------------------------------
// Training tracker - PageModel
// - trainees take centre stage
// - KPI cards get overall strength
// - simulator/drone chart sends TRAINEE counts (computed from list)
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training
{
    [Authorize(Policy = ProjectOfficeReportsPolicies.ViewTrainingTracker)]
    public class IndexModel : PageModel
    {
        // ---------------------------------------------------------------------
        // deps
        // ---------------------------------------------------------------------
        private readonly IOptionsSnapshot<TrainingTrackerOptions> _options;
        private readonly TrainingTrackerReadService _readService;
        private readonly ITrainingExportService _exportService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuthorizationService _authorizationService;

        public IndexModel(
            IOptionsSnapshot<TrainingTrackerOptions> options,
            TrainingTrackerReadService readService,
            ITrainingExportService exportService,
            UserManager<ApplicationUser> userManager,
            IAuthorizationService authorizationService)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        // ---------------------------------------------------------------------
        // view-facing props
        // ---------------------------------------------------------------------
        public bool IsFeatureEnabled { get; private set; }
        public bool CanApproveTrainingTracker { get; private set; }
        public bool CanManageTrainingTracker { get; private set; }

        [BindProperty(SupportsGet = true)]
        public FilterInput Filter { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public ExportInput Export { get; set; } = new();

        public IReadOnlyList<SelectListItem> TrainingTypes { get; private set; } = Array.Empty<SelectListItem>();
        public IReadOnlyList<SelectListItem> ProjectTechnicalCategoryOptions { get; private set; } = Array.Empty<SelectListItem>();
        public IReadOnlyList<SelectListItem> CategoryOptions { get; } = new List<SelectListItem>
        {
            new("All categories", string.Empty),
            new("Officers", TrainingCategory.Officer.ToString()),
            new("Junior Commissioned Officers", TrainingCategory.JuniorCommissionedOfficer.ToString()),
            new("Other Ranks", TrainingCategory.OtherRank.ToString())
        };

        public IReadOnlyList<TrainingRowViewModel> Trainings { get; private set; } = Array.Empty<TrainingRowViewModel>();
        public bool HasResults => Trainings.Count > 0;

        // KPIs from service
        public TrainingKpiDto Kpis { get; private set; } = new();

        // chart rows we actually send to the browser
        public IReadOnlyList<TrainingYearChartRow> TrainingYearChart { get; private set; } = Array.Empty<TrainingYearChartRow>();

        // overall strength for "Total trainees"
        public int TotalOfficers { get; private set; }
        public int TotalJcos { get; private set; }
        public int TotalOrs { get; private set; }

        // we’ll also expose per-type strength for simulator/drone cards
        public (int Officers, int Jcos, int Ors) SimulatorStrength { get; private set; }
        public (int Officers, int Jcos, int Ors) DroneStrength { get; private set; }

        // ---------------------------------------------------------------------
        // GET
        // ---------------------------------------------------------------------
        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            await PopulateAsync(cancellationToken);
            return Page();
        }

        // ---------------------------------------------------------------------
        // POST Export
        // ---------------------------------------------------------------------
        public async Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
        {
            IsFeatureEnabled = _options.Value.Enabled;
            if (!IsFeatureEnabled)
            {
                return Forbid();
            }

            BackfillExportDefaultsFromFilter();

            if (!ModelState.IsValid)
            {
                await PopulateAsync(cancellationToken);
                ViewData["ShowTrainingExportModal"] = true;
                return Page();
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                TempData["ToastError"] = "You are not signed in or your session has expired. Please sign in again.";
                return Challenge();
            }

            var request = new TrainingExportRequest(
                Export.TypeId,
                Export.Category,
                Export.ProjectTechnicalCategoryId,
                Export.From,
                Export.To,
                Export.Search,
                Export.IncludeRoster,
                userId);

            var result = await _exportService.ExportAsync(request, cancellationToken);
            if (!result.Success || result.File is null)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error);
                }

                if (result.Errors.Count > 0)
                {
                    TempData["ToastError"] = result.Errors[0];
                }

                await PopulateAsync(cancellationToken);
                ViewData["ShowTrainingExportModal"] = true;
                return Page();
            }

            return File(result.File.Content, result.File.ContentType, result.File.FileName);
        }

        // ---------------------------------------------------------------------
        // main populate
        // ---------------------------------------------------------------------
        private async Task PopulateAsync(CancellationToken cancellationToken)
        {
            IsFeatureEnabled = _options.Value.Enabled;

            // permissions
            var manageAuthorization = await _authorizationService.AuthorizeAsync(
                User,
                resource: null,
                ProjectOfficeReportsPolicies.ManageTrainingTracker);
            CanManageTrainingTracker = manageAuthorization.Succeeded;

            var approvalAuthorization = await _authorizationService.AuthorizeAsync(
                User,
                resource: null,
                ProjectOfficeReportsPolicies.ApproveTrainingTracker);
            CanApproveTrainingTracker = approvalAuthorization.Succeeded;

            // dropdowns
            await LoadOptionsAsync(cancellationToken);

            // line up export with current filter
            BackfillExportDefaultsFromFilter();

            if (!IsFeatureEnabled)
            {
                Trainings = Array.Empty<TrainingRowViewModel>();
                Kpis = new();
                TrainingYearChart = Array.Empty<TrainingYearChartRow>();
                TotalOfficers = TotalJcos = TotalOrs = 0;
                SimulatorStrength = (0, 0, 0);
                DroneStrength = (0, 0, 0);
                return;
            }

            // ---------- get list -------------
            var query = BuildQuery(Filter);
            var results = await _readService.SearchAsync(query, cancellationToken);
            Trainings = results.Select(TrainingRowViewModel.FromListItem).ToList();

            // ---------- KPIs from service ----
            Kpis = await _readService.GetKpisAsync(query, cancellationToken);

            // ---------- overall strength (for total trainees) -----------
            TotalOfficers = results.Sum(r => r.CounterOfficers);
            TotalJcos = results.Sum(r => r.CounterJcos);
            TotalOrs = results.Sum(r => r.CounterOrs);

            // ---------- per-type strength (for simulator/drone cards) ---
            SimulatorStrength = AggregateStrengthForType(results, "Simulator");
            DroneStrength = AggregateStrengthForType(results, "Drone");

            // ---------- chart: trainees per Apr–Mar year ----------------
            TrainingYearChart = BuildTrainingYearChart(Kpis.ByTrainingYear, results);
        }

        // ---------------------------------------------------------------------
        // build chart rows: we take the year buckets from KPIs (for ordering),
        // but we fill them with trainee counts that we compute from the actual
        // training rows we loaded above.
        // ---------------------------------------------------------------------
        private static IReadOnlyList<TrainingYearChartRow> BuildTrainingYearChart(
            IReadOnlyList<TrainingYearBucketDto> kpiBuckets,
            IReadOnlyList<TrainingListItem> rows)
        {
            // group rows by year label and by training type
            var bucketLookup = new Dictionary<string, (int SimTrainees, int DroneTrainees)>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var label = GetFinancialYearLabel(row);
                var trainees = row.CounterTotal;
                var typeName = row.TrainingTypeName ?? string.Empty;

                if (!bucketLookup.TryGetValue(label, out var current))
                {
                    current = (0, 0);
                }

                if (typeName.Equals("Simulator", StringComparison.OrdinalIgnoreCase))
                {
                    current.SimTrainees += trainees;
                }
                else if (typeName.Equals("Drone", StringComparison.OrdinalIgnoreCase))
                {
                    current.DroneTrainees += trainees;
                }

                bucketLookup[label] = current;
            }

            // now align with KPI buckets (so the same years show up in the chart)
            var result = new List<TrainingYearChartRow>();
            foreach (var kpiBucket in kpiBuckets)
            {
                var label = kpiBucket.TrainingYearLabel;
                bucketLookup.TryGetValue(label, out var counts);
                result.Add(new TrainingYearChartRow(
                    label,
                    counts.SimTrainees,
                    counts.DroneTrainees));
            }

            return result;
        }

        // ---------------------------------------------------------------------
        // helper: build an Apr–Mar year label (2023-24)
        // ---------------------------------------------------------------------
        private static string GetFinancialYearLabel(TrainingListItem item)
        {
            // most reliable is actual date
            DateOnly? date = item.StartDate ?? item.EndDate;

            int fyStart;
            if (date.HasValue)
            {
                // Apr (4) – Mar (3) year
                fyStart = date.Value.Month >= 4 ? date.Value.Year : date.Value.Year - 1;
            }
            else if (item.TrainingYear.HasValue)
            {
                fyStart = item.TrainingYear.Value;
            }
            else
            {
                // fallback bucket
                return "Unspecified";
            }

            var fyEnd = (fyStart + 1) % 100;
            return $"{fyStart}-{fyEnd:00}";
        }

        // ---------------------------------------------------------------------
        // helper: sum strength for a specific training type
        // ---------------------------------------------------------------------
        private static (int Officers, int Jcos, int Ors) AggregateStrengthForType(
            IReadOnlyList<TrainingListItem> rows,
            string typeName)
        {
            var filtered = rows
                .Where(r => string.Equals(r.TrainingTypeName, typeName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return (
                filtered.Sum(r => r.CounterOfficers),
                filtered.Sum(r => r.CounterJcos),
                filtered.Sum(r => r.CounterOrs)
            );
        }

        // ---------------------------------------------------------------------
        // dropdowns
        // ---------------------------------------------------------------------
        private async Task LoadOptionsAsync(CancellationToken cancellationToken)
        {
            var selectedTypeId = Filter.TypeId.GetValueOrDefault();

            var options = new List<SelectListItem>
            {
                new("All training types", string.Empty)
                {
                    Selected = selectedTypeId == Guid.Empty
                }
            };

            options.AddRange((await _readService.GetTrainingTypesAsync(cancellationToken))
                .Select(option => new SelectListItem(option.Name, option.Id.ToString())
                {
                    Selected = option.Id == selectedTypeId
                }));

            TrainingTypes = options;

            var technicalCategories = await _readService.GetProjectTechnicalCategoryOptionsAsync(cancellationToken);
            var selectedTechnicalCategoryId = Export.ProjectTechnicalCategoryId ?? Filter.ProjectTechnicalCategoryId;
            ProjectTechnicalCategoryOptions = BuildTechnicalCategoryOptions(technicalCategories, selectedTechnicalCategoryId);
        }

        // ---------------------------------------------------------------------
        // query builder
        // ---------------------------------------------------------------------
        private static TrainingTrackerQuery BuildQuery(FilterInput filter)
        {
            var query = new TrainingTrackerQuery
            {
                ProjectTechnicalCategoryId = filter.ProjectTechnicalCategoryId,
                From = filter.From,
                To = filter.To,
                Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim()
            };

            if (filter.Category.HasValue)
            {
                query.Category = filter.Category.Value;
            }

            if (filter.TypeId is { } typeId && typeId != Guid.Empty)
            {
                query.TrainingTypeIds.Add(typeId);
            }

            return query;
        }

        // ---------------------------------------------------------------------
        // keep export in sync
        // ---------------------------------------------------------------------
        private void BackfillExportDefaultsFromFilter()
        {
            Export.From ??= Filter.From;
            Export.To ??= Filter.To;
            Export.ProjectTechnicalCategoryId ??= Filter.ProjectTechnicalCategoryId;
            Export.TypeId ??= Filter.TypeId;
            Export.Category ??= Filter.Category;
            Export.Search ??= Filter.Search;
        }

        // ---------------------------------------------------------------------
        // input classes
        // ---------------------------------------------------------------------
        public sealed class FilterInput
        {
            [Display(Name = "Training type")]
            public Guid? TypeId { get; set; }

            [Display(Name = "Project technical category")]
            public int? ProjectTechnicalCategoryId { get; set; }

            [Display(Name = "Category")]
            public TrainingCategory? Category { get; set; }

            [DataType(DataType.Date)]
            [Display(Name = "From")]
            public DateOnly? From { get; set; }

            [DataType(DataType.Date)]
            [Display(Name = "To")]
            public DateOnly? To { get; set; }

            [Display(Name = "Search")]
            public string? Search { get; set; }
        }

        public sealed class ExportInput
        {
            [Display(Name = "Training type")]
            public Guid? TypeId { get; set; }

            [Display(Name = "Category")]
            public TrainingCategory? Category { get; set; }

            [DataType(DataType.Date)]
            [Display(Name = "From")]
            public DateOnly? From { get; set; }

            [DataType(DataType.Date)]
            [Display(Name = "To")]
            public DateOnly? To { get; set; }

            [Display(Name = "Project technical category")]
            public int? ProjectTechnicalCategoryId { get; set; }

            [Display(Name = "Search")]
            public string? Search { get; set; }

            [Display(Name = "Include roster details")]
            public bool IncludeRoster { get; set; }
        }

        // ---------------------------------------------------------------------
        // hierarchical technical category options
        // ---------------------------------------------------------------------
        private static IReadOnlyList<SelectListItem> BuildTechnicalCategoryOptions(
            IEnumerable<ProjectTechnicalCategoryOption> categories,
            int? selectedId)
        {
            var categoryList = categories.ToList();

            var lookup = categoryList
                .Where(category => category.IsActive)
                .ToLookup(category => category.ParentId);

            var options = new List<SelectListItem>
            {
                new("All technical categories", string.Empty, !selectedId.HasValue)
            };

            void AddOptions(int? parentId, string prefix)
            {
                foreach (var category in lookup[parentId].OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var text = string.IsNullOrEmpty(prefix) ? category.Name : $"{prefix}{category.Name}";
                    var isSelected = selectedId.HasValue && selectedId.Value == category.Id;
                    options.Add(new SelectListItem(text, category.Id.ToString(), isSelected));
                    AddOptions(category.Id, string.Concat(prefix, "— "));
                }
            }

            AddOptions(null, string.Empty);

            if (selectedId.HasValue)
            {
                var selectedValue = selectedId.Value.ToString();
                if (options.All(option => !string.Equals(option.Value, selectedValue, StringComparison.Ordinal)))
                {
                    var selected = categoryList.FirstOrDefault(category => category.Id == selectedId.Value);
                    if (selected is not null)
                    {
                        options.Add(new SelectListItem($"{selected.Name} (inactive)", selected.Id.ToString(), true));
                    }
                }
            }

            return options;
        }

        // ---------------------------------------------------------------------
        // list row VM
        // ---------------------------------------------------------------------
        public sealed record TrainingRowViewModel(
            Guid Id,
            string TrainingTypeName,
            string Period,
            string Strength,
            int Total,
            TrainingCounterSource Source,
            string? Notes,
            IReadOnlyList<string> ProjectNames)
        {
            public string? PeriodDayCount { get; init; }

            public static TrainingRowViewModel FromListItem(TrainingListItem item)
            {
                var strength = $"{item.CounterOfficers:N0} – {item.CounterJcos:N0} – {item.CounterOrs:N0}";
                var (period, dayCount) = FormatPeriod(item);
                return new TrainingRowViewModel(
                    item.Id,
                    item.TrainingTypeName,
                    period,
                    strength,
                    item.CounterTotal,
                    item.CounterSource,
                    item.Notes,
                    item.ProjectNames)
                {
                    PeriodDayCount = dayCount
                };
            }

            private static (string Period, string? DayCount) FormatPeriod(TrainingListItem item)
            {
                if (item.StartDate.HasValue || item.EndDate.HasValue)
                {
                    var normalizedEnd = item.EndDate ?? item.StartDate;

                    var start = item.StartDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "(not set)";
                    var end = item.EndDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? start;
                    var period = start == end ? start : $"{start} – {end}";

                    if (item.StartDate.HasValue && normalizedEnd.HasValue)
                    {
                        var dayCount = normalizedEnd.Value.DayNumber - item.StartDate.Value.DayNumber + 1;
                        var dayCountText = FormatDayCount(dayCount);
                        return (period, dayCountText);
                    }

                    return (period, null);
                }

                if (item.TrainingYear.HasValue && item.TrainingMonth.HasValue)
                {
                    var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(item.TrainingMonth.Value);
                    return ($"{monthName} {item.TrainingYear.Value}", null);
                }

                return ("(unspecified)", null);
            }

            private static string FormatDayCount(int dayCount)
            {
                if (dayCount <= 1)
                {
                    return "1 day";
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} days",
                    dayCount);
            }
        }

        // ---------------------------------------------------------------------
        // chart row (what JS expects)
        // ---------------------------------------------------------------------
        public sealed record TrainingYearChartRow(
            string TrainingYearLabel,
            int SimulatorTrainees,
            int DroneTrainees);
    }
}
