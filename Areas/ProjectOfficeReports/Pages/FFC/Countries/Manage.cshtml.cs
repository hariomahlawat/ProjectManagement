using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

    public IList<FfcCountry> Countries { get; private set; } = [];
    private bool CanManageCountries => User.IsInRole("Admin") || User.IsInRole("HoD");

    public bool IsEditMode => EditId.HasValue;

    [FromQuery]
    public long? EditId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public long? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? IsoCode { get; set; }
        public bool IsActive { get; set; } = true;
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Countries = await _db.FfcCountries
            .OrderBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync();

        if (EditId.HasValue)
        {
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
                RowVersion = entity.RowVersion
            };
        }

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
            return await ReloadPageAsync();
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
        return RedirectToPage();
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

        if (Input.RowVersion is null || Input.RowVersion.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "The country information was outdated. Please reload the page and try again.");
            return await ReloadPageAsync(Input.Id);
        }

        await ValidateInputAsync(Input, isEdit: true);
        if (!ModelState.IsValid)
        {
            return await ReloadPageAsync(Input.Id);
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

        entity.Name = normalizedName;
        entity.IsoCode = normalizedIso;
        entity.IsActive = Input.IsActive;

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = Input.RowVersion;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(string.Empty, "The country was modified by another user. The latest values are now loaded—please review and try again.");

            var databaseEntity = await _db.FfcCountries
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == entity.Id);

            if (databaseEntity is not null)
            {
                Input = new InputModel
                {
                    Id = databaseEntity.Id,
                    Name = databaseEntity.Name,
                    IsoCode = databaseEntity.IsoCode,
                    IsActive = databaseEntity.IsActive,
                    RowVersion = databaseEntity.RowVersion
                };
                return await ReloadPageAsync(Input.Id);
            }

            return await ReloadPageAsync();
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
        return RedirectToPage();
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
        return RedirectToPage();
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
            input.IsoCode = trimmedIso;
            if (trimmedIso.Length != 3)
            {
                ModelState.AddModelError(nameof(Input) + "." + nameof(Input.IsoCode), "ISO code must be exactly 3 characters.");
            }
            else if (!Regex.IsMatch(trimmedIso, "^[A-Za-z]{3}$"))
            {
                ModelState.AddModelError(nameof(Input) + "." + nameof(Input.IsoCode), "ISO code must be three letters.");
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

    private async Task<PageResult> ReloadPageAsync(long? keepEditingId = null)
    {
        Countries = await _db.FfcCountries
            .OrderBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync();

        EditId = keepEditingId;
        return Page();
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
