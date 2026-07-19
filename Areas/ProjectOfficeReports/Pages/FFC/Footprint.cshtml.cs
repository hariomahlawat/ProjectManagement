using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public sealed class FootprintModel : PageModel
{
    private readonly IFfcFootprintService _footprintService;

    public FootprintModel(IFfcFootprintService footprintService)
    {
        _footprintService = footprintService ?? throw new ArgumentNullException(nameof(footprintService));
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

    [BindProperty(SupportsGet = true, Name = "focus")]
    public string? Focus { get; set; }

    [BindProperty(SupportsGet = true, Name = "presentation")]
    public bool Presentation { get; set; }

    [BindProperty(SupportsGet = true, Name = "selectedCountryId")]
    public long? SelectedCountryId { get; set; }

    public FfcFootprintResult Result { get; private set; } = FfcFootprintResult.Empty();

    public FfcFootprintMetric Metric { get; private set; } = FfcFootprintMetric.TotalUnits;

    public FfcFootprintSort Sort { get; private set; } = FfcFootprintSort.TotalUnits;

    public bool IsMapView => string.Equals(ViewMode, "map", StringComparison.OrdinalIgnoreCase);

    public bool IsCardsView => !IsMapView;

    public bool HasActiveFilters => Year.HasValue || CountryId.HasValue || !string.IsNullOrWhiteSpace(Query);

    public DateTimeOffset PositionDate => Result.Countries.Count == 0
        ? DateTimeOffset.UtcNow
        : Result.Countries.Max(country => country.LastUpdated);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeRequest();

        ViewData["Title"] = "FFC Proposals – Global footprint";
        ViewData["UseFullWidth"] = true;
        ViewData["PresentationMode"] = Presentation;

        if (!Presentation)
        {
            FfcBreadcrumbs.Set(
                ViewData,
                ("FFC Proposals", Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" })),
                ("Global footprint", null));
        }

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
    }

    public Dictionary<string, string?> BuildRoute(
        string? view = null,
        string? metric = null,
        string? sort = null,
        bool? presentation = null,
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
            ["focus"] = Focus,
            ["presentation"] = (presentation ?? Presentation) ? "true" : null,
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

        Focus = (Focus ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "africa" => "africa",
            "southasia" => "southasia",
            _ => "world"
        };

        Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();
    }
}
