using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public class MapTableDetailedModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IFfcQueryService _ffcQueryService;

    // SECTION: Construction
    public MapTableDetailedModel(ApplicationDbContext db, IFfcQueryService ffcQueryService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ffcQueryService = ffcQueryService ?? throw new ArgumentNullException(nameof(ffcQueryService));
    }

    // SECTION: Filter state
    public long? CountryId { get; private set; }

    public short? Year { get; private set; }

    public string? CountryName { get; private set; }

    public bool HasFilters => CountryId.HasValue || Year.HasValue;

    public string? FilterSummary { get; private set; }

    // SECTION: View model
    public IReadOnlyList<FfcDetailedGroupVm> Groups { get; private set; } = Array.Empty<FfcDetailedGroupVm>();

    // SECTION: Request handlers
    public async Task OnGetAsync(long? countryId, short? year, CancellationToken cancellationToken)
    {
        CountryId = countryId;
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
        Groups = await _ffcQueryService.GetDetailedGroupsAsync(rangeFrom, rangeTo, incompleteOnly: false, CountryId, Year, cancellationToken);

        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" })),
            ("Detailed table", null));
    }

    public async Task<IActionResult> OnGetDataAsync(long? countryId, short? year, CancellationToken cancellationToken)
    {
        CountryId = countryId;
        Year = year;

        var (rangeFrom, rangeTo) = ResolveRange();
        var groups = await _ffcQueryService.GetDetailedGroupsAsync(rangeFrom, rangeTo, incompleteOnly: false, CountryId, Year, cancellationToken);
        return new JsonResult(groups);
    }

    public async Task<IActionResult> OnGetExportAsync(long? countryId, short? year, CancellationToken cancellationToken)
    {
        CountryId = countryId;
        Year = year;

        var (rangeFrom, rangeTo) = ResolveRange();
        var groups = await _ffcQueryService.GetDetailedGroupsAsync(rangeFrom, rangeTo, incompleteOnly: false, CountryId, Year, cancellationToken);

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.AddWorksheet("FFC Projects (Detailed)");

        // SECTION: Header row
        var columnIndex = 1;
        worksheet.Cell(1, columnIndex++).Value = "Country";
        worksheet.Cell(1, columnIndex++).Value = "ISO3";
        worksheet.Cell(1, columnIndex++).Value = "Year";
        worksheet.Cell(1, columnIndex++).Value = "S. No.";
        worksheet.Cell(1, columnIndex++).Value = "Project";
        worksheet.Cell(1, columnIndex++).Value = "Cost (₹ Cr)";
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
                    worksheet.Cell(rowIndex, columnIndex).Value = project.CostInCr.Value;
                    worksheet.Cell(rowIndex, columnIndex).Style.NumberFormat.Format = "0.00";
                }
                columnIndex++;

                worksheet.Cell(rowIndex, columnIndex++).Value = project.Quantity;
                worksheet.Cell(rowIndex, columnIndex++).Value = project.Status;
                worksheet.Cell(rowIndex, columnIndex++).Value = project.Progress ?? string.Empty;
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
}
