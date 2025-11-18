using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

public enum MilestoneFilterState
{
    Any,
    Completed,
    Pending
}

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

    [FromQuery(Name = "ipa")] public MilestoneFilterState IpaStatus { get; set; } = MilestoneFilterState.Any;

    [FromQuery(Name = "gsl")] public MilestoneFilterState GslStatus { get; set; } = MilestoneFilterState.Any;

    [FromQuery(Name = "delivery")] public MilestoneFilterState DeliveryStatus { get; set; } = MilestoneFilterState.Any;

    [FromQuery(Name = "installation")] public MilestoneFilterState InstallationStatus { get; set; } = MilestoneFilterState.Any;

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public string CurrentSort { get; private set; } = "year";

    public string CurrentSortDirection { get; private set; } = "desc";

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public bool HasActiveFilters =>
        Year.HasValue ||
        CountryId.HasValue ||
        IpaStatus != MilestoneFilterState.Any ||
        GslStatus != MilestoneFilterState.Any ||
        DeliveryStatus != MilestoneFilterState.Any ||
        InstallationStatus != MilestoneFilterState.Any ||
        HasQuery;

    protected async Task LoadRecordsAsync()
    {
        Query = string.IsNullOrWhiteSpace(Query) ? null : Query!.Trim();

        var queryable = Db.FfcRecords
            .Include(x => x.Country)
            .Include(x => x.Projects)
                .ThenInclude(p => p.LinkedProject)
            .Include(x => x.Attachments)
            .AsQueryable();

        queryable = ApplyRecordFilters(ApplyMilestoneFilters(ApplyScalarFilters(queryable)));

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.ToLowerInvariant();
            var hasYear = short.TryParse(Query, out var year);

            queryable = queryable.Where(x =>
                x.Country.Name.ToLower().Contains(term) ||
                (hasYear && x.Year == year));
        }

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

        Records = await queryable
            .AsSplitQuery()
            .AsNoTracking()
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }

    protected virtual IQueryable<FfcRecord> ApplyRecordFilters(IQueryable<FfcRecord> queryable) => queryable;

    public Dictionary<string, string?> BuildRoute(
        int? page = null,
        string? sort = null,
        string? dir = null,
        string? query = null,
        short? year = null,
        long? countryId = null,
        MilestoneFilterState? ipa = null,
        MilestoneFilterState? gsl = null,
        MilestoneFilterState? delivery = null,
        MilestoneFilterState? installation = null)
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

        AddMilestoneRouteValue(values, "ipa", effectiveIpa);
        AddMilestoneRouteValue(values, "gsl", effectiveGsl);
        AddMilestoneRouteValue(values, "delivery", effectiveDelivery);
        AddMilestoneRouteValue(values, "installation", effectiveInstallation);

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
                ? queryable.OrderByDescending(x => x.Country.Name).ThenByDescending(x => x.Year)
                : queryable.OrderBy(x => x.Country.Name).ThenByDescending(x => x.Year),
            _ => descending
                ? queryable.OrderByDescending(x => x.Year).ThenBy(x => x.Country.Name)
                : queryable.OrderBy(x => x.Year).ThenBy(x => x.Country.Name)
        };
    }

    public string GetSortDirectionFor(string column)
    {
        if (string.Equals(CurrentSort, column, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(CurrentSortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
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

    private IQueryable<FfcRecord> ApplyScalarFilters(IQueryable<FfcRecord> queryable)
    {
        if (Year.HasValue)
        {
            queryable = queryable.Where(record => record.Year == Year.Value);
        }

        if (CountryId.HasValue)
        {
            queryable = queryable.Where(record => record.CountryId == CountryId.Value);
        }

        return queryable;
    }

    private IQueryable<FfcRecord> ApplyMilestoneFilters(IQueryable<FfcRecord> queryable)
    {
        queryable = ApplyMilestoneFilter(queryable, IpaStatus, record => record.IpaYes);
        queryable = ApplyMilestoneFilter(queryable, GslStatus, record => record.GslYes);
        queryable = ApplyMilestoneFilter(queryable, DeliveryStatus, record => record.Projects.Any(project => project.IsDelivered));
        queryable = ApplyMilestoneFilter(queryable, InstallationStatus, record => record.Projects.Any(project => project.IsInstalled));
        return queryable;
    }

    private static IQueryable<FfcRecord> ApplyMilestoneFilter(
        IQueryable<FfcRecord> queryable,
        MilestoneFilterState state,
        Expression<Func<FfcRecord, bool>> predicate)
    {
        return state switch
        {
            MilestoneFilterState.Completed => queryable.Where(predicate),
            MilestoneFilterState.Pending => queryable.Where(Negate(predicate)),
            _ => queryable
        };
    }

    private static void AddMilestoneRouteValue(
        IDictionary<string, string?> values,
        string key,
        MilestoneFilterState state)
    {
        if (state == MilestoneFilterState.Any)
        {
            values.Remove(key);
            return;
        }

        values[key] = state.ToString().ToLowerInvariant();
    }

    private static Expression<Func<FfcRecord, bool>> Negate(Expression<Func<FfcRecord, bool>> predicate)
    {
        var parameter = predicate.Parameters[0];
        var negatedBody = Expression.Not(predicate.Body);
        return Expression.Lambda<Func<FfcRecord, bool>>(negatedBody, parameter);
    }
}
