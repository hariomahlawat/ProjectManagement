using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Countries;

[Authorize(Roles = "Admin,HoD")]
public class ManageModel(ApplicationDbContext db, IAuditService audit, ILogger<ManageModel> logger) : PageModel
{
    private readonly ApplicationDbContext _db = db;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<ManageModel> _logger = logger;

    private const int PageSize = 10;

    public IList<FfcCountry> Countries { get; private set; } = [];

    [FromQuery(Name = "q")]
    public string? Query { get; set; }

    [FromQuery(Name = "p")]
    public int PageNumber { get; set; } = 1;

    [FromQuery(Name = "sort")]
    public string? Sort { get; set; }

    [FromQuery(Name = "dir")]
    public string? SortDirection { get; set; }

    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }
    public string CurrentSort { get; private set; } = "name";
    public string CurrentSortDirection { get; private set; } = "asc";
    public bool IsSortDescending => string.Equals(CurrentSortDirection, "desc", StringComparison.OrdinalIgnoreCase);
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    private bool CanManageCountries => User.IsInRole("Admin") || User.IsInRole("HoD");

    public async Task<IActionResult> OnGetAsync()
    {
        ConfigureBreadcrumb();
        await LoadCountriesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(long id)
    {
        if (!CanManageCountries)
        {
            return Forbid();
        }

        var entity = await _db.FfcCountries.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null)
        {
            return NotFound();
        }

        var previousActive = entity.IsActive;
        entity.IsActive = !previousActive;
        await _db.SaveChangesAsync();

        await TryLogAsync("ProjectOfficeReports.FFC.CountryStatusChanged", new Dictionary<string, string?>
        {
            ["CountryId"] = entity.Id.ToString(),
            ["IsActive.Before"] = previousActive.ToString(),
            ["IsActive.After"] = entity.IsActive.ToString()
        });

        TempData["StatusMessage"] = entity.IsActive ? "Country activated." : "Country deactivated.";
        return RedirectToManage();
    }

    private void ConfigureBreadcrumb()
    {
        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" })),
            ("Manage countries", null));
    }

    private async Task LoadCountriesAsync()
    {
        Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();

        var queryable = _db.FfcCountries.AsQueryable();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.ToLowerInvariant();
            queryable = queryable.Where(x => x.Name.ToLower().Contains(term) || x.IsoCode.ToLower().Contains(term));
        }

        var sort = (Sort ?? string.Empty).Trim().ToLowerInvariant();
        var descending = string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        queryable = sort switch
        {
            "iso" => descending
                ? queryable.OrderByDescending(x => x.IsoCode).ThenBy(x => x.Name)
                : queryable.OrderBy(x => x.IsoCode).ThenBy(x => x.Name),
            "status" => descending
                ? queryable.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
                : queryable.OrderBy(x => x.IsActive).ThenBy(x => x.Name),
            _ => descending
                ? queryable.OrderByDescending(x => x.Name)
                : queryable.OrderBy(x => x.Name)
        };

        CurrentSort = sort switch
        {
            "iso" => "iso",
            "status" => "status",
            _ => "name"
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

        Countries = await queryable
            .AsNoTracking()
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }

    private IActionResult RedirectToManage(Dictionary<string, string?>? routeValues = null)
    {
        var values = routeValues ?? BuildRoute();
        return RedirectToPage("./Manage", new RouteValueDictionary(values));
    }

    public Dictionary<string, string?> BuildRoute(int? p = null, string? sort = null, string? dir = null, string? query = null)
    {
        var effectiveQuery = query ?? Query;
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = (p ?? PageNumber).ToString(CultureInfo.InvariantCulture),
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
            return IsSortDescending ? "asc" : "desc";
        }

        return "asc";
    }

    public string GetSortIconClass(string column)
    {
        if (!string.Equals(CurrentSort, column, StringComparison.OrdinalIgnoreCase))
        {
            return "bi bi-arrow-down-up text-muted";
        }

        return IsSortDescending ? "bi bi-arrow-down" : "bi bi-arrow-up";
    }

    private async Task TryLogAsync(string action, IDictionary<string, string?> data)
    {
        try
        {
            await _audit.LogAsync(
                action,
                userId: User.FindFirstValue(ClaimTypes.NameIdentifier),
                userName: User.Identity?.Name,
                data: data,
                http: HttpContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for action {Action}.", action);
        }
    }
}
