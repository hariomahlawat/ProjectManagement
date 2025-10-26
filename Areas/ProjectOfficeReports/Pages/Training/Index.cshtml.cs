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
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Services;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTrainingTracker)]
public class IndexModel : PageModel
{
    private readonly IOptionsSnapshot<TrainingTrackerOptions> _options;
    private readonly TrainingTrackerReadService _readService;
    private readonly ITrainingExcelWorkbookBuilder _workbookBuilder;
    private readonly IClock _clock;

    public IndexModel(
        IOptionsSnapshot<TrainingTrackerOptions> options,
        TrainingTrackerReadService readService,
        ITrainingExcelWorkbookBuilder workbookBuilder,
        IClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool IsFeatureEnabled { get; private set; }

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

    public TrainingKpiDto Kpis { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        IsFeatureEnabled = _options.Value.Enabled;

        await LoadOptionsAsync(cancellationToken);

        BackfillExportDefaultsFromFilter();

        if (!IsFeatureEnabled)
        {
            Trainings = Array.Empty<TrainingRowViewModel>();
            return Page();
        }

        var query = BuildQuery(Filter);
        var results = await _readService.SearchAsync(query, cancellationToken);
        Trainings = results.Select(TrainingRowViewModel.FromListItem).ToList();
        Kpis = await _readService.GetKpisAsync(query, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        IsFeatureEnabled = _options.Value.Enabled;
        if (!IsFeatureEnabled)
        {
            return Forbid();
        }

        BackfillExportDefaultsFromFilter();

        var query = BuildExportQuery(Export);
        var rows = await _readService.ExportAsync(query, Export.IncludeRoster, cancellationToken);

        var trainingTypeName = await ResolveTrainingTypeNameAsync(Export.TypeId, cancellationToken);
        var (technicalCategoryName, technicalCategoryDisplayName) = await ResolveTechnicalCategoryMetadataAsync(
            Export.ProjectTechnicalCategoryId,
            cancellationToken);
        var categoryDisplayName = GetCategoryDisplayName(Export.Category);

        var workbook = _workbookBuilder.Build(new TrainingExcelWorkbookContext(
            rows,
            _clock.UtcNow,
            query.From,
            query.To,
            query.Search,
            Export.IncludeRoster,
            trainingTypeName,
            categoryDisplayName,
            technicalCategoryName,
            technicalCategoryDisplayName));

        var fileName = $"training-tracker-{_clock.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        return File(workbook, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

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

    private static TrainingTrackerQuery BuildExportQuery(ExportInput export)
    {
        var query = new TrainingTrackerQuery
        {
            ProjectTechnicalCategoryId = export.ProjectTechnicalCategoryId,
            From = export.From,
            To = export.To,
            Search = string.IsNullOrWhiteSpace(export.Search) ? null : export.Search.Trim()
        };

        if (export.Category.HasValue)
        {
            query.Category = export.Category.Value;
        }

        if (export.TypeId is { } typeId && typeId != Guid.Empty)
        {
            query.TrainingTypeIds.Add(typeId);
        }

        return query;
    }

    private void BackfillExportDefaultsFromFilter()
    {
        Export.From ??= Filter.From;
        Export.To ??= Filter.To;
        Export.ProjectTechnicalCategoryId ??= Filter.ProjectTechnicalCategoryId;
        Export.TypeId ??= Filter.TypeId;
        Export.Category ??= Filter.Category;
        Export.Search ??= Filter.Search;
    }

    private async Task<string?> ResolveTrainingTypeNameAsync(Guid? typeId, CancellationToken cancellationToken)
    {
        if (typeId is not { } trainingTypeId || trainingTypeId == Guid.Empty)
        {
            return null;
        }

        var trainingTypes = await _readService.GetTrainingTypesAsync(cancellationToken);
        return trainingTypes.FirstOrDefault(option => option.Id == trainingTypeId)?.Name;
    }

    private async Task<(string? Name, string? DisplayName)> ResolveTechnicalCategoryMetadataAsync(
        int? technicalCategoryId,
        CancellationToken cancellationToken)
    {
        if (!technicalCategoryId.HasValue)
        {
            return (null, null);
        }

        var categories = await _readService.GetProjectTechnicalCategoryOptionsAsync(cancellationToken);
        var selected = categories.FirstOrDefault(option => option.Id == technicalCategoryId.Value);
        var options = BuildTechnicalCategoryOptions(categories, technicalCategoryId);
        var displayName = options.FirstOrDefault(option => option.Selected)?.Text;

        return (selected?.Name, displayName);
    }

    private static string? GetCategoryDisplayName(TrainingCategory? category)
        => category switch
        {
            TrainingCategory.Officer => "Officers",
            TrainingCategory.JuniorCommissionedOfficer => "Junior Commissioned Officers",
            TrainingCategory.OtherRank => "Other Ranks",
            _ => null
        };

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
        public static TrainingRowViewModel FromListItem(TrainingListItem item)
        {
            var strength = $"{item.CounterOfficers:N0} – {item.CounterJcos:N0} – {item.CounterOrs:N0}";
            var period = FormatPeriod(item);
            return new TrainingRowViewModel(
                item.Id,
                item.TrainingTypeName,
                period,
                strength,
                item.CounterTotal,
                item.CounterSource,
                item.Notes,
                item.ProjectNames);
        }

        private static string FormatPeriod(TrainingListItem item)
        {
            if (item.StartDate.HasValue || item.EndDate.HasValue)
            {
                var start = item.StartDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "(not set)";
                var end = item.EndDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? start;
                return start == end ? start : $"{start} – {end}";
            }

            if (item.TrainingYear.HasValue && item.TrainingMonth.HasValue)
            {
                var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(item.TrainingMonth.Value);
                return $"{monthName} {item.TrainingYear.Value}";
            }

            return "(unspecified)";
        }
    }
}
