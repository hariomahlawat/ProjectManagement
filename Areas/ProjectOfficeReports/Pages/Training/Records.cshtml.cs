// -----------------------------------------------------------------------------
// Areas/ProjectOfficeReports/Pages/Training/Records.cshtml.cs
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTrainingTracker)]
public class RecordsModel : PageModel
{
    private readonly TrainingTrackerReadService _readService;
    private readonly IAuthorizationService _authorizationService;

    private const int DefaultPageSize = 50;

    public RecordsModel(
        TrainingTrackerReadService readService,
        IAuthorizationService authorizationService)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    // filters (same as index)
    [BindProperty(SupportsGet = true)]
    public FilterInput Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public bool CanManageTrainingTracker { get; private set; }

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
    public int TotalCount { get; private set; }
    public int PageSize { get; private set; } = DefaultPageSize;
    public bool HasResults => Trainings.Count > 0;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        // permissions
        var manageAuthorization = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.ManageTrainingTracker);
        CanManageTrainingTracker = manageAuthorization.Succeeded;

        // dropdowns
        await LoadOptionsAsync(cancellationToken);

        // query
        var query = BuildQuery(Filter);
        var paged = await _readService.SearchPagedAsync(query, PageNumber, PageSize, cancellationToken);

        Trainings = paged.Items.Select(TrainingRowViewModel.FromListItem).ToList();
        TotalCount = paged.TotalCount;
        PageNumber = paged.PageNumber;
        PageSize = paged.PageSize;

        return Page();
    }

    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------
    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var trainingTypes = await _readService.GetTrainingTypesAsync(cancellationToken);
        var selectedTypeId = Filter.TypeId.GetValueOrDefault();

        var typeOptions = new List<SelectListItem>
        {
            new("All training types", string.Empty)
            {
                Selected = selectedTypeId == Guid.Empty
            }
        };

        typeOptions.AddRange(trainingTypes.Select(t => new SelectListItem(t.Name, t.Id.ToString())
        {
            Selected = t.Id == selectedTypeId
        }));

        TrainingTypes = typeOptions;

        var technicalCategories = await _readService.GetProjectTechnicalCategoryOptionsAsync(cancellationToken);
        var selectedTechnicalCategoryId = Filter.ProjectTechnicalCategoryId;
        ProjectTechnicalCategoryOptions = BuildTechnicalCategoryOptions(technicalCategories, selectedTechnicalCategoryId);
    }

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

    private static IReadOnlyList<SelectListItem> BuildTechnicalCategoryOptions(
        IEnumerable<ProjectTechnicalCategoryOption> categories,
        int? selectedId)
    {
        var categoryList = categories.ToList();
        var lookup = categoryList
            .Where(c => c.IsActive)
            .ToLookup(c => c.ParentId);

        var options = new List<SelectListItem>
        {
            new("All technical categories", string.Empty, !selectedId.HasValue)
        };

        void AddOptions(int? parentId, string prefix)
        {
            foreach (var c in lookup[parentId].OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var text = string.IsNullOrEmpty(prefix) ? c.Name : $"{prefix}{c.Name}";
                var isSelected = selectedId.HasValue && selectedId.Value == c.Id;
                options.Add(new SelectListItem(text, c.Id.ToString(), isSelected));
                AddOptions(c.Id, string.Concat(prefix, "— "));
            }
        }

        AddOptions(null, string.Empty);

        if (selectedId.HasValue &&
            options.All(o => o.Value != selectedId.Value.ToString(CultureInfo.InvariantCulture)))
        {
            var selected = categoryList.FirstOrDefault(c => c.Id == selectedId.Value);
            if (selected is not null)
            {
                options.Add(new SelectListItem($"{selected.Name} (inactive)", selected.Id.ToString(), true));
            }
        }

        return options;
    }

    // -------------------------------------------------------------------------
    // view models
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
                    var dayCountText = dayCount <= 1 ? "1 day" : $"{dayCount} days";
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
    }
}
