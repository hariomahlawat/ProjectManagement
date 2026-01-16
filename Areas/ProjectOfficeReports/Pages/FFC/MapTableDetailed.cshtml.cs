using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public class MapTableDetailedModel : PageModel
{
    private const int MaxRowRemarkLength = 2000;
    private const int MaxOverallRemarkLength = 4000;
    private readonly ApplicationDbContext _db;
    private readonly IFfcQueryService _ffcQueryService;
    private readonly UserManager<ApplicationUser> _userManager;

    // SECTION: Construction
    public MapTableDetailedModel(
        ApplicationDbContext db,
        IFfcQueryService ffcQueryService,
        UserManager<ApplicationUser> userManager)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ffcQueryService = ffcQueryService ?? throw new ArgumentNullException(nameof(ffcQueryService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    // SECTION: Filter state
    public long? CountryId { get; private set; }

    public string? CountryIso3 { get; private set; }

    public short? Year { get; private set; }

    public string? CountryName { get; private set; }

    public bool HasFilters => CountryId.HasValue || Year.HasValue || !string.IsNullOrWhiteSpace(CountryIso3);

    public string? FilterSummary { get; private set; }

    // SECTION: View model
    public IReadOnlyList<FfcDetailedGroupVm> Groups { get; private set; } = Array.Empty<FfcDetailedGroupVm>();

    // SECTION: Request handlers
    public async Task OnGetAsync(long? countryId, short? year, string? countryIso3, CancellationToken cancellationToken)
    {
        CountryIso3 = string.IsNullOrWhiteSpace(countryIso3) ? null : countryIso3.Trim().ToUpperInvariant();
        CountryId = await ResolveCountryIdAsync(countryId, CountryIso3, cancellationToken);
        Year = year;

        if (CountryId.HasValue)
        {
            CountryName = await _db.FfcCountries
                .AsNoTracking()
                .Where(country => country.Id == CountryId.Value)
                .Select(country => country.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        FilterSummary = BuildFilterSummary();

        var (rangeFrom, rangeTo) = ResolveRange();
        Groups = await _ffcQueryService.GetDetailedGroupsAsync(rangeFrom, rangeTo, incompleteOnly: false, CountryId, Year, applyYearFilter: true, cancellationToken: cancellationToken);

        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" })),
            ("Detailed table", null));
    }

    public async Task<IActionResult> OnGetDataAsync(long? countryId, short? year, string? countryIso3, CancellationToken cancellationToken)
    {
        CountryIso3 = string.IsNullOrWhiteSpace(countryIso3) ? null : countryIso3.Trim().ToUpperInvariant();
        CountryId = await ResolveCountryIdAsync(countryId, CountryIso3, cancellationToken);
        Year = year;

        var (rangeFrom, rangeTo) = ResolveRange();
        var groups = await _ffcQueryService.GetDetailedGroupsAsync(rangeFrom, rangeTo, incompleteOnly: false, CountryId, Year, applyYearFilter: true, cancellationToken: cancellationToken);
        return new JsonResult(groups);
    }

    public async Task<IActionResult> OnGetExportAsync(long? countryId, short? year, string? countryIso3, CancellationToken cancellationToken)
    {
        CountryIso3 = string.IsNullOrWhiteSpace(countryIso3) ? null : countryIso3.Trim().ToUpperInvariant();
        CountryId = await ResolveCountryIdAsync(countryId, CountryIso3, cancellationToken);
        Year = year;

        var (rangeFrom, rangeTo) = ResolveRange();
        var groups = await _ffcQueryService.GetDetailedGroupsAsync(rangeFrom, rangeTo, incompleteOnly: false, CountryId, Year, applyYearFilter: true, cancellationToken: cancellationToken);

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.AddWorksheet("FFC Projects (Detailed)");

        // SECTION: Header row
        var columnIndex = 1;
        worksheet.Cell(1, columnIndex++).Value = "Country";
        worksheet.Cell(1, columnIndex++).Value = "ISO3";
        worksheet.Cell(1, columnIndex++).Value = "Year";
        worksheet.Cell(1, columnIndex++).Value = "S. No.";
        worksheet.Cell(1, columnIndex++).Value = "Project";
        worksheet.Cell(1, columnIndex++).Value = "Cost (₹ Lakh)";
        worksheet.Cell(1, columnIndex++).Value = "Quantity";
        worksheet.Cell(1, columnIndex++).Value = "Status";
        worksheet.Cell(1, columnIndex++).Value = "Progress / present status";
        worksheet.Cell(1, columnIndex++).Value = "Overall remarks";

        var rowIndex = 2;
        foreach (var group in groups
            .OrderByDescending(g => g.Year)
            .ThenBy(g => g.CountryName, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Rows is null || group.Rows.Count == 0)
            {
                continue;
            }

            foreach (var project in group.Rows)
            {
                columnIndex = 1;

                worksheet.Cell(rowIndex, columnIndex++).Value = group.CountryName;
                worksheet.Cell(rowIndex, columnIndex++).Value = group.CountryCode;
                worksheet.Cell(rowIndex, columnIndex++).Value = group.Year;
                worksheet.Cell(rowIndex, columnIndex++).Value = project.Serial;
                worksheet.Cell(rowIndex, columnIndex++).Value = project.ProjectName;

                if (project.CostInCr.HasValue)
                {
                    var costInLakh = project.CostInCr.Value * 100m;
                    worksheet.Cell(rowIndex, columnIndex).Value = costInLakh;
                    worksheet.Cell(rowIndex, columnIndex).Style.NumberFormat.Format = "0.00";
                }
                columnIndex++;

                worksheet.Cell(rowIndex, columnIndex++).Value = project.Quantity;
                worksheet.Cell(rowIndex, columnIndex++).Value = project.Status;
                worksheet.Cell(rowIndex, columnIndex++).Value = ResolveProgressDisplay(project);
                worksheet.Cell(rowIndex, columnIndex++).Value = group.OverallRemarks ?? string.Empty;

                rowIndex++;
            }

            rowIndex++;
        }

        // SECTION: Styling
        var headerRange = worksheet.Range(1, 1, 1, 10);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#f3f4f6");
        headerRange.Style.Border.BottomBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();

        using var stream = new System.IO.MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"FFC_Projects_Detailed_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
        return File(
            fileContents: stream.ToArray(),
            contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: fileName);
    }

    // SECTION: Inline edit handlers
    [Authorize(Roles = "HoD,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostUpdateRowRemarkAsync([FromBody] RowRemarkUpdateRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.FfcProjectId <= 0)
        {
            return BadRequest(new { ok = false, message = "Invalid request payload." });
        }

        if (!TryDecodeRowVersion(request.RowVersion, out var expectedRowVersion))
        {
            return BadRequest(new { ok = false, message = "Missing or invalid row version." });
        }

        var normalized = NormalizeRemark(request.Remark);
        if (normalized.Length > MaxRowRemarkLength)
        {
            return BadRequest(new { ok = false, message = $"Remarks cannot exceed {MaxRowRemarkLength} characters." });
        }

        var entity = await _db.FfcProjects
            .FirstOrDefaultAsync(project => project.Id == request.FfcProjectId, cancellationToken);

        if (entity is null)
        {
            return NotFound(new { ok = false, message = "Project row not found." });
        }

        if (!entity.RowVersion.SequenceEqual(expectedRowVersion))
        {
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, message = "This row was updated by someone else. Please refresh." });
        }

        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var userName = await ResolveUserDisplayNameAsync(userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        entity.ProgressRemarks = normalized;
        entity.ProgressRemarksUpdatedAtUtc = now;
        entity.ProgressRemarksUpdatedByUserId = userId;
        entity.UpdatedAt = now;

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = expectedRowVersion;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, message = "This row was updated by someone else. Please refresh." });
        }

        return new JsonResult(new
        {
            ok = true,
            remark = entity.ProgressRemarks ?? string.Empty,
            updatedAtUtc = entity.ProgressRemarksUpdatedAtUtc,
            updatedBy = userName,
            rowVersion = Convert.ToBase64String(entity.RowVersion)
        });
    }

    [Authorize(Roles = "HoD,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostUpdateOverallRemarkAsync([FromBody] OverallRemarkUpdateRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.FfcRecordId <= 0)
        {
            return BadRequest(new { ok = false, message = "Invalid request payload." });
        }

        if (!TryDecodeRowVersion(request.RowVersion, out var expectedRowVersion))
        {
            return BadRequest(new { ok = false, message = "Missing or invalid row version." });
        }

        var normalized = NormalizeRemark(request.Remark);
        if (normalized.Length > MaxOverallRemarkLength)
        {
            return BadRequest(new { ok = false, message = $"Remarks cannot exceed {MaxOverallRemarkLength} characters." });
        }

        var entity = await _db.FfcRecords
            .FirstOrDefaultAsync(record => record.Id == request.FfcRecordId, cancellationToken);

        if (entity is null)
        {
            return NotFound(new { ok = false, message = "FFC record not found." });
        }

        if (!entity.RowVersion.SequenceEqual(expectedRowVersion))
        {
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, message = "This row was updated by someone else. Please refresh." });
        }

        var userId = _userManager.GetUserId(User) ?? string.Empty;
        var userName = await ResolveUserDisplayNameAsync(userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        entity.OverallRemarks = normalized;
        entity.OverallRemarksUpdatedAtUtc = now;
        entity.OverallRemarksUpdatedByUserId = userId;
        entity.UpdatedAt = now;

        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = expectedRowVersion;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, message = "This row was updated by someone else. Please refresh." });
        }

        return new JsonResult(new
        {
            ok = true,
            remark = entity.OverallRemarks ?? string.Empty,
            updatedAtUtc = entity.OverallRemarksUpdatedAtUtc,
            updatedBy = userName,
            rowVersion = Convert.ToBase64String(entity.RowVersion)
        });
    }

    // SECTION: Helper methods
    private string? BuildFilterSummary()
    {
        if (!HasFilters)
        {
            return null;
        }

        var parts = new List<string>();

        if (CountryId.HasValue)
        {
            parts.Add(string.IsNullOrWhiteSpace(CountryName) ? "Selected country" : CountryName!);
        }
        else if (!string.IsNullOrWhiteSpace(CountryIso3))
        {
            parts.Add(CountryIso3);
        }

        if (Year.HasValue)
        {
            parts.Add(Year.Value.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(" · ", parts);
    }

    private (DateOnly From, DateOnly To) ResolveRange()
    {
        if (Year.HasValue)
        {
            return (new DateOnly(Year.Value, 1, 1), new DateOnly(Year.Value, 12, 31));
        }

        return (DateOnly.MinValue, DateOnly.MaxValue);
    }

    // SECTION: Lookup helpers
    private async Task<long?> ResolveCountryIdAsync(long? countryId, string? countryIso3, CancellationToken cancellationToken)
    {
        if (countryId.HasValue)
        {
            return countryId;
        }

        if (string.IsNullOrWhiteSpace(countryIso3))
        {
            return null;
        }

        var iso = countryIso3.Trim().ToUpperInvariant();

        var matchedId = await _db.FfcCountries
            .AsNoTracking()
            .Where(country => country.IsoCode == iso)
            .Select(country => country.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return matchedId == 0 ? null : matchedId;
    }

    // SECTION: Inline edit helpers
    private static string NormalizeRemark(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Trim()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
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

    private async Task<string> ResolveUserDisplayNameAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return "Unknown";
        }

        var name = await _db.Users
            .Where(user => user.Id == userId)
            .Select(user => user.FullName)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(name) ? (User.Identity?.Name ?? "Unknown") : name;
    }

    private static string ResolveProgressDisplay(FfcDetailedRowVm project)
    {
        if (!string.IsNullOrWhiteSpace(project.ProgressRemarks))
        {
            return project.ProgressRemarks;
        }

        return project.Progress ?? string.Empty;
    }

    // SECTION: Inline edit contracts
    public sealed record RowRemarkUpdateRequest(long FfcProjectId, string? Remark, string? RowVersion);

    public sealed record OverallRemarkUpdateRequest(long FfcRecordId, string? Remark, string? RowVersion);
}
