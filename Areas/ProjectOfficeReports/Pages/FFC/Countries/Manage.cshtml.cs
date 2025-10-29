using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Countries;

[Authorize]
public class ManageModel(ApplicationDbContext db, IAuditService audit, ILogger<ManageModel> logger) : PageModel
{
    private readonly ApplicationDbContext _db = db;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<ManageModel> _logger = logger;

    private const int PageSize = 10;

    public IList<FfcCountry> Countries { get; private set; } = [];
    private bool CanManageCountries => User.IsInRole("Admin") || User.IsInRole("HoD");

    public bool IsEditMode => EditId.HasValue;

    [FromQuery]
    public long? EditId { get; set; }

    [FromQuery(Name = "q")]
    public string? Query { get; set; }

    [FromQuery(Name = "page")]
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

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public long? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? IsoCode { get; set; }
        public bool IsActive { get; set; } = true;
        public string? RowVersion { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadCountriesAsync();

        if (!EditId.HasValue)
        {
            return Page();
        }

        var entity = await _db.FfcCountries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == EditId.Value);

        if (entity is null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = entity.Id,
            Name = entity.Name,
            IsoCode = entity.IsoCode,
            IsActive = entity.IsActive,
            RowVersion = Convert.ToBase64String(entity.RowVersion)
        };

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!CanManageCountries)
        {
            return Forbid();
        }

        await ValidateInputAsync(Input, isEdit: false);
        if (!ModelState.IsValid)
        {
            await LoadCountriesAsync();
            return Page();
        }

        var normalizedName = Input.Name?.Trim() ?? string.Empty;
        var normalizedIso = string.IsNullOrWhiteSpace(Input.IsoCode)
            ? null
            : Input.IsoCode!.Trim().ToUpperInvariant();

        var entity = new FfcCountry
        {
            Name = normalizedName,
            IsoCode = normalizedIso,
            IsActive = true
        };

        _db.FfcCountries.Add(entity);
        await _db.SaveChangesAsync();

        await TryLogAsync("ProjectOfficeReports.FFC.CountryCreated", new Dictionary<string, string?>
        {
            ["CountryId"] = entity.Id.ToString(),
            ["Name"] = entity.Name,
            ["IsoCode"] = entity.IsoCode,
            ["IsActive"] = entity.IsActive.ToString()
        });

        TempData["StatusMessage"] = "Country created.";
        return RedirectToPage(new RouteValueDictionary(BuildRoute(page: 1)));
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (!CanManageCountries)
        {
            return Forbid();
        }

        if (Input.Id is null)
        {
            return BadRequest();
        }

        await ValidateInputAsync(Input, isEdit: true);
        if (!ModelState.IsValid)
        {
            await LoadCountriesAsync(Input.Id);
            return Page();
        }

        var entity = await _db.FfcCountries.FirstOrDefaultAsync(x => x.Id == Input.Id);
        if (entity is null)
        {
            return NotFound();
        }

        var originalName = entity.Name;
        var originalIso = entity.IsoCode;
        var originalActive = entity.IsActive;

        var normalizedName = Input.Name?.Trim() ?? string.Empty;
        var normalizedIso = string.IsNullOrWhiteSpace(Input.IsoCode)
            ? null
            : Input.IsoCode!.Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(Input.RowVersion))
        {
            try
            {
                var originalRowVersion = Convert.FromBase64String(Input.RowVersion);
                _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = originalRowVersion;
            }
            catch (FormatException)
            {
                ModelState.AddModelError(string.Empty, "Invalid concurrency token provided.");
                await LoadCountriesAsync(Input.Id);
                return Page();
            }
        }

        entity.Name = normalizedName;
        entity.IsoCode = normalizedIso;
        entity.IsActive = Input.IsActive;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(string.Empty, "The country was updated by someone else. Review the latest values and try again.");
            await _db.Entry(entity).ReloadAsync();

            var refreshed = await _db.FfcCountries
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == Input.Id);

            if (refreshed is not null)
            {
                Input = new InputModel
                {
                    Id = refreshed.Id,
                    Name = refreshed.Name,
                    IsoCode = refreshed.IsoCode,
                    IsActive = refreshed.IsActive,
                    RowVersion = Convert.ToBase64String(refreshed.RowVersion)
                };
            }

            await LoadCountriesAsync(Input.Id);
            return Page();
        }

        await TryLogAsync("ProjectOfficeReports.FFC.CountryUpdated", new Dictionary<string, string?>
        {
            ["CountryId"] = entity.Id.ToString(),
            ["Name.Before"] = originalName,
            ["Name.After"] = entity.Name,
            ["IsoCode.Before"] = originalIso,
            ["IsoCode.After"] = entity.IsoCode,
            ["IsActive.Before"] = originalActive.ToString(),
            ["IsActive.After"] = entity.IsActive.ToString()
        });

        TempData["StatusMessage"] = "Country updated.";
        return RedirectToPage(new RouteValueDictionary(BuildRoute()));
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
        return RedirectToPage(new RouteValueDictionary(BuildRoute()));
    }

    public async Task<IActionResult> OnGetEditAsync(long id)
    {
        EditId = id;
        return await OnGetAsync();
    }

    private async Task ValidateInputAsync(InputModel input, bool isEdit)
    {
        var trimmedName = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.Name), "Name is required.");
        }

        if (!string.IsNullOrWhiteSpace(input.IsoCode))
        {
            var trimmedIso = input.IsoCode!.Trim();
            if (trimmedIso.Length != 3)
            {
                ModelState.AddModelError(nameof(Input) + "." + nameof(Input.IsoCode), "ISO code must be exactly 3 characters.");
            }
            else if (!Regex.IsMatch(trimmedIso, "^[A-Za-z]{3}$"))
            {
                ModelState.AddModelError(nameof(Input) + "." + nameof(Input.IsoCode), "ISO code must contain only letters.");
            }
        }

        if (!string.IsNullOrWhiteSpace(trimmedName))
        {
            var normalized = trimmedName.ToLowerInvariant();
            var query = _db.FfcCountries.Where(x => x.Name.ToLower() == normalized);
            if (isEdit && input.Id is not null)
            {
                query = query.Where(x => x.Id != input.Id.Value);
            }

            if (await query.AnyAsync())
            {
                ModelState.AddModelError(nameof(Input) + "." + nameof(Input.Name), "A country with this name already exists.");
            }
        }
    }

    private async Task LoadCountriesAsync(long? keepEditingId = null)
    {
        Query = string.IsNullOrWhiteSpace(Query) ? null : Query!.Trim();

        var queryable = _db.FfcCountries.AsQueryable();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.ToLowerInvariant();
            queryable = queryable.Where(x => x.Name.ToLower().Contains(term) || (x.IsoCode != null && x.IsoCode.ToLower().Contains(term)));
        }

        var sort = (Sort ?? string.Empty).Trim().ToLowerInvariant();
        var direction = string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        queryable = sort switch
        {
            "iso" => direction
                ? queryable.OrderByDescending(x => x.IsoCode ?? string.Empty).ThenBy(x => x.Name)
                : queryable.OrderBy(x => x.IsoCode ?? string.Empty).ThenBy(x => x.Name),
            "status" => direction
                ? queryable.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
                : queryable.OrderBy(x => x.IsActive).ThenBy(x => x.Name),
            _ =>
                direction
                    ? queryable.OrderByDescending(x => x.Name)
                    : queryable.OrderBy(x => x.Name)
        };

        CurrentSort = sort switch
        {
            "iso" => "iso",
            "status" => "status",
            _ => "name"
        };

        CurrentSortDirection = direction ? "desc" : "asc";

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

        EditId = keepEditingId ?? EditId;
    }

    public Dictionary<string, string?> BuildRoute(int? page = null, string? sort = null, string? dir = null, string? query = null)
    {
        var effectiveQuery = query ?? Query;
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = (page ?? PageNumber).ToString(CultureInfo.InvariantCulture),
            ["sort"] = (sort ?? CurrentSort),
            ["dir"] = (dir ?? CurrentSortDirection),
            ["q"] = effectiveQuery
        };

        if (string.IsNullOrWhiteSpace(effectiveQuery))
        {
            values.Remove("q");
        }

        return values;
    }

    public Dictionary<string, string?> BuildRouteForEdit(long id)
    {
        var values = new Dictionary<string, string?>(BuildRoute())
        {
            ["editId"] = id.ToString(CultureInfo.InvariantCulture)
        };

        return values;
    }

    public string GetSortDirectionFor(string column)
    {
        if (string.Equals(CurrentSort, column, StringComparison.OrdinalIgnoreCase))
        {
            return IsSortDescending ? "asc" : "desc";
        }

        return column == "name" ? "asc" : "asc";
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
