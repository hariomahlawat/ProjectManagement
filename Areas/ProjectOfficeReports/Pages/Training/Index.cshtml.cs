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
using Microsoft.AspNetCore.Http;
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
    // register paging and sort
    // -------------------------------------------------------------------------
    public const int DefaultPageSize = 25;

    public static readonly int[] AllowedPageSizes = [25, 50, 100];

    public static readonly IReadOnlyList<(string Value, string Label, TrainingRegisterSort Sort)> SortOptions =
    [
        ("newest", "Newest first", TrainingRegisterSort.Newest),
        ("oldest", "Oldest first", TrainingRegisterSort.Oldest),
        ("largest", "Most trainees", TrainingRegisterSort.Largest),
        ("type", "Training type", TrainingRegisterSort.Type)
    ];

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

    public int ExportTimeoutMilliseconds
    {
        get
        {
            var seconds = Math.Clamp(_options.Value.ExportTimeoutSeconds, 10, 900);
            return seconds * 1000;
        }
    }

    [BindProperty(SupportsGet = true)]
    public FilterInput Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public ExportInput Export { get; set; } = new();

    public IReadOnlyList<SelectListItem> TrainingTypes { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> ProjectOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> ProjectTechnicalCategoryOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> CategoryOptions { get; } = new List<SelectListItem>
    {
        new("All trainee categories", string.Empty),
        new("Officers", TrainingCategory.Officer.ToString()),
        new("Junior Commissioned Officers", TrainingCategory.JuniorCommissionedOfficer.ToString()),
        new("Other Ranks", TrainingCategory.OtherRank.ToString())
    };

    // project breakdown (per technical category)
    public IReadOnlyList<TechnicalCategoryProjectBreakdownDto> ProjectBreakdown { get; private set; } =
        Array.Empty<TechnicalCategoryProjectBreakdownDto>();
    public string? SelectedProjectName { get; private set; }

    // the register: one page of the matching trainings
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = DefaultPageSize;

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    public IReadOnlyList<TrainingRowViewModel> Trainings { get; private set; } = Array.Empty<TrainingRowViewModel>();
    public bool HasResults => Trainings.Count > 0;

    public int TotalItems { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalItems / PageSize));
    public int ShowingFrom => TotalItems == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
    public int ShowingTo => Math.Min(PageNumber * PageSize, TotalItems);

    public string SortKey => SortOptions.Any(option => option.Value == Sort) ? Sort! : "newest";

    // In date order the rows sit under month headings.
    public bool GroupByMonth => SortKey is "newest" or "oldest";

    // Month heading totals across every matching training, not only this page.
    public IReadOnlyDictionary<(int Year, int Month), (int Trainings, int Trainees)> MonthTotals { get; private set; } =
        new Dictionary<(int Year, int Month), (int Trainings, int Trainees)>();

    public bool HasFilters =>
        (Filter.TypeId.HasValue && Filter.TypeId.Value != Guid.Empty)
        || Filter.ProjectId.HasValue
        || Filter.ProjectTechnicalCategoryId.HasValue
        || Filter.Category.HasValue
        || Filter.From.HasValue
        || Filter.To.HasValue
        || !string.IsNullOrWhiteSpace(Filter.Search);

    // labels for the active-filter chips
    public string? SelectedTypeLabel => LabelFor(TrainingTypes, Filter.TypeId is { } id && id != Guid.Empty ? id.ToString() : null);
    public string? SelectedTechnicalCategoryLabel => LabelFor(ProjectTechnicalCategoryOptions, Filter.ProjectTechnicalCategoryId?.ToString(CultureInfo.InvariantCulture))?.TrimStart('—', ' ');
    public string? SelectedCategoryLabel => LabelFor(CategoryOptions, Filter.Category?.ToString());

    // KPIs
    public TrainingKpiDto Kpis { get; private set; } = new();

    // Technical category cards. They follow every filter except the category and project
    // themselves, so they stay a stable way to move between categories.
    public IReadOnlyList<TrainingKpiByTechnicalCategoryDto> TechnicalCategoryCards { get; private set; } =
        Array.Empty<TrainingKpiByTechnicalCategoryDto>();

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
            return IsAjaxRequest()
                ? StatusCode(StatusCodes.Status403Forbidden, new { errors = new[] { "Training exports are currently unavailable." } })
                : Forbid();
        }

        if (!ModelState.IsValid)
        {
            var errors = GetModelStateErrors();
            if (IsAjaxRequest())
            {
                return BadRequest(new { errors });
            }

            await PopulateAsync(cancellationToken);
            ViewData["ShowTrainingExportModal"] = true;
            return Page();
        }

        var user = await _userManager.GetUserAsync(User);
        var userId = user?.Id ?? _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            const string message = "You are not signed in or your session has expired. Please sign in again.";
            if (IsAjaxRequest())
            {
                return new UnauthorizedObjectResult(new { errors = new[] { message } });
            }

            TempData["ToastError"] = message;
            return Challenge();
        }

        var displayName = !string.IsNullOrWhiteSpace(user?.FullName)
            ? user.FullName
            : !string.IsNullOrWhiteSpace(user?.UserName)
                ? user.UserName!
                : userId;

        var applicationBaseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var request = new TrainingExportRequest(
            Export.TypeId,
            Export.Category,
            Export.ProjectId,
            Export.ProjectTechnicalCategoryId,
            Export.From,
            Export.To,
            Export.Search,
            Export.IncludeRoster,
            Export.RosterScope,
            userId,
            displayName,
            applicationBaseUrl);

        var result = await _exportService.ExportAsync(request, cancellationToken);
        if (!result.Success || result.File is null)
        {
            IReadOnlyList<string> errors = result.Errors.Count > 0
                ? result.Errors
                : new[] { "The export could not be generated." };

            if (IsAjaxRequest())
            {
                return BadRequest(new { errors });
            }

            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            TempData["ToastError"] = errors[0];
            await PopulateAsync(cancellationToken);
            ViewData["ShowTrainingExportModal"] = true;
            return Page();
        }

        return File(result.File.Content, result.File.ContentType, result.File.FileName);
    }

    private bool IsAjaxRequest()
        => string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

    private string[] GetModelStateErrors()
        => ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "The export request contains an invalid value."
                : error.ErrorMessage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
        // the register page, in the chosen order, with units
        // ------------------------------------------------------------
        if (!AllowedPageSizes.Contains(PageSize))
        {
            PageSize = DefaultPageSize;
        }

        TotalItems = results.Count;
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        var sort = SortOptions.First(option => option.Value == SortKey).Sort;
        var paged = await _readService.SearchPagedAsync(results, PageNumber, PageSize, sort, cancellationToken);

        Trainings = paged.Items.Select(TrainingRowViewModel.FromListItem).ToList();

        MonthTotals = results
            .Where(item => item.EffectiveDate.HasValue)
            .GroupBy(item => (item.EffectiveDate!.Value.Year, item.EffectiveDate!.Value.Month))
            .ToDictionary(group => group.Key, group => (group.Count(), group.Sum(item => item.CounterTotal)));

        // ------------------------------------------------------------
        // KPIs (unchanged)
        // ------------------------------------------------------------
        Kpis = await _readService.GetKpisAsync(query, cancellationToken);

        if (Filter.ProjectTechnicalCategoryId.HasValue || Filter.ProjectId.HasValue)
        {
            var cardQuery = BuildQuery(Filter);
            cardQuery.ProjectTechnicalCategoryId = null;
            cardQuery.ProjectId = null;
            TechnicalCategoryCards = await _readService.GetTechnicalCategoryKpisAsync(cardQuery, cancellationToken);
        }
        else
        {
            TechnicalCategoryCards = Kpis.ByTechnicalCategory;
        }

        // ------------------------------------------------------------
        // project breakdown (per technical category)
        // ------------------------------------------------------------
        ProjectBreakdown = Array.Empty<TechnicalCategoryProjectBreakdownDto>();
        SelectedProjectName = null;

        if (Filter.ProjectTechnicalCategoryId.HasValue)
        {
            ProjectBreakdown = await _readService.GetProjectBreakdownForTechnicalCategoryAsync(
                query,
                Filter.ProjectTechnicalCategoryId.Value,
                cancellationToken);

        }

        if (Filter.ProjectId.HasValue)
        {
            SelectedProjectName = ProjectOptions
                .FirstOrDefault(option => option.Value == Filter.ProjectId.Value.ToString(CultureInfo.InvariantCulture))
                ?.Text;
        }

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
        var selectedTypeId = (Export.TypeId ?? Filter.TypeId).GetValueOrDefault();

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

        var selectedProjectId = Export.ProjectId ?? Filter.ProjectId;
        var projects = await _readService.GetProjectOptionsAsync(
            selectedProjectId.HasValue ? new[] { selectedProjectId.Value } : Array.Empty<int>(),
            cancellationToken);
        ProjectOptions = new[] { new SelectListItem("All projects", string.Empty, !selectedProjectId.HasValue) }
            .Concat(projects.Select(project => new SelectListItem(
                project.Name,
                project.Id.ToString(CultureInfo.InvariantCulture),
                selectedProjectId == project.Id)))
            .ToList();

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
            ProjectId = filter.ProjectId,
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
        Export.ProjectId ??= Filter.ProjectId;
        Export.ProjectTechnicalCategoryId ??= Filter.ProjectTechnicalCategoryId;
        Export.TypeId ??= Filter.TypeId;
        Export.Category ??= Filter.Category;
        Export.Search ??= Filter.Search;
    }

    // -------------------------------------------------------------------------
    // route values for links that keep the current filters, with some replaced
    // -------------------------------------------------------------------------
    public Dictionary<string, string?> RouteWith(
        string? removeKey = null,
        int? pageNumber = null,
        string? sort = null,
        int? pageSize = null,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["TypeId"] = Filter.TypeId is { } typeId && typeId != Guid.Empty ? typeId.ToString() : null,
            ["ProjectTechnicalCategoryId"] = Filter.ProjectTechnicalCategoryId?.ToString(CultureInfo.InvariantCulture),
            ["ProjectId"] = Filter.ProjectId?.ToString(CultureInfo.InvariantCulture),
            ["Category"] = Filter.Category?.ToString(),
            ["From"] = Filter.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["To"] = Filter.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Search"] = string.IsNullOrWhiteSpace(Filter.Search) ? null : Filter.Search.Trim(),
            ["Sort"] = (sort ?? SortKey) == "newest" ? null : sort ?? SortKey,
            ["PageSize"] = (pageSize ?? PageSize) == DefaultPageSize ? null : (pageSize ?? PageSize).ToString(CultureInfo.InvariantCulture),
            ["PageNumber"] = pageNumber is > 1 ? pageNumber.Value.ToString(CultureInfo.InvariantCulture) : null
        };

        if (removeKey is not null)
        {
            values[removeKey] = null;
        }

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static string? LabelFor(IEnumerable<SelectListItem> options, string? value)
        => string.IsNullOrEmpty(value)
            ? null
            : options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))?.Text;

    // -------------------------------------------------------------------------
    // filter input
    // -------------------------------------------------------------------------
    public sealed class FilterInput
    {
        [Display(Name = "Training type")]
        public Guid? TypeId { get; set; }

        [Display(Name = "Project")]
        public int? ProjectId { get; set; }

        [Display(Name = "Project technical category")]
        public int? ProjectTechnicalCategoryId { get; set; }

        [Display(Name = "Training includes")]
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

        [Display(Name = "Project")]
        public int? ProjectId { get; set; }

        [Display(Name = "Training includes")]
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
        [StringLength(200, ErrorMessage = "Search text cannot exceed 200 characters.")]
        public string? Search { get; set; }

        [Display(Name = "Include roster details")]
        public bool IncludeRoster { get; set; }

        [Display(Name = "Roster rows")]
        public TrainingRosterScope RosterScope { get; set; } = TrainingRosterScope.AllTraineesInMatchingEvents;
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
        IReadOnlyList<string> ProjectNames,
        IReadOnlyList<string> Units,
        string UnitDisplay)
    {
        public string? PeriodDayCount { get; init; }

        // Date block for the register: a big first line, a month/year line and a short note.
        public DateOnly? EffectiveDate { get; init; }

        // Officers / JCOs / ORs for the register's labelled split.
        public int Officers { get; init; }
        public int Jcos { get; init; }
        public int Ors { get; init; }
        public string DateLead { get; init; } = "—";
        public string DateMonth { get; init; } = "Date not set";
        public string? DateNote { get; init; }

        public static TrainingRowViewModel FromListItem(TrainingListItem item)
        {
            var strength = $"{item.CounterOfficers:N0} · {item.CounterJcos:N0} · {item.CounterOrs:N0}";
            var (period, dayCount) = FormatPeriod(item);
            var (lead, month, note) = FormatDateBlock(item);
            return new TrainingRowViewModel(
                item.Id,
                item.TrainingTypeName,
                period,
                strength,
                item.CounterTotal,
                item.CounterSource,
                item.ProjectNames,
                item.Units,
                item.UnitDisplay)
            {
                PeriodDayCount = dayCount,
                EffectiveDate = item.EffectiveDate,
                Officers = item.CounterOfficers,
                Jcos = item.CounterJcos,
                Ors = item.CounterOrs,
                DateLead = lead,
                DateMonth = month,
                DateNote = note
            };
        }

        private static (string Lead, string Month, string? Note) FormatDateBlock(TrainingListItem item)
        {
            var inv = CultureInfo.InvariantCulture;

            if ((item.StartDate ?? item.EndDate) is { } start)
            {
                var end = item.EndDate ?? start;
                if (end <= start)
                {
                    return (start.ToString("dd", inv), start.ToString("MMM yyyy", inv), "1 day");
                }

                var days = end.DayNumber - start.DayNumber + 1;
                var endText = end.Year == start.Year ? end.ToString("dd MMM", inv) : end.ToString("dd MMM yyyy", inv);
                return (start.ToString("dd", inv), start.ToString("MMM yyyy", inv), $"to {endText} · {days} days");
            }

            if (item.EffectiveDate is { } monthStart)
            {
                return (monthStart.ToString("MMM", inv), monthStart.ToString("yyyy", inv), "Month only");
            }

            return ("—", "Date not set", null);
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
