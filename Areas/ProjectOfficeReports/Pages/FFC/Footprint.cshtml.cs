using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Ffc.Presentation;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public sealed class FootprintModel : PageModel
{
    private readonly IFfcFootprintService _footprintService;
    private readonly IFfcPowerPointExportService _powerPointExportService;
    private readonly ILogger<FootprintModel> _logger;

    public FootprintModel(
        IFfcFootprintService footprintService,
        IFfcPowerPointExportService powerPointExportService,
        ILogger<FootprintModel> logger)
    {
        _footprintService = footprintService ?? throw new ArgumentNullException(nameof(footprintService));
        _powerPointExportService = powerPointExportService ?? throw new ArgumentNullException(nameof(powerPointExportService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true, Name = "view")]
    public string? ViewMode { get; set; }

    [BindProperty(SupportsGet = true, Name = "year")]
    public short? Year { get; set; }

    [BindProperty(SupportsGet = true, Name = "countryId")]
    public long? CountryId { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "metric")]
    public string? MetricValue { get; set; }

    [BindProperty(SupportsGet = true, Name = "sort")]
    public string? SortValue { get; set; }

    [BindProperty(SupportsGet = true, Name = "selectedCountryId")]
    public long? SelectedCountryId { get; set; }

    [BindProperty]
    public FfcPowerPointExportInput PowerPoint { get; set; } = new();

    public FfcFootprintResult Result { get; private set; } = FfcFootprintResult.Empty();

    public FfcFootprintMetric Metric { get; private set; } = FfcFootprintMetric.TotalUnits;

    public FfcFootprintSort Sort { get; private set; } = FfcFootprintSort.TotalUnits;

    public bool IsMapView => string.Equals(ViewMode, "map", StringComparison.OrdinalIgnoreCase);

    public bool IsCardsView => !IsMapView;

    public bool HasActiveFilters => Year.HasValue || CountryId.HasValue || !string.IsNullOrWhiteSpace(Query);

    public DateTimeOffset PositionDate => Result.Countries.Count == 0
        ? DateTimeOffset.Now
        : Result.Countries.Max(country => country.LastUpdated);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeRequest();

        ViewData["Title"] = "FFC Proposals – Global footprint";
        ViewData["UseFullWidth"] = true;
        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" })),
            ("Global footprint", null));

        Result = await _footprintService.GetAsync(
            new FfcFootprintRequest(
                Year: Year,
                CountryId: CountryId,
                Search: Query,
                Metric: Metric,
                Sort: Sort),
            cancellationToken);

        if (SelectedCountryId.HasValue && Result.Countries.All(country => country.CountryId != SelectedCountryId.Value))
        {
            SelectedCountryId = null;
        }

        InitialisePowerPointInput();
    }

    public async Task<IActionResult> OnPostExportPowerPointAsync(CancellationToken cancellationToken)
    {
        var reference = $"FFC-PPT-{HttpContext.TraceIdentifier}";
        try
        {
            var request = BuildPowerPointRequest();
            var result = await _powerPointExportService.GenerateAsync(request, cancellationToken);
            Response.Headers["X-FFC-Presentation-Slides"] = result.SlideCount.ToString(CultureInfo.InvariantCulture);
            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (ArgumentException exception)
        {
            return ExportProblem(StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return ExportProblem(StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "FFC PowerPoint export failed. Reference {Reference}.", reference);
            return ExportProblem(
                StatusCodes.Status500InternalServerError,
                $"The PowerPoint could not be prepared. Reference: {reference}");
        }
    }

    public Dictionary<string, string?> BuildRoute(
        string? view = null,
        string? metric = null,
        string? sort = null,
        long? selectedCountryId = null,
        bool clearSelectedCountry = false,
        bool clearFilters = false)
    {
        var route = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["view"] = view ?? ViewMode,
            ["year"] = clearFilters ? null : Year?.ToString(CultureInfo.InvariantCulture),
            ["countryId"] = clearFilters ? null : CountryId?.ToString(CultureInfo.InvariantCulture),
            ["q"] = clearFilters || string.IsNullOrWhiteSpace(Query) ? null : Query.Trim(),
            ["metric"] = metric ?? MetricValue,
            ["sort"] = sort ?? SortValue,
            ["selectedCountryId"] = clearSelectedCountry
                ? null
                : (selectedCountryId ?? SelectedCountryId)?.ToString(CultureInfo.InvariantCulture)
        };

        return route
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static string MetricQueryValue(FfcFootprintMetric metric) => metric switch
    {
        FfcFootprintMetric.Installed => "installed",
        FfcFootprintMetric.Delivered => "delivered",
        FfcFootprintMetric.Planned => "planned",
        _ => "total"
    };

    public static string MetricLabel(FfcFootprintMetric metric) => metric switch
    {
        FfcFootprintMetric.Installed => "Installed units",
        FfcFootprintMetric.Delivered => "Delivered, awaiting installation",
        FfcFootprintMetric.Planned => "Planned units",
        _ => "Total units"
    };

    public static string SortQueryValue(FfcFootprintSort sort) => sort switch
    {
        FfcFootprintSort.CountryName => "country",
        FfcFootprintSort.InstalledUnits => "installed",
        FfcFootprintSort.PlannedUnits => "planned",
        FfcFootprintSort.MostRecentActivity => "recent",
        _ => "total"
    };

    private void NormalizeRequest()
    {
        ViewMode = string.Equals(ViewMode, "cards", StringComparison.OrdinalIgnoreCase)
            ? "cards"
            : "map";

        Metric = (MetricValue ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "installed" => FfcFootprintMetric.Installed,
            "delivered" => FfcFootprintMetric.Delivered,
            "planned" => FfcFootprintMetric.Planned,
            _ => FfcFootprintMetric.TotalUnits
        };
        MetricValue = MetricQueryValue(Metric);

        Sort = (SortValue ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "country" => FfcFootprintSort.CountryName,
            "installed" => FfcFootprintSort.InstalledUnits,
            "planned" => FfcFootprintSort.PlannedUnits,
            "recent" => FfcFootprintSort.MostRecentActivity,
            _ => FfcFootprintSort.TotalUnits
        };
        SortValue = SortQueryValue(Sort);

        Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();
    }

    private void InitialisePowerPointInput()
    {
        PowerPoint = new FfcPowerPointExportInput
        {
            Scope = "current",
            PresentationType = "executive",
            IncludeProjects = true,
            IncludeProgress = true,
            Title = "FFC Global Portfolio",
            Subtitle = $"Position as at {PositionDate:dd MMM yyyy}",
            CurrentYear = Year,
            CurrentCountryId = CountryId,
            CurrentSearch = Query
        };
    }

    private FfcPowerPointExportRequest BuildPowerPointRequest()
    {
        var scope = PowerPoint.Scope?.Trim().ToLowerInvariant() switch
        {
            "complete" => FfcExportScope.CompletePortfolio,
            "selected" => FfcExportScope.SelectedCountries,
            _ => FfcExportScope.CurrentFilteredPortfolio
        };
        var presentationType = string.Equals(
            PowerPoint.PresentationType,
            "full",
            StringComparison.OrdinalIgnoreCase)
            ? FfcPresentationType.FullPortfolio
            : FfcPresentationType.ExecutiveBrief;

        var includePortfolioDetails = presentationType == FfcPresentationType.FullPortfolio;
        var includeProjects = includePortfolioDetails && PowerPoint.IncludeProjects;

        return new FfcPowerPointExportRequest(
            scope,
            presentationType,
            scope == FfcExportScope.CompletePortfolio ? null : PowerPoint.CurrentYear,
            scope == FfcExportScope.CurrentFilteredPortfolio ? PowerPoint.CurrentCountryId : null,
            scope == FfcExportScope.CompletePortfolio ? null : PowerPoint.CurrentSearch,
            PowerPoint.SelectedCountryIds?.ToArray() ?? Array.Empty<long>(),
            includeProjects,
            includeProjects && PowerPoint.IncludeProgress,
            includePortfolioDetails && PowerPoint.IncludeMilestoneRemarks,
            includePortfolioDetails && PowerPoint.IncludeAttachmentRegister,
            PowerPoint.Title?.Trim() ?? string.Empty,
            PowerPoint.Subtitle?.Trim(),
            PowerPoint.HandlingMarking?.Trim(),
            DateTimeOffset.Now);
    }

    private JsonResult ExportProblem(int statusCode, string message)
        => new(new
        {
            title = "PowerPoint export could not be completed",
            detail = message
        })
        {
            StatusCode = statusCode
        };

    public sealed class FfcPowerPointExportInput
    {
        public string Scope { get; set; } = "current";
        public string PresentationType { get; set; } = "executive";
        public short? CurrentYear { get; set; }
        public long? CurrentCountryId { get; set; }
        public string? CurrentSearch { get; set; }
        public List<long> SelectedCountryIds { get; set; } = new();
        public bool IncludeProjects { get; set; } = true;
        public bool IncludeProgress { get; set; } = true;
        public bool IncludeMilestoneRemarks { get; set; }
        public bool IncludeAttachmentRegister { get; set; }

        [Required]
        [StringLength(120)]
        public string? Title { get; set; }

        [StringLength(180)]
        public string? Subtitle { get; set; }

        [StringLength(80)]
        public string? HandlingMarking { get; set; }
    }
}
