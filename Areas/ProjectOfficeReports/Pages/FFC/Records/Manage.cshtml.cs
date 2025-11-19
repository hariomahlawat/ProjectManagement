using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records;

[Authorize(Roles = "Admin,HoD")]
public class ManageModel : FfcRecordListPageModel
{
    private readonly IAuditService _audit;
    private readonly ILogger<ManageModel> _logger;
    private const string ConcurrencyReloadMessage =
        "The record was modified by another user. The latest values have been loaded; please review and try again.";
    private readonly Dictionary<long, FfcProjectQuantitySummary> _projectSummaryCache = new();

    public bool CanManageRecords => User.IsInRole("Admin") || User.IsInRole("HoD");
    public SelectList CountrySelect { get; private set; } = default!;
    public bool IsEditMode => EditId.HasValue;

    [FromQuery] public long? EditId { get; set; }
    public bool HasActiveCountries { get; private set; }

    [BindProperty] public InputModel Input { get; set; } = new();

    public ManageModel(ApplicationDbContext db, IAuditService audit, ILogger<ManageModel> logger)
        : base(db)
    {
        _audit = audit;
        _logger = logger;
    }

    private void ConfigureBreadcrumb()
    {
        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" })),
            ("Manage records", null));
    }

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

        public string? OverallRemarks { get; set; }
        public bool IsDeleted { get; set; }
        public string? RowVersion { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        ConfigureBreadcrumb();
        var editId = EditId;
        await LoadPageAsync(editId);
        if (editId.HasValue)
        {
            var r = await Db.FfcRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == editId.Value);
            if (r is null) return NotFound();
            Input = MapToInput(r);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        ConfigureBreadcrumb();
        await ValidateAsync(Input);
        if (!ModelState.IsValid)
        {
            await LoadPageAsync();
            return Page();
        }

        var entity = MapToEntity(Input, new FfcRecord());
        Db.FfcRecords.Add(entity);
        await Db.SaveChangesAsync();

        var data = BuildRecordData(entity, "After");
        data["RecordId"] = entity.Id.ToString();
        await TryLogAsync("ProjectOfficeReports.FFC.RecordCreated", data);

        TempData["StatusMessage"] = "Record created.";
        return RedirectToManage(BuildRoute(page: 1));
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        ConfigureBreadcrumb();
        if (Input.Id is null) return BadRequest();
        await ValidateAsync(Input);
        if (!ModelState.IsValid)
        {
            await LoadPageAsync(Input.Id);
            return Page();
        }

        var entity = await Db.FfcRecords.FirstOrDefaultAsync(x => x.Id == Input.Id);
        if (entity is null) return NotFound();

        if (!TryDecodeRowVersion(Input.RowVersion, out var originalRowVersion))
        {
            ModelState.AddModelError(string.Empty, ConcurrencyReloadMessage);
            var exists = await ReloadFormStateAsync(Input.Id);
            if (!exists)
            {
                ModelState.AddModelError(string.Empty, "The record no longer exists.");
            }
            return Page();
        }

        var before = BuildRecordData(entity, "Before");

        Db.Entry(entity).Property(x => x.RowVersion).OriginalValue = originalRowVersion;

        MapToEntity(Input, entity);

        try
        {
            await Db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(string.Empty, ConcurrencyReloadMessage);
            var exists = await ReloadFormStateAsync(Input.Id, entity);
            if (!exists)
            {
                ModelState.AddModelError(string.Empty, "The record no longer exists.");
            }
            return Page();
        }

        var data = new Dictionary<string, string?>(before)
        {
            ["RecordId"] = entity.Id.ToString()
        };

        foreach (var kvp in BuildRecordData(entity, "After"))
        {
            data[kvp.Key] = kvp.Value;
        }

        await TryLogAsync("ProjectOfficeReports.FFC.RecordUpdated", data);

        TempData["StatusMessage"] = "Record updated.";
        return RedirectToManage();
    }

    private async Task LoadPageAsync(long? keepEditId = null)
    {
        await LoadRecordsAsync();
        _projectSummaryCache.Clear();

        var countries = await Db.FfcCountries
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        HasActiveCountries = countries.Count > 0;
        CountrySelect = new SelectList(countries, "Id", "Name");
        EditId = keepEditId;
    }

    public Dictionary<string, string?> BuildRouteForEdit(long id)
    {
        var values = new Dictionary<string, string?>(BuildRoute())
        {
            ["editId"] = id.ToString(CultureInfo.InvariantCulture)
        };

        return values;
    }

    private IActionResult RedirectToManage(Dictionary<string, string?>? routeValues = null)
    {
        var values = routeValues ?? BuildRoute();
        return RedirectToPage("./Manage", new RouteValueDictionary(values));
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
        OverallRemarks = r.OverallRemarks,
        IsDeleted = r.IsDeleted,
        RowVersion = r.RowVersion.Length > 0 ? Convert.ToBase64String(r.RowVersion) : null
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
        e.OverallRemarks = i.OverallRemarks;
        e.IsDeleted = i.IsDeleted;
        return e;
    }

    private static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<bool> ReloadFormStateAsync(long? recordId, FfcRecord? trackedEntity = null)
    {
        if (recordId is not long id)
        {
            Input = new InputModel();
            await LoadPageAsync();
            return false;
        }

        FfcRecord? latest = trackedEntity;

        if (latest is not null)
        {
            var entry = Db.Entry(latest);
            if (entry.State != EntityState.Detached)
            {
                try
                {
                    await entry.ReloadAsync();
                }
                catch (InvalidOperationException)
                {
                    latest = null;
                }
            }
        }

        latest ??= await Db.FfcRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

        if (latest is null)
        {
            Input = new InputModel();
            await LoadPageAsync();
            return false;
        }

        Input = MapToInput(latest);
        await LoadPageAsync(id);
        return true;
    }

    private async Task ValidateAsync(InputModel i)
    {
        var hasActiveCountry = await Db.FfcCountries.AnyAsync(c => c.Id == i.CountryId && c.IsActive);
        if (!hasActiveCountry)
        {
            var isExistingInactive = false;
            if (i.Id is { } id)
            {
                var existingCountryId = await Db.FfcRecords
                    .Where(r => r.Id == id)
                    .Select(r => (long?)r.CountryId)
                    .FirstOrDefaultAsync();

                isExistingInactive = existingCountryId.HasValue && existingCountryId.Value == i.CountryId;
            }

            if (!isExistingInactive)
            {
                ModelState.AddModelError(nameof(Input) + "." + nameof(Input.CountryId), "Select a valid active country.");
            }
        }

        if (i.Year is < 2000 or > 2100)
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.Year), "Year must be between 2000 and 2100.");

        if (i.IpaDate.HasValue && !i.IpaYes)
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.IpaDate), "IPA date requires IPA = Yes.");
        if (i.GslDate.HasValue && !i.GslYes)
            ModelState.AddModelError(nameof(Input) + "." + nameof(Input.GslDate), "GSL date requires GSL = Yes.");
    }

    public async Task<IActionResult> OnGetEditAsync(long id)
    {
        EditId = id;
        return await OnGetAsync();
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

    private static Dictionary<string, string?> BuildRecordData(FfcRecord record, string prefix)
    {
        static string? FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new Dictionary<string, string?>
        {
            [$"{prefix}.CountryId"] = record.CountryId.ToString(),
            [$"{prefix}.Year"] = record.Year.ToString(CultureInfo.InvariantCulture),
            [$"{prefix}.IpaYes"] = record.IpaYes.ToString(),
            [$"{prefix}.IpaDate"] = FormatDate(record.IpaDate),
            [$"{prefix}.IpaRemarks"] = record.IpaRemarks,
            [$"{prefix}.GslYes"] = record.GslYes.ToString(),
            [$"{prefix}.GslDate"] = FormatDate(record.GslDate),
            [$"{prefix}.GslRemarks"] = record.GslRemarks,
            [$"{prefix}.DeliveryYes"] = record.DeliveryYes.ToString(),
            [$"{prefix}.DeliveryDate"] = FormatDate(record.DeliveryDate),
            [$"{prefix}.DeliveryRemarks"] = record.DeliveryRemarks,
            [$"{prefix}.InstallationYes"] = record.InstallationYes.ToString(),
            [$"{prefix}.InstallationDate"] = FormatDate(record.InstallationDate),
            [$"{prefix}.InstallationRemarks"] = record.InstallationRemarks,
            [$"{prefix}.OverallRemarks"] = record.OverallRemarks,
            [$"{prefix}.IsDeleted"] = record.IsDeleted.ToString()
        };
    }

    public FfcProjectQuantitySummary GetProjectSummary(FfcRecord record)
    {
        if (record is null)
        {
            return new FfcProjectQuantitySummary(0, 0, 0);
        }

        if (_projectSummaryCache.TryGetValue(record.Id, out var summary))
        {
            return summary;
        }

        summary = FfcProjectBucketHelper.Summarize(record.Projects);
        _projectSummaryCache[record.Id] = summary;
        return summary;
    }
}
