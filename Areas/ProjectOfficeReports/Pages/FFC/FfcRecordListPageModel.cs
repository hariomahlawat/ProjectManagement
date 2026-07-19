using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

public abstract class FfcRecordListPageModel : PageModel
{
    protected const int PageSize = 10;

    protected FfcRecordListPageModel(ApplicationDbContext db)
    {
        Db = db;
    }

    protected ApplicationDbContext Db { get; }

    public IList<FfcRecord> Records { get; protected set; } = [];

    [FromQuery(Name = "q")] public string? Query { get; set; }

    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    [FromQuery(Name = "sort")] public string? Sort { get; set; }

    [FromQuery(Name = "dir")] public string? SortDirection { get; set; }

    [FromQuery(Name = "year")] public short? Year { get; set; }

    [FromQuery(Name = "countryId")] public long? CountryId { get; set; }

    [FromQuery(Name = "ipa")] public FfcFilterState IpaStatus { get; set; } = FfcFilterState.Any;

    [FromQuery(Name = "gsl")] public FfcFilterState GslStatus { get; set; } = FfcFilterState.Any;

    [FromQuery(Name = "delivery")] public FfcFilterState DeliveryStatus { get; set; } = FfcFilterState.Any;

    [FromQuery(Name = "installation")] public FfcFilterState InstallationStatus { get; set; } = FfcFilterState.Any;

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public string CurrentSort { get; private set; } = "year";

    public string CurrentSortDirection { get; private set; } = "desc";

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public bool HasActiveFilters =>
        Year.HasValue ||
        CountryId.HasValue ||
        IpaStatus != FfcFilterState.Any ||
        GslStatus != FfcFilterState.Any ||
        DeliveryStatus != FfcFilterState.Any ||
        InstallationStatus != FfcFilterState.Any ||
        HasQuery;

    protected async Task LoadRecordsAsync()
    {
        NormalizeFilterState();
        Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();

        var queryable = Db.FfcRecords
            .AsNoTracking()
            .AsQueryable();

        queryable = ApplyRecordFilters(queryable);
        queryable = FfcPortfolioQuery.ApplyFilters(queryable, BuildPortfolioFilter());
        queryable = ApplyOrdering(queryable);

        TotalCount = await queryable.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

        if (PageNumber < 1)
        {
            PageNumber = 1;
        }
        else if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        var pageIds = await queryable
            .Select(record => record.Id)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        if (pageIds.Count == 0)
        {
            Records = [];
            return;
        }

        var records = await Db.FfcRecords
            .AsNoTracking()
            .Where(record => pageIds.Contains(record.Id))
            .Include(record => record.Country)
            .Include(record => record.Projects)
                .ThenInclude(project => project.LinkedProject)
            .Include(record => record.Attachments)
            .AsSplitQuery()
            .ToListAsync();

        var recordsById = records.ToDictionary(record => record.Id);
        Records = pageIds
            .Where(recordsById.ContainsKey)
            .Select(id => recordsById[id])
            .ToList();
    }

    protected virtual IQueryable<FfcRecord> ApplyRecordFilters(IQueryable<FfcRecord> queryable)
        => queryable;

    protected FfcPortfolioFilter BuildPortfolioFilter()
        => new(
            Query: Query,
            Year: Year,
            CountryId: CountryId,
            IpaStatus: IpaStatus,
            GslStatus: GslStatus,
            DeliveryStatus: DeliveryStatus,
            InstallationStatus: InstallationStatus);

    public Dictionary<string, string?> BuildRoute(
        int? page = null,
        string? sort = null,
        string? dir = null,
        string? query = null,
        short? year = null,
        long? countryId = null,
        FfcFilterState? ipa = null,
        FfcFilterState? gsl = null,
        FfcFilterState? delivery = null,
        FfcFilterState? installation = null)
    {
        var effectiveQuery = query ?? Query;
        var effectiveYear = year.HasValue ? year : Year;
        var effectiveCountryId = countryId.HasValue ? countryId : CountryId;
        var effectiveIpa = ipa ?? IpaStatus;
        var effectiveGsl = gsl ?? GslStatus;
        var effectiveDelivery = delivery ?? DeliveryStatus;
        var effectiveInstallation = installation ?? InstallationStatus;

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = (page ?? PageNumber).ToString(CultureInfo.InvariantCulture),
            ["sort"] = sort ?? CurrentSort,
            ["dir"] = dir ?? CurrentSortDirection,
            ["q"] = effectiveQuery
        };

        if (string.IsNullOrWhiteSpace(effectiveQuery))
        {
            values.Remove("q");
        }

        if (effectiveYear.HasValue)
        {
            values["year"] = effectiveYear.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (effectiveCountryId.HasValue)
        {
            values["countryId"] = effectiveCountryId.Value.ToString(CultureInfo.InvariantCulture);
        }

        AddFilterRouteValue(values, "ipa", effectiveIpa);
        AddFilterRouteValue(values, "gsl", effectiveGsl);
        AddFilterRouteValue(values, "delivery", effectiveDelivery);
        AddFilterRouteValue(values, "installation", effectiveInstallation);

        return values;
    }

    protected virtual IQueryable<FfcRecord> ApplyOrdering(IQueryable<FfcRecord> queryable)
    {
        var sort = (Sort ?? string.Empty).Trim().ToLowerInvariant();
        var descending = !string.Equals(SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        CurrentSort = sort switch
        {
            "country" => "country",
            _ => "year"
        };

        CurrentSortDirection = descending ? "desc" : "asc";

        return sort switch
        {
            "country" => descending
                ? queryable.OrderByDescending(record => record.Country.Name).ThenByDescending(record => record.Year)
                : queryable.OrderBy(record => record.Country.Name).ThenByDescending(record => record.Year),
            _ => descending
                ? queryable.OrderByDescending(record => record.Year).ThenBy(record => record.Country.Name)
                : queryable.OrderBy(record => record.Year).ThenBy(record => record.Country.Name)
        };
    }

    public string GetSortDirectionFor(string column)
    {
        if (string.Equals(CurrentSort, column, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(CurrentSortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                ? "asc"
                : "desc";
        }

        return column == "year" ? "desc" : "asc";
    }

    public string GetSortIconClass(string column)
    {
        if (!string.Equals(CurrentSort, column, StringComparison.OrdinalIgnoreCase))
        {
            return "bi bi-arrow-down-up text-muted";
        }

        return string.Equals(CurrentSortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? "bi bi-arrow-down"
            : "bi bi-arrow-up";
    }

    private void NormalizeFilterState()
    {
        // IPA and GSL are binary milestones. Partial is meaningful only for
        // delivery and installation, which aggregate multiple project rows.
        if (IpaStatus == FfcFilterState.Partial)
        {
            IpaStatus = FfcFilterState.Any;
        }

        if (GslStatus == FfcFilterState.Partial)
        {
            GslStatus = FfcFilterState.Any;
        }
    }

    private static void AddFilterRouteValue(
        IDictionary<string, string?> values,
        string key,
        FfcFilterState state)
    {
        if (state == FfcFilterState.Any)
        {
            values.Remove(key);
            return;
        }

        values[key] = state.ToString().ToLowerInvariant();
    }
}
