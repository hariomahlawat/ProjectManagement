using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Remarks;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
[ValidateAntiForgeryToken]
public class MapTableDetailedModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IFfcQueryService _ffcQueryService;
    private readonly IFfcProgressService _ffcProgressService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MapTableDetailedModel> _logger;

    // SECTION: Construction
    public MapTableDetailedModel(
        ApplicationDbContext db,
        IFfcQueryService ffcQueryService,
        IFfcProgressService ffcProgressService,
        UserManager<ApplicationUser> userManager,
        ILogger<MapTableDetailedModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ffcQueryService = ffcQueryService ?? throw new ArgumentNullException(nameof(ffcQueryService));
        _ffcProgressService = ffcProgressService ?? throw new ArgumentNullException(nameof(ffcProgressService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                worksheet.Cell(rowIndex, columnIndex++).Value = project.ProgressText ?? string.Empty;
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

    // SECTION: Inline editing handlers
    public async Task<IActionResult> OnPostUpdateOverallRemarksAsync([FromBody] UpdateOverallRemarksRequest request, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("HoD"))
        {
            return Forbid();
        }

        if (request is null)
        {
            return BadRequest(new { message = "Request payload is missing." });
        }

        if (request.FfcRecordId <= 0)
        {
            return BadRequest(new { message = "Invalid record identifier." });
        }

        var normalized = NormalizeRemark(request.OverallRemarks);
        if (normalized.Length > OverallRemarksMaxLength)
        {
            return BadRequest(new { message = $"Overall remarks must be {OverallRemarksMaxLength} characters or fewer." });
        }

        var record = await _db.FfcRecords
            .FirstOrDefaultAsync(item => item.Id == request.FfcRecordId, cancellationToken);

        if (record is null)
        {
            return NotFound(new { message = "Record not found." });
        }

        record.OverallRemarks = string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new JsonResult(new
        {
            ok = true,
            overallRemarks = normalized,
            renderedOverallRemarks = FormatRemarkForDisplay(normalized),
            updatedAtUtc = record.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            updatedBy = User.Identity?.Name
        });
    }

    public async Task<IActionResult> OnPostUpdateProgressAsync([FromBody] UpdateProgressRequest request, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("HoD"))
        {
            return Forbid();
        }

        if (request is null)
        {
            return BadRequest(new { message = "Request payload is missing." });
        }

        var actor = await BuildRemarkActorContextAsync(cancellationToken);

        try
        {
            var result = await _ffcProgressService.UpdateProgressAsync(
                new FfcProgressUpdateCommand(
                    FfcProjectId: request.FfcProjectId,
                    RequestedLinkedProjectId: request.LinkedProjectId,
                    ExternalRemarkId: request.ExternalRemarkId,
                    ProgressText: request.ProgressText,
                    Actor: actor),
                cancellationToken);

            return new JsonResult(new
            {
                ok = true,
                progressText = result.ProgressText,
                renderedProgressText = FormatRemarkForDisplay(result.ProgressText),
                updatedAtUtc = result.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                updatedBy = User.Identity?.Name,
                externalRemarkId = result.ExternalRemarkId
            });
        }
        catch (FfcProgressNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (FfcProgressValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateException ex)
        {
            LogProgressUpdateFailure(ex, request, request.LinkedProjectId);
            return BadRequest(new
            {
                message = "Unable to save progress due to a database constraint. See server logs for details."
            });
        }
        catch (Exception ex)
        {
            LogProgressUpdateFailure(ex, request, request.LinkedProjectId);
            return StatusCode(500, new
            {
                ok = false,
                message = "Unable to save. See server logs for details."
            });
        }
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

    // SECTION: Inline editing helpers
    private const int OverallRemarksMaxLength = 4000;

    private static string NormalizeRemark(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string FormatRemarkForDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        const int limit = 200;
        return text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit), "…");
    }

    private Task<RemarkActorContext?> BuildRemarkActorContextAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult<RemarkActorContext?>(null);
        }

        var roles = new List<RemarkActorRole>();
        if (User.IsInRole("Admin"))
        {
            roles.Add(RemarkActorRole.Administrator);
        }

        if (User.IsInRole("HoD"))
        {
            roles.Add(RemarkActorRole.HeadOfDepartment);
        }

        if (roles.Count == 0)
        {
            return Task.FromResult<RemarkActorContext?>(null);
        }

        var primary = roles.Contains(RemarkActorRole.Administrator)
            ? RemarkActorRole.Administrator
            : RemarkActorRole.HeadOfDepartment;

        return Task.FromResult<RemarkActorContext?>(new RemarkActorContext(userId, primary, roles));
    }

    private void LogProgressUpdateFailure(Exception ex, UpdateProgressRequest request, int? linkedProjectId)
    {
        var userId = _userManager.GetUserId(User);
        var roles = new List<string>();
        if (User.IsInRole("Admin"))
        {
            roles.Add("Admin");
        }

        if (User.IsInRole("HoD"))
        {
            roles.Add("HoD");
        }

        _logger.LogError(
            ex,
            "Failed to update FFC progress. FfcProjectId={FfcProjectId}, LinkedProjectId={LinkedProjectId}, ExternalRemarkId={ExternalRemarkId}, UserId={UserId}, Roles={Roles}",
            request.FfcProjectId,
            linkedProjectId,
            request.ExternalRemarkId,
            userId ?? string.Empty,
            roles.Count == 0 ? "None" : string.Join(",", roles));

        // SECTION: Database exception details
        if (ex is DbUpdateException { InnerException: Npgsql.PostgresException postgresException })
        {
            _logger.LogError(
                "PostgresException SqlState={SqlState}, Constraint={Constraint}, Detail={Detail}",
                postgresException.SqlState,
                postgresException.ConstraintName,
                postgresException.Detail);
        }
    }

    public sealed class UpdateOverallRemarksRequest
    {
        [Required]
        public long FfcRecordId { get; set; }

        public string? OverallRemarks { get; set; }
    }

    public sealed class UpdateProgressRequest
    {
        [Required]
        public long FfcProjectId { get; set; }

        public string? ProgressText { get; set; }

        public int? LinkedProjectId { get; set; }

        public int? ExternalRemarkId { get; set; }
    }

}
