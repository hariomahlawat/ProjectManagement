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

    public IReadOnlyList<SelectListItem> TrainingTypes { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> ProjectOptions { get; private set; } = Array.Empty<SelectListItem>();

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

        Filter.TypeIds ??= Array.Empty<Guid>();

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

        var query = BuildQuery(Filter);
        var rows = await _readService.ExportAsync(query, cancellationToken);

        var workbook = _workbookBuilder.Build(new TrainingExcelWorkbookContext(
            rows,
            _clock.UtcNow,
            Filter.From,
            Filter.To,
            Filter.Search));

        var fileName = $"training-tracker-{_clock.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        return File(workbook, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var selectedTypeIds = Filter.TypeIds is { Length: > 0 }
            ? new HashSet<Guid>(Filter.TypeIds.Where(id => id != Guid.Empty))
            : new HashSet<Guid>();

        TrainingTypes = (await _readService.GetTrainingTypesAsync(cancellationToken))
            .Select(option => new SelectListItem(option.Name, option.Id.ToString())
            {
                Selected = selectedTypeIds.Contains(option.Id)
            })
            .ToList();

        ProjectOptions = (await _readService.GetProjectOptionsAsync(cancellationToken))
            .Select(option => new SelectListItem(option.Name, option.Id.ToString()))
            .ToList();
    }

    private static TrainingTrackerQuery BuildQuery(FilterInput filter)
    {
        var query = new TrainingTrackerQuery
        {
            ProjectId = filter.ProjectId,
            From = filter.From,
            To = filter.To,
            Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim()
        };

        if (filter.Category.HasValue)
        {
            query.Category = filter.Category.Value;
        }

        if (filter.TypeIds is { Length: > 0 })
        {
            foreach (var id in filter.TypeIds.Where(id => id != Guid.Empty))
            {
                query.TrainingTypeIds.Add(id);
            }
        }

        return query;
    }

    public sealed class FilterInput
    {
        [Display(Name = "Training types")]
        public Guid[]? TypeIds { get; set; }

        [Display(Name = "Project")]
        public int? ProjectId { get; set; }

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
