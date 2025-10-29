using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Countries;

[Authorize]
public class ManageModel(ApplicationDbContext db) : PageModel
{
    private readonly ApplicationDbContext _db = db;

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
                IsActive = entity.IsActive
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

        var normalizedName = Input.Name?.Trim() ?? string.Empty;
        var normalizedIso = string.IsNullOrWhiteSpace(Input.IsoCode)
            ? null
            : Input.IsoCode!.Trim().ToUpperInvariant();

        entity.Name = normalizedName;
        entity.IsoCode = normalizedIso;
        entity.IsActive = Input.IsActive;

        await _db.SaveChangesAsync();

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

        entity.IsActive = !entity.IsActive;
        await _db.SaveChangesAsync();

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
            if (trimmedIso.Length != 3)
            {
                ModelState.AddModelError(nameof(Input) + "." + nameof(Input.IsoCode), "ISO code must be exactly 3 characters.");
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
}
