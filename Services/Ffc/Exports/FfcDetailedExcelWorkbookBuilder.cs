using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Ffc.Exports;

public sealed class FfcDetailedExcelWorkbookBuilder
{
    private const int ColumnCount = 10;
    private const int TableHeaderRow = 9;

    public byte[] Build(FfcDetailedTableExportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("FFC Projects Update");
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());
        var summary = context.Summary;

        worksheet.Style.Font.FontName = "Arial";
        worksheet.Style.Font.FontSize = 10;
        worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        BuildHeading(worksheet, context, generatedAtIst);
        BuildSummary(worksheet, summary);
        BuildTableHeader(worksheet);

        var rowIndex = TableHeaderRow + 1;
        foreach (var group in context.Groups
                     .Where(group => group.Rows is { Count: > 0 })
                     .OrderByDescending(group => group.Year)
                     .ThenBy(group => group.CountryName, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var project in group.Rows)
            {
                worksheet.Cell(rowIndex, 1).SetValue(group.CountryName);
                worksheet.Cell(rowIndex, 2).SetValue(group.CountryCode);
                worksheet.Cell(rowIndex, 3).SetValue(group.Year);
                worksheet.Cell(rowIndex, 4).SetValue(project.Serial);
                worksheet.Cell(rowIndex, 5).SetValue(project.ProjectName);

                if (project.CostInCr.HasValue)
                {
                    worksheet.Cell(rowIndex, 6).SetValue(project.CostInCr.Value * 100m);
                }

                worksheet.Cell(rowIndex, 7).SetValue(project.Quantity);
                worksheet.Cell(rowIndex, 8).SetValue(project.Status);
                worksheet.Cell(rowIndex, 9).SetValue(NormalizeNarrative(project.ProgressText));
                worksheet.Cell(rowIndex, 10).SetValue(NormalizeNarrative(group.OverallRemarks));
                rowIndex++;
            }
        }

