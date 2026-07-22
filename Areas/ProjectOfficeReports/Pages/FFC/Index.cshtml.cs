using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public sealed class IndexModel : PageModel
{
    public const int PortfolioPageSize = 25;

    private readonly ApplicationDbContext _db;
    private readonly IFfcPortfolioService _portfolioService;

    public IndexModel(
        ApplicationDbContext db,
        IFfcPortfolioService portfolioService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _portfolioService = portfolioService ?? throw new ArgumentNullException(nameof(portfolioService));
    }

    [FromQuery(Name = "q")]
    public string? Query { get; set; }

    [FromQuery(Name = "page")]
    public int PageNumber { get; set; } = 1;

    [FromQuery(Name = "year")]
    public short? Year { get; set; }

    [FromQuery(Name = "countryId")]
    public long? CountryId { get; set; }

    [FromQuery(Name = "ipa")]
    public FfcFilterState IpaStatus { get; set; } = FfcFilterState.Any;

    [FromQuery(Name = "gsl")]
    public FfcFilterState GslStatus { get; set; } = FfcFilterState.Any;

    [FromQuery(Name = "delivery")]
    public FfcFilterState DeliveryStatus { get; set; } = FfcFilterState.Any;

    [FromQuery(Name = "installation")]
    public FfcFilterState InstallationStatus { get; set; } = FfcFilterState.Any;

    public bool CanManageRecords { get; private set; }

    public FfcPortfolioPageResult Portfolio { get; private set; } = new(
        FfcPortfolioSummary.Empty,
        Array.Empty<FfcPortfolioRecordRow>(),
        0,
        1,
        PortfolioPageSize);

    public IReadOnlyList<SelectListItem> CountryOptions { get; private set; }
        = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> YearOptions { get; private set; }
        = Array.Empty<SelectListItem>();

    public IReadOnlyList<FilterOption> MilestoneOptions { get; } =
    [
        new(FfcFilterState.Any, "Any status"),
        new(FfcFilterState.Completed, "Completed"),
        new(FfcFilterState.Pending, "Pending")
    ];

    public IReadOnlyList<FilterOption> ProgressOptions { get; } =
    [
        new(FfcFilterState.Any, "Any status"),
        new(FfcFilterState.Completed, "Completed"),
        new(FfcFilterState.Partial, "Partial"),
        new(FfcFilterState.Pending, "Pending")
    ];

    public IReadOnlyList<FfcPortfolioRecordRow> Records => Portfolio.Records;

    public FfcPortfolioSummary Summary => Portfolio.Summary;

    public int TotalCount => Portfolio.TotalRecordCount;

    public int TotalPages => Portfolio.TotalPages;

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public bool HasAdvancedFilters =>
        IpaStatus != FfcFilterState.Any ||
        GslStatus != FfcFilterState.Any ||
        DeliveryStatus != FfcFilterState.Any ||
        InstallationStatus != FfcFilterState.Any;

    public bool HasActiveFilters =>
        HasQuery ||
        Year.HasValue ||
        CountryId.HasValue ||
        HasAdvancedFilters;

    public string? SelectedCountryLabel => CountryOptions
        .FirstOrDefault(option => option.Selected)?.Text;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeRequest();
        CanManageRecords = User.IsInRole("Admin") || User.IsInRole("HoD");

        await LoadFilterOptionsAsync(cancellationToken);

        Portfolio = await _portfolioService.GetPageAsync(
            new FfcPortfolioPageRequest(
                BuildPortfolioFilter(),
                PageNumber,
                PortfolioPageSize),
            cancellationToken);

        PageNumber = Portfolio.PageNumber;
        FfcBreadcrumbs.Set(ViewData, ("FFC Proposals", null));
    }

    public Dictionary<string, string?> BuildRoute(
        int? page = null,
        string? remove = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var targetPage = Math.Max(1, page ?? PageNumber);

        if (targetPage > 1)
        {
            values["page"] = targetPage.ToString(CultureInfo.InvariantCulture);
        }

        if (!IsRemoved(remove, "q") && !string.IsNullOrWhiteSpace(Query))
        {
            values["q"] = Query;
        }

        if (!IsRemoved(remove, "year") && Year.HasValue)
        {
            values["year"] = Year.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!IsRemoved(remove, "countryId") && CountryId.HasValue)
        {
            values["countryId"] = CountryId.Value.ToString(CultureInfo.InvariantCulture);
        }

        AddFilterRouteValue(values, "ipa", IpaStatus, remove);
        AddFilterRouteValue(values, "gsl", GslStatus, remove);
        AddFilterRouteValue(values, "delivery", DeliveryStatus, remove);
        AddFilterRouteValue(values, "installation", InstallationStatus, remove);

        return values;
    }

    public string GetFilterLabel(FfcFilterState state, bool allowsPartial)
    {
        var options = allowsPartial ? ProgressOptions : MilestoneOptions;
        return options.FirstOrDefault(option => option.Value == state)?.Label ?? state.ToString();
    }

    private FfcPortfolioFilter BuildPortfolioFilter()
        => new(
            Query: Query,
            Year: Year,
            CountryId: CountryId,
            IpaStatus: IpaStatus,
            GslStatus: GslStatus,
            DeliveryStatus: DeliveryStatus,
            InstallationStatus: InstallationStatus);

    private async Task LoadFilterOptionsAsync(CancellationToken cancellationToken)
    {
        var activeRecords = _db.FfcRecords
            .AsNoTracking()
            .Where(record => !record.IsDeleted);

        var years = await activeRecords
            .Select(record => record.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        YearOptions = years
            .Select(year => new SelectListItem
            {
                Value = year.ToString(CultureInfo.InvariantCulture),
                Text = year.ToString(CultureInfo.InvariantCulture),
                Selected = Year.HasValue && Year.Value == year
            })
            .ToList();

        var countries = await activeRecords
            .Select(record => new
            {
                record.Country.Id,
                record.Country.Name,
                record.Country.IsoCode
            })
            .Distinct()
            .OrderBy(country => country.Name)
            .ToListAsync(cancellationToken);

        CountryOptions = countries
            .Select(country => new SelectListItem
            {
                Value = country.Id.ToString(CultureInfo.InvariantCulture),
                Text = $"{country.Name} · {country.IsoCode}",
                Selected = CountryId.HasValue && CountryId.Value == country.Id
            })
            .ToList();
    }

    private void NormalizeRequest()
    {
        Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();
        PageNumber = Math.Max(1, PageNumber);

        IpaStatus = NormalizeMilestoneFilter(IpaStatus);
        GslStatus = NormalizeMilestoneFilter(GslStatus);
        DeliveryStatus = NormalizeProgressFilter(DeliveryStatus);
        InstallationStatus = NormalizeProgressFilter(InstallationStatus);
    }

    private static FfcFilterState NormalizeMilestoneFilter(FfcFilterState state)
        => state is FfcFilterState.Completed or FfcFilterState.Pending
            ? state
            : FfcFilterState.Any;

    private static FfcFilterState NormalizeProgressFilter(FfcFilterState state)
        => state is FfcFilterState.Completed or FfcFilterState.Partial or FfcFilterState.Pending
            ? state
            : FfcFilterState.Any;

    private static void AddFilterRouteValue(
        IDictionary<string, string?> values,
        string key,
        FfcFilterState state,
        string? remove)
    {
        if (IsRemoved(remove, key) || state == FfcFilterState.Any)
        {
            return;
        }

        values[key] = state.ToString().ToLowerInvariant();
    }

    private static bool IsRemoved(string? remove, string key)
        => string.Equals(remove, key, StringComparison.OrdinalIgnoreCase);

    public sealed record FilterOption(FfcFilterState Value, string Label)
    {
        public string QueryValue => Value.ToString().ToLowerInvariant();
    }
}
