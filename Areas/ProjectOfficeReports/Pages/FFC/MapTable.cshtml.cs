using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public class MapTableModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public MapTableModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetDataAsync(CancellationToken cancellationToken)
    {
        var rows = await FfcCountryRollupDataSource.LoadAsync(_db, cancellationToken);
        return new JsonResult(rows);
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        var rows = await FfcCountryRollupDataSource.LoadAsync(_db, cancellationToken);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Country summary");

        worksheet.Cell(1, 1).Value = "Country";
        worksheet.Cell(1, 2).Value = "ISO3";
        worksheet.Cell(1, 3).Value = "Installed";
        worksheet.Cell(1, 4).Value = "Delivered (not installed)";
        worksheet.Cell(1, 5).Value = "Planned";
        worksheet.Cell(1, 6).Value = "Total";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            worksheet.Cell(rowIndex, 1).Value = row.Name;
            worksheet.Cell(rowIndex, 2).Value = row.Iso3;
            worksheet.Cell(rowIndex, 3).Value = row.Installed;
            worksheet.Cell(rowIndex, 4).Value = row.Delivered;
            worksheet.Cell(rowIndex, 5).Value = row.Planned;
            worksheet.Cell(rowIndex, 6).Value = row.Total;
            rowIndex++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"FFC_Country_Summary_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