        var lastDataRow = Math.Max(TableHeaderRow, rowIndex - 1);
        ApplyTableFormatting(worksheet, lastDataRow);
        ApplyColumnLayout(worksheet);
        ConfigurePrint(worksheet, context.HandlingMarking);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildHeading(
        IXLWorksheet worksheet,
        FfcDetailedTableExportContext context,
        DateTimeOffset generatedAtIst)
    {
        var titleRange = worksheet.Range(1, 1, 1, ColumnCount).Merge();
        titleRange.Value = context.Title;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 18;
        titleRange.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        worksheet.Row(1).Height = 28;

        var scopeRange = worksheet.Range(2, 1, 2, ColumnCount).Merge();
        scopeRange.Value = $"Scope: {context.ScopeLabel}";
        scopeRange.Style.Font.FontColor = XLColor.FromHtml("#526174");

        var generatedRange = worksheet.Range(3, 1, 3, ColumnCount).Merge();
        generatedRange.Value = $"Position as at {generatedAtIst:dd MMM yyyy, HH:mm} IST";
        generatedRange.Style.Font.FontColor = XLColor.FromHtml("#526174");

        if (!string.IsNullOrWhiteSpace(context.HandlingMarking))
        {
            var markingRange = worksheet.Range(4, 1, 4, ColumnCount).Merge();
            markingRange.Value = context.HandlingMarking;
            markingRange.Style.Font.Bold = true;
            markingRange.Style.Font.FontColor = XLColor.FromHtml("#9C1C1C");
            markingRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static void BuildSummary(IXLWorksheet worksheet, FfcDetailedTableExportSummary summary)
    {
        SetSummaryPair(worksheet, 5, 1, "Countries", summary.CountryCount);
        SetSummaryPair(worksheet, 5, 3, "Country–year records", summary.RecordCount);
        SetSummaryPair(worksheet, 5, 5, "Projects", summary.ProjectCount);
        SetSummaryPair(worksheet, 5, 7, "Total quantity", summary.TotalQuantity);
        SetSummaryPair(worksheet, 5, 9, "Cost coverage", summary.CostCoverageLabel);

        SetSummaryPair(worksheet, 6, 1, "Installed", summary.InstalledQuantity);
        SetSummaryPair(worksheet, 6, 3, "Delivered, awaiting installation", summary.DeliveredNotInstalledQuantity);
        SetSummaryPair(worksheet, 6, 5, "Planned", summary.PlannedQuantity);
        SetSummaryPair(worksheet, 6, 7, "Available project cost (₹ lakh)", summary.AvailableCostInLakh, "#,##0.00");
        SetSummaryPair(worksheet, 6, 9, "Cost note", "Resolved project costs only");

        var summaryRange = worksheet.Range(5, 1, 6, ColumnCount);
        summaryRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        summaryRange.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        summaryRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#AAB7C6");
        summaryRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#D8E0E8");
        summaryRange.Style.Alignment.WrapText = true;
    }

    private static void SetSummaryPair(
        IXLWorksheet worksheet,
        int row,
        int labelColumn,
        string label,
        object value,
        string? numberFormat = null)
    {
        var labelCell = worksheet.Cell(row, labelColumn);
        labelCell.SetValue(label);
        labelCell.Style.Font.Bold = true;
        labelCell.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        labelCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F7FA");

        var valueCell = worksheet.Cell(row, labelColumn + 1);
        switch (value)
        {
            case int intValue:
                valueCell.SetValue(intValue);
                valueCell.Style.NumberFormat.Format = "#,##0";
                valueCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                break;
            case decimal decimalValue:
                valueCell.SetValue(decimalValue);
                valueCell.Style.NumberFormat.Format = numberFormat ?? "#,##0.00";
                valueCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                break;
            default:
                valueCell.SetValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                break;
        }
    }

    private static void BuildTableHeader(IXLWorksheet worksheet)
    {
        var headings = new[]
        {
            "Country",
            "ISO3",
            "Year",
            "S. No.",
            "Project",
            "Cost (₹ lakh)",
            "Quantity",
            "Status",
            "Current progress",
            "Overall status"
        };

        for (var column = 1; column <= headings.Length; column++)
        {
            worksheet.Cell(TableHeaderRow, column).SetValue(headings[column - 1]);
        }

        var headerRange = worksheet.Range(TableHeaderRow, 1, TableHeaderRow, ColumnCount);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
        headerRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.TopBorderColor = XLColor.FromHtml("#AAB7C6");
        headerRange.Style.Border.BottomBorderColor = XLColor.FromHtml("#7F91A5");
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        worksheet.Row(TableHeaderRow).Height = 28;
    }

    private static void ApplyTableFormatting(IXLWorksheet worksheet, int lastDataRow)
    {
        if (lastDataRow > TableHeaderRow)
        {
            var dataRange = worksheet.Range(TableHeaderRow + 1, 1, lastDataRow, ColumnCount);
            dataRange.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            dataRange.Style.Border.BottomBorderColor = XLColor.FromHtml("#D8E0E8");
            dataRange.Style.Alignment.WrapText = true;

            worksheet.Range(TableHeaderRow + 1, 3, lastDataRow, 4)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(TableHeaderRow + 1, 6, lastDataRow, 7)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            worksheet.Range(TableHeaderRow + 1, 6, lastDataRow, 6)
                .Style.NumberFormat.Format = "#,##0.00";
            worksheet.Range(TableHeaderRow + 1, 7, lastDataRow, 7)
                .Style.NumberFormat.Format = "#,##0";
        }

        worksheet.Range(TableHeaderRow, 1, lastDataRow, ColumnCount).SetAutoFilter();
        worksheet.SheetView.FreezeRows(TableHeaderRow);
        worksheet.SheetView.FreezeColumns(3);
    }

    private static void ApplyColumnLayout(IXLWorksheet worksheet)
    {
        worksheet.Column(1).Width = 19;
        worksheet.Column(2).Width = 8;
        worksheet.Column(3).Width = 8;
        worksheet.Column(4).Width = 8;
        worksheet.Column(5).Width = 34;
        worksheet.Column(6).Width = 15;
        worksheet.Column(7).Width = 11;
        worksheet.Column(8).Width = 24;
        worksheet.Column(9).Width = 58;
        worksheet.Column(10).Width = 64;

        worksheet.Column(2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Column(3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Column(4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Column(6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        worksheet.Column(7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    private static void ConfigurePrint(IXLWorksheet worksheet, string? handlingMarking)
    {
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.Margins.Top = 0.35;
        worksheet.PageSetup.Margins.Bottom = 0.45;
        worksheet.PageSetup.Margins.Left = 0.3;
        worksheet.PageSetup.Margins.Right = 0.3;
        worksheet.PageSetup.SetRowsToRepeatAtTop(TableHeaderRow, TableHeaderRow);

        if (!string.IsNullOrWhiteSpace(handlingMarking))
        {
            worksheet.PageSetup.Header.Center.AddText(handlingMarking);
        }

        worksheet.PageSetup.Footer.Left.AddText("PRISM ERP · Simulator Development Division");
        worksheet.PageSetup.Footer.Right.AddText("Page &P of &N");
    }

    private static string NormalizeNarrative(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);
}
