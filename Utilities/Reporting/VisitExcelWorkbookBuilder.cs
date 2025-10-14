using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

public interface IVisitExcelWorkbookBuilder
{
    byte[] Build(VisitExcelWorkbookContext context);
}

public sealed record VisitExcelWorkbookContext(
    IReadOnlyList<VisitExportRow> Rows,
    DateTimeOffset GeneratedAtUtc,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed class VisitExcelWorkbookBuilder : IVisitExcelWorkbookBuilder
{
    public byte[] Build(VisitExcelWorkbookContext context)
    {
        if (context.Rows is null)
        {
            throw new ArgumentNullException(nameof(context.Rows));
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Visits");
        WriteHeader(worksheet);
        WriteRows(worksheet, context.Rows);
        ApplyMetadata(worksheet, context);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet worksheet)
    {
        var headers = new[]
        {
            "S.No.",
            "Visit type",
            "Date of visit",
            "Visitor",
            "Strength",
            "Photo count",
            "Has cover photo",
            "Remarks"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = headers[column];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239);
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.SheetView.FreezeRows(1);
    }

    private static void WriteRows(IXLWorksheet worksheet, IReadOnlyList<VisitExportRow> rows)
    {
        var rowNumber = 2;

        for (var index = 0; index < rows.Count; index++, rowNumber++)
        {
            var row = rows[index];
            worksheet.Cell(rowNumber, 1).Value = index + 1;
            worksheet.Cell(rowNumber, 2).Value = row.VisitTypeName;
            worksheet.Cell(rowNumber, 3).Value = row.DateOfVisit.ToDateTime(TimeOnly.MinValue);
            worksheet.Cell(rowNumber, 3).Style.DateFormat.Format = "dd-MMM-yyyy";
            worksheet.Cell(rowNumber, 4).Value = row.VisitorName;
            worksheet.Cell(rowNumber, 5).Value = row.Strength;
            worksheet.Cell(rowNumber, 6).Value = row.PhotoCount;
            worksheet.Cell(rowNumber, 7).Value = row.HasCoverPhoto ? "Yes" : "No";
            worksheet.Cell(rowNumber, 8).Value = row.Remarks ?? string.Empty;
        }

        worksheet.Column(8).Style.Alignment.WrapText = true;
        worksheet.Column(8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    private static void ApplyMetadata(IXLWorksheet worksheet, VisitExcelWorkbookContext context)
    {
        var lastRow = Math.Max(2, context.Rows.Count + 1);
        worksheet.Columns(1, 8).AdjustToContents(1, lastRow);

        foreach (var column in worksheet.Columns(1, 8))
        {
            if (column.Width > 60)
            {
                column.Width = 60;
            }
        }

        worksheet.Column(4).Width = Math.Min(worksheet.Column(4).Width, 40);
        worksheet.Column(8).Width = Math.Min(worksheet.Column(8).Width, 60);

        var metadataRow = context.Rows.Count + 3;
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());

        worksheet.Cell(metadataRow, 1).Value = "Export generated";
        worksheet.Cell(metadataRow, 2).Value = generatedAtIst.DateTime;
        worksheet.Cell(metadataRow, 2).Style.DateFormat.Format = "yyyy-MM-dd HH:mm\" IST\"";

        worksheet.Cell(metadataRow + 1, 1).Value = "From";
        worksheet.Cell(metadataRow + 1, 2).Value = context.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(not set)";

        worksheet.Cell(metadataRow + 2, 1).Value = "To";
        worksheet.Cell(metadataRow + 2, 2).Value = context.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(not set)";

        worksheet.Range(metadataRow, 1, metadataRow + 2, 1).Style.Font.Bold = true;
    }
}
