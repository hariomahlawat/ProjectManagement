// -----------------------------------------------------------------------------
// Areas/ProjectOfficeReports/Pages/Training/Index.cshtml.cs
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

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTrainingTracker)]
public class IndexModel : PageModel
{
    // -------------------------------------------------------------------------
    // constants
    // -------------------------------------------------------------------------
    private const int DashboardTrainingRowLimit = 5;

    // -------------------------------------------------------------------------
    // ctor & deps
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // view-facing props
    // -------------------------------------------------------------------------
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

    // dashboard shows only a slice
    public IReadOnlyList<TrainingRowViewModel> Trainings { get; private set; } = Array.Empty<TrainingRowViewModel>();
    public bool HasResults => Trainings.Count > 0;

    // this tells the view to show the “View all records” button
    public bool HasMoreRecords { get; private set; }

    // KPIs
    public TrainingKpiDto Kpis { get; private set; } = new();

    // chart rows
    public IReadOnlyList<TrainingYearChartRow> TrainingYearChart { get; private set; } = Array.Empty<TrainingYearChartRow>();

    // strength for KPI cards
    public int TotalOfficers { get; private set; }
    public int TotalJcos { get; private set; }
    public int TotalOrs { get; private set; }

    // simulator / drone strength (we added these earlier)
    public StrengthBreakdown SimulatorStrength { get; private set; } = StrengthBreakdown.Empty;
    public StrengthBreakdown DroneStrength { get; private set; } = StrengthBreakdown.Empty;

    // -------------------------------------------------------------------------
    // GET
    // -------------------------------------------------------------------------
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateAsync(cancellationToken);
        return Page();
    }

    // -------------------------------------------------------------------------
    // POST Export
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // main populate
    // -------------------------------------------------------------------------
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

        // export defaults
        BackfillExportDefaultsFromFilter();

        if (!IsFeatureEnabled)
        {
            Trainings = Array.Empty<TrainingRowViewModel>();
            Kpis = new();
            TrainingYearChart = Array.Empty<TrainingYearChartRow>();
            TotalOfficers = TotalJcos = TotalOrs = 0;
            return;
        }

        // ------------------------------------------------------------
        // fetch full list from service (existing behavior)
        // ------------------------------------------------------------
        var query = BuildQuery(Filter);
        var results = await _readService.SearchAsync(query, cancellationToken);

        // ------------------------------------------------------------
        // map rows (we want most recent at top)
        // ------------------------------------------------------------
        var ordered = results
            .OrderByDescending(r => r.StartDate ?? r.EndDate) // recency
            .ThenByDescending(r => r.CounterTotal)
            .ToList();

        // full count (for “View all”)
        var totalCount = ordered.Count;

        // take only the dashboard slice
        var slice = ordered
            .Take(DashboardTrainingRowLimit)
            .Select(TrainingRowViewModel.FromListItem)
            .ToList();

        Trainings = slice;
        HasMoreRecords = totalCount > DashboardTrainingRowLimit;

        // ------------------------------------------------------------
        // KPIs (unchanged)
        // ------------------------------------------------------------
        Kpis = await _readService.GetKpisAsync(query, cancellationToken);

        // aggregate strength for KPI cards (total)
        TotalOfficers = results.Sum(r => r.CounterOfficers);
        TotalJcos = results.Sum(r => r.CounterJcos);
        TotalOrs = results.Sum(r => r.CounterOrs);

        // training-year chart rows (we already had this)
        TrainingYearChart = Kpis.ByTrainingYear
            .Select(x => new TrainingYearChartRow(
                x.TrainingYearLabel,
                x.SimulatorTrainings,
                x.DroneTrainings,
                x.TotalTrainees))
            .ToList();

        // simulator/drone bracket strength — computed from filtered set
        SimulatorStrength = StrengthBreakdown.FromRows(results, "Simulator");
        DroneStrength = StrengthBreakdown.FromRows(results, "Drone");
    }

    // -------------------------------------------------------------------------
    // load dropdown options
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // build query from filter
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // keep export in sync with current filters
    // -------------------------------------------------------------------------
    private void BackfillExportDefaultsFromFilter()
    {
        Export.From ??= Filter.From;
        Export.To ??= Filter.To;
        Export.ProjectTechnicalCategoryId ??= Filter.ProjectTechnicalCategoryId;
        Export.TypeId ??= Filter.TypeId;
        Export.Category ??= Filter.Category;
        Export.Search ??= Filter.Search;
    }

    // -------------------------------------------------------------------------
    // filter input
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // export input
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // build hierarchical technical category options
    // -------------------------------------------------------------------------
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

    // -------------------------------------------------------------------------
    // row VM (used by table)
    // -------------------------------------------------------------------------
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

            return string.Format(CultureInfo.InvariantCulture, "{0} days", dayCount);
        }
    }

    // -------------------------------------------------------------------------
    // used by chart
    // -------------------------------------------------------------------------
    public sealed record TrainingYearChartRow(
        string TrainingYearLabel,
        int SimulatorTrainings,
        int DroneTrainings,
        int TotalTrainees);

    // -------------------------------------------------------------------------
    // strength helper for simulator/drone KPI
    // -------------------------------------------------------------------------
    public readonly record struct StrengthBreakdown(int Officers, int Jcos, int Ors)
    {
        public static StrengthBreakdown Empty => new(0, 0, 0);

        public static StrengthBreakdown FromRows(IReadOnlyList<TrainingListItem> rows, string typeName)
        {
            var filtered = rows.Where(r => string.Equals(r.TrainingTypeName, typeName, StringComparison.OrdinalIgnoreCase));
            return new StrengthBreakdown(
                filtered.Sum(r => r.CounterOfficers),
                filtered.Sum(r => r.CounterJcos),
                filtered.Sum(r => r.CounterOrs));
        }
    }
}
