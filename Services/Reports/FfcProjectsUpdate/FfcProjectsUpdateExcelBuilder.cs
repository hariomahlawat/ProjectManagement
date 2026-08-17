using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Services.Ffc;

namespace ProjectManagement.Services.Reports.FfcProjectsUpdate;

public static class FfcProjectsUpdateExcelBuilder
{
    private const int TitleRow = 1;
    private const int HeaderRow = 3;

    public static byte[] Build(
        FfcProjectsUpdateReport report,
        FfcProjectsUpdatePresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var resolved = options ?? FfcProjectsUpdatePresentationOptions.Default;
        var columnCount = resolved.IncludeOverallStatus ? 7 : 6;

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("FFC Projects Update");

        worksheet.Style.Font.FontName = "Arial";
        worksheet.Style.Font.FontSize = 10;
        worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        var title = worksheet.Range(TitleRow, 1, TitleRow, columnCount).Merge();
        title.Value = FfcProjectsUpdateReport.FormalTitle;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 16;
        title.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Row(TitleRow).Height = 25;

        BuildHeader(worksheet, resolved, columnCount);

        var rowIndex = HeaderRow + 1;
        foreach (var group in report.Groups)
        {
            var groupRow = rowIndex++;
            var groupRange = worksheet.Range(groupRow, 1, groupRow, columnCount).Merge();
            groupRange.Value = $"{group.CountryName} – {group.Year}";
            groupRange.Style.Font.Bold = true;
            groupRange.Style.Font.FontColor = XLColor.FromHtml("#17365D");
            groupRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F5F8");
            groupRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            groupRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            groupRange.Style.Border.TopBorderColor = XLColor.FromHtml("#AAB7C6");
            groupRange.Style.Border.BottomBorderColor = XLColor.FromHtml("#AAB7C6");

            var groupDataStart = rowIndex;

            foreach (var row in group.Rows)
            {
                worksheet.Cell(rowIndex, 1).SetValue(row.Serial);
                worksheet.Cell(rowIndex, 2).SetValue(row.ProjectName);

                if (row.CostInCr.HasValue)
                {
                    worksheet.Cell(rowIndex, 3).SetValue(row.CostInCr.Value * 100m);
                }

                worksheet.Cell(rowIndex, 4).SetValue(row.Quantity);
                worksheet.Cell(rowIndex, 5).SetValue(row.Status);
                worksheet.Cell(rowIndex, 6).SetValue(Narrative(row.ProgressText));

                if (resolved.IncludeOverallStatus && rowIndex == groupDataStart)
                {
                    worksheet.Cell(rowIndex, 7).SetValue(Narrative(group.OverallRemarks));
                }

                rowIndex++;
            }

            if (resolved.IncludeOverallStatus && group.Rows.Count > 1)
            {
                var overallRange = worksheet.Range(groupDataStart, 7, rowIndex - 1, 7).Merge();
                overallRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                overallRange.Style.Alignment.WrapText = true;
            }
        }

        var lastRow = Math.Max(HeaderRow, rowIndex - 1);
        ApplyFormatting(worksheet, lastRow, columnCount, resolved);
        ConfigureColumns(worksheet, resolved);
        ConfigurePrint(worksheet);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildHeader(
        IXLWorksheet worksheet,
        FfcProjectsUpdatePresentationOptions options,
        int columnCount)
    {
        var headings = options.IncludeOverallStatus
            ? new[] { "S. No.", "Project", "Cost (₹ lakh)", "Quantity", "Status", "Current progress", "Overall status" }
            : new[] { "S. No.", "Project", "Cost (₹ lakh)", "Quantity", "Status", "Current progress" };

        for (var column = 1; column <= headings.Length; column++)
        {
            worksheet.Cell(HeaderRow, column).SetValue(headings[column - 1]);
        }

        var header = worksheet.Range(HeaderRow, 1, HeaderRow, columnCount);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF5");
        header.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        header.Style.Border.TopBorderColor = XLColor.FromHtml("#8A99A8");
        header.Style.Border.BottomBorderColor = XLColor.FromHtml("#8A99A8");
        header.Style.Alignment.WrapText = true;
        worksheet.Row(HeaderRow).Height = 25;
    }

    private static void ApplyFormatting(
        IXLWorksheet worksheet,
        int lastRow,
        int columnCount,
        FfcProjectsUpdatePresentationOptions options)
    {
        if (lastRow > HeaderRow)
        {
            var tableRange = worksheet.Range(HeaderRow, 1, lastRow, columnCount);
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            tableRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#AAB7C6");
            tableRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#D8E0E8");
            tableRange.Style.Alignment.WrapText = true;
        }

        worksheet.Column(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Column(3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        worksheet.Column(3).Style.NumberFormat.Format = "#,##0.00";
        worksheet.Column(4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        worksheet.Column(4).Style.NumberFormat.Format = "#,##0";

        worksheet.SheetView.FreezeRows(HeaderRow);
    }

    private static void ConfigureColumns(
        IXLWorksheet worksheet,
        FfcProjectsUpdatePresentationOptions options)
    {
        worksheet.Column(1).Width = 8;
        worksheet.Column(2).Width = options.IncludeOverallStatus ? 34 : 40;
        worksheet.Column(3).Width = 15;
        worksheet.Column(4).Width = 11;
        worksheet.Column(5).Width = 24;
        worksheet.Column(6).Width = options.IncludeOverallStatus ? 58 : 82;

        if (options.IncludeOverallStatus)
        {
            worksheet.Column(7).Width = 64;
        }
    }

    private static void ConfigurePrint(IXLWorksheet worksheet)
    {
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.Margins.Top = 0.35;
        worksheet.PageSetup.Margins.Bottom = 0.45;
        worksheet.PageSetup.Margins.Left = 0.3;
        worksheet.PageSetup.Margins.Right = 0.3;
        worksheet.PageSetup.SetRowsToRepeatAtTop(TitleRow, HeaderRow);
        worksheet.PageSetup.Footer.Left.AddText("PRISM ERP · Simulator Development Division");
        worksheet.PageSetup.Footer.Right.AddText("Page &P of &N");
    }

    private static string Narrative(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);
}
