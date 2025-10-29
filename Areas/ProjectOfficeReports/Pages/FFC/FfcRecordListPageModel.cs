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

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public string CurrentSort { get; private set; } = "year";

    public string CurrentSortDirection { get; private set; } = "desc";

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    protected async Task LoadRecordsAsync()
    {
        Query = string.IsNullOrWhiteSpace(Query) ? null : Query!.Trim();

        var queryable = Db.FfcRecords
            .Include(x => x.Country)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.ToLowerInvariant();
            var hasYear = short.TryParse(Query, out var year);

            queryable = queryable.Where(x =>
                x.Country.Name.ToLower().Contains(term) ||
                (hasYear && x.Year == year));
        }

        var sort = (Sort ?? string.Empty).Trim().ToLowerInvariant();
        var descending = !string.Equals(SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        queryable = sort switch
        {
            "country" => descending
                ? queryable.OrderByDescending(x => x.Country.Name).ThenByDescending(x => x.Year)
                : queryable.OrderBy(x => x.Country.Name).ThenByDescending(x => x.Year),
            _ => descending
                ? queryable.OrderByDescending(x => x.Year).ThenBy(x => x.Country.Name)
                : queryable.OrderBy(x => x.Year).ThenBy(x => x.Country.Name)
        };

        CurrentSort = sort switch
        {
            "country" => "country",
            _ => "year"
        };

        CurrentSortDirection = descending ? "desc" : "asc";

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
            .AsNoTracking()
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }

    public Dictionary<string, string?> BuildRoute(int? page = null, string? sort = null, string? dir = null, string? query = null)
    {
        var effectiveQuery = query ?? Query;
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

        return values;
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
}
