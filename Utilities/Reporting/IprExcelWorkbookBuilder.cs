using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

public interface IIprExcelWorkbookBuilder
{
    byte[] Build(IprExcelWorkbookContext context);
}

public sealed record IprExcelWorkbookContext(IReadOnlyList<IprExportRowDto> Rows);

public sealed class IprExcelWorkbookBuilder : IIprExcelWorkbookBuilder
{
    public byte[] Build(IprExcelWorkbookContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("IPR Records");

        WriteHeaderRow(worksheet);
        WriteDataRows(worksheet, context.Rows);

        worksheet.Column(3).Style.NumberFormat.Format = "@";
        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream.ToArray();
    }

    private static void WriteHeaderRow(IXLWorksheet worksheet)
    {
        var headers = new[]
        {
            "Ser",
            "Name of the product",
            "IPR filing no",
            "Status",
            "Filed by",
            "Filing date",
            "Grant date",
            "Project",
            "Remarks"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
        }

        worksheet.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        worksheet.Range(1, 1, 1, headers.Length).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
    }

    private static void WriteDataRows(IXLWorksheet worksheet, IReadOnlyList<IprExportRowDto> rows)
    {
        var istZone = TimeZoneHelper.GetIst();

        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = index + 2;
            var record = rows[index];

            worksheet.Cell(rowNumber, 1).Value = index + 1;
            worksheet.Cell(rowNumber, 2).Value = record.Title ?? string.Empty;
            worksheet.Cell(rowNumber, 3).Value = record.FilingNumber ?? string.Empty;
            worksheet.Cell(rowNumber, 4).Value = GetStatusLabel(record.Status);
            worksheet.Cell(rowNumber, 5).Value = record.FiledBy ?? string.Empty;

            if (record.FiledAtUtc.HasValue)
            {
                var filedAtIst = TimeZoneInfo.ConvertTime(record.FiledAtUtc.Value, istZone);
                var filedCell = worksheet.Cell(rowNumber, 6);
                filedCell.Value = filedAtIst.Date;
                filedCell.Style.DateFormat.Format = "dd-mmm-yyyy";
            }

            if (record.GrantedAtUtc.HasValue)
            {
                var grantedAtIst = TimeZoneInfo.ConvertTime(record.GrantedAtUtc.Value, istZone);
                var grantedCell = worksheet.Cell(rowNumber, 7);
                grantedCell.Value = grantedAtIst.Date;
                grantedCell.Style.DateFormat.Format = "dd-mmm-yyyy";
            }

            worksheet.Cell(rowNumber, 8).Value = record.ProjectName ?? string.Empty;
            worksheet.Cell(rowNumber, 9).Value = record.Remarks ?? string.Empty;
        }
    }

    private static string GetStatusLabel(IprStatus status)
        => status switch
        {
            IprStatus.FilingUnderProcess => "Filing under process",
            IprStatus.Filed => "Filed",
            IprStatus.Granted => "Granted",
            IprStatus.Rejected => "Rejected",
            IprStatus.Withdrawn => "Withdrawn",
            _ => status.ToString()
        };
}
