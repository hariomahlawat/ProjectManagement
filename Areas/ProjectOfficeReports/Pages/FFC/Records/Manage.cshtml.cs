using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records;

[Authorize]
public class ManageModel(ApplicationDbContext db) : PageModel
{
    private readonly ApplicationDbContext _db = db;

    public IList<FfcRecord> Records { get; private set; } = [];
    private bool CanManageRecords => User.IsInRole("Admin") || User.IsInRole("HoD");
    public SelectList CountrySelect { get; private set; } = default!;
    public bool IsEditMode => EditId.HasValue;

    [FromQuery] public long? EditId { get; set; }

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public long? Id { get; set; }
        public long CountryId { get; set; }
        public short Year { get; set; }

        public bool IpaYes { get; set; }
        [DataType(DataType.Date)] public DateOnly? IpaDate { get; set; }
        public string? IpaRemarks { get; set; }

        public bool GslYes { get; set; }
        [DataType(DataType.Date)] public DateOnly? GslDate { get; set; }
        public string? GslRemarks { get; set; }

        public bool DeliveryYes { get; set; }
        [DataType(DataType.Date)] public DateOnly? DeliveryDate { get; set; }
        public string? DeliveryRemarks { get; set; }

        public bool InstallationYes { get; set; }
        [DataType(DataType.Date)] public DateOnly? InstallationDate { get; set; }
        public string? InstallationRemarks { get; set; }

        public string? OverallRemarks { get; set; }
        public bool IsDeleted { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadPageAsync();
        if (EditId.HasValue)
        {
            var r = await _db.FfcRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == EditId.Value);
            if (r is null) return NotFound();
            Input = MapToInput(r);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!CanManageRecords) return Forbid();

        await ValidateAsync(Input);
        if (!ModelState.IsValid)
        {
            await LoadPageAsync();
            return Page();
        }

        var entity = MapToEntity(Input, new FfcRecord());
        _db.FfcRecords.Add(entity);
        await _db.SaveChangesAsync();

        TempData["StatusMessage"] = "Record created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (!CanManageRecords) return Forbid();

        if (Input.Id is null) return BadRequest();
        await ValidateAsync(Input);
        if (!ModelState.IsValid)
        {
            await LoadPageAsync(Input.Id);
            return Page();
        }

        var entity = await _db.FfcRecords.FirstOrDefaultAsync(x => x.Id == Input.Id);
        if (entity is null) return NotFound();

        MapToEntity(Input, entity);
        await _db.SaveChangesAsync();

        TempData["StatusMessage"] = "Record updated.";
        return RedirectToPage();
    }

    private async Task LoadPageAsync(long? keepEditId = null)
    {
        Records = await _db.FfcRecords
            .Include(x => x.Country)
            .OrderByDescending(x => x.Year)
            .ThenBy(x => x.Country.Name)
            .AsNoTracking()
            .ToListAsync();

        var countries = await _db.FfcCountries
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        CountrySelect = new SelectList(countries, "Id", "Name");
        EditId = keepEditId;
    }

    private static InputModel MapToInput(FfcRecord r) => new()
    {
        Id = r.Id,
        CountryId = r.CountryId,
        Year = r.Year,
        IpaYes = r.IpaYes,
        IpaDate = r.IpaDate,
        IpaRemarks = r.IpaRemarks,
        GslYes = r.GslYes,
        GslDate = r.GslDate,
        GslRemarks = r.GslRemarks,
        DeliveryYes = r.DeliveryYes,
        DeliveryDate = r.DeliveryDate,
        DeliveryRemarks = r.DeliveryRemarks,
        InstallationYes = r.InstallationYes,
        InstallationDate = r.InstallationDate,
        InstallationRemarks = r.InstallationRemarks,
        OverallRemarks = r.OverallRemarks,
        IsDeleted = r.IsDeleted
    };

    private static FfcRecord MapToEntity(InputModel i, FfcRecord e)
    {
        e.CountryId = i.CountryId;
        e.Year = i.Year;
        e.IpaYes = i.IpaYes;
        e.IpaDate = i.IpaDate;
        e.IpaRemarks = i.IpaRemarks;
        e.GslYes = i.GslYes;
        e.GslDate = i.GslDate;
        e.GslRemarks = i.GslRemarks;
        e.DeliveryYes = i.DeliveryYes;
        e.DeliveryDate = i.DeliveryDate;
        e.DeliveryRemarks = i.DeliveryRemarks;
        e.InstallationYes = i.InstallationYes;
        e.InstallationDate = i.InstallationDate;
        e.InstallationRemarks = i.InstallationRemarks;
        e.OverallRemarks = i.OverallRemarks;
        e.IsDeleted = i.IsDeleted;
        return e;
    }

    private async Task ValidateAsync(InputModel i)
    {
        if (!await _db.FfcCountries.AnyAsync(c => c.Id == i.CountryId && c.IsActive))
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.CountryId), "Select a valid active country.");

        if (i.Year is < 2000 or > 2100)
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.Year), "Year must be between 2000 and 2100.");

        if (i.IpaDate.HasValue && !i.IpaYes)
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.IpaDate), "IPA date requires IPA = Yes.");
        if (i.GslDate.HasValue && !i.GslYes)
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.GslDate), "GSL date requires GSL = Yes.");
        if (i.DeliveryDate.HasValue && !i.DeliveryYes)
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.DeliveryDate), "Delivery date requires Delivery = Yes.");
        if (i.InstallationDate.HasValue && !i.InstallationYes)
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.InstallationDate), "Installation date requires Installation = Yes.");
    }

    public async Task<IActionResult> OnGetEditAsync(long id)
    {
        EditId = id;
        return await OnGetAsync();
    }
}
