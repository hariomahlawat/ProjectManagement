using System.Globalization;
using ClosedXML.Excel;

namespace ProjectManagement.Services.Reports.ArppFyProjectUpdate;

public sealed class ArppFyProjectUpdateExcelBuilder
{
    private const int HeaderRowOne = 3;
    private const int HeaderRowTwo = 4;
    private const int FirstDataRow = 5;

    public byte[] Build(
        ArppFyProjectUpdateReport report,
        ArppFyProjectUpdatePresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var resolvedOptions = options ?? ArppFyProjectUpdatePresentationOptions.Default;
        var columns = ColumnLayout.For(resolvedOptions);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("ARPP Project Update");
        worksheet.Style.Font.FontName = "Arial";
        worksheet.Style.Font.FontSize = 9;

        var title = worksheet.Range(1, 1, 1, columns.ColumnCount).Merge();
        title.Value = report.FormalTitle;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 15;
        title.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(1).Height = 24;
        title.Style.Alignment.WrapText = false;

        worksheet.Row(2).Height = 7;
        BuildHeader(worksheet, columns);

        var rowIndex = FirstDataRow;
        foreach (var row in report.Rows)
        {
            worksheet.Cell(rowIndex, 1).SetValue(row.SerialNumber);
            worksheet.Cell(rowIndex, 2).SetValue(row.PppNumber ?? string.Empty);
            worksheet.Cell(rowIndex, 3).SetValue(row.ProjectName);
            SetDate(worksheet.Cell(rowIndex, 4), row.FirstArppListingDate);
            worksheet.Cell(rowIndex, 5).SetValue(row.DfpdsSchedule ?? string.Empty);
            worksheet.Cell(rowIndex, 6).SetValue(row.Cfa ?? string.Empty);
            worksheet.Cell(rowIndex, 7).SetValue(row.Establishment);
            SetDate(worksheet.Cell(rowIndex, columns.Aon), row.AonDate);
            if (columns.PresentStage.HasValue)
            {
                worksheet.Cell(rowIndex, columns.PresentStage.Value).SetValue(row.StageDisplay);
            }
            worksheet.Cell(rowIndex, columns.SupplyOrder).SetValue(SupplyOrderText(row));
            SetDate(worksheet.Cell(rowIndex, columns.Pdc), row.DevelopmentPdcDate);
            worksheet.Cell(rowIndex, columns.ProjectCase).SetValue(row.ProjectCaseDisplay);
            worksheet.Cell(rowIndex, columns.Remarks).SetValue(row.LatestExternalRemark ?? string.Empty);
            rowIndex++;
        }

        var lastDataRow = Math.Max(HeaderRowTwo, rowIndex - 1);
        var tableRange = worksheet.Range(HeaderRowOne, 1, lastDataRow, columns.ColumnCount);
        tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#8A99A8");
        tableRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#AAB7C6");
        tableRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        if (lastDataRow >= FirstDataRow)
        {
            var data = worksheet.Range(FirstDataRow, 1, lastDataRow, columns.ColumnCount);
            data.Style.Alignment.WrapText = true;
            worksheet.Range(FirstDataRow, 1, lastDataRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(FirstDataRow, 4, lastDataRow, columns.ProjectCase).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(FirstDataRow, 3, lastDataRow, 3).Style.Font.Bold = true;
        }

        ApplyColumnWidths(worksheet, columns);
        worksheet.SheetView.FreezeRows(HeaderRowTwo);
        worksheet.SheetView.FreezeColumns(3);
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.Margins.Top = 0.3;
        worksheet.PageSetup.Margins.Bottom = 0.4;
        worksheet.PageSetup.Margins.Left = 0.25;
        worksheet.PageSetup.Margins.Right = 0.25;
        worksheet.PageSetup.SetRowsToRepeatAtTop(HeaderRowOne, HeaderRowTwo);
        worksheet.PageSetup.Footer.Left.AddText("PRISM ERP · Simulator Development Division");
        worksheet.PageSetup.Footer.Right.AddText("Page &P of &N");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildHeader(IXLWorksheet worksheet, ColumnLayout columns)
    {
        MergeVertical(worksheet, 1, "Ser No.");
        MergeVertical(worksheet, 2, "ARPP No.");
        MergeVertical(worksheet, 3, "Name of Project");
        MergeVertical(worksheet, 4, "Dt of Grant of IPA / ARPP Listing");
        MergeVertical(worksheet, 5, "Sch");
        MergeVertical(worksheet, 6, "CFA");
        MergeVertical(worksheet, 7, "Est");

        var status = worksheet.Range(HeaderRowOne, columns.Aon, HeaderRowOne, columns.Pdc).Merge();
        status.Value = "Status";
        worksheet.Cell(HeaderRowTwo, columns.Aon).Value = "AoN";
        if (columns.PresentStage.HasValue)
        {
            worksheet.Cell(HeaderRowTwo, columns.PresentStage.Value).Value = "Present Stage";
        }
        worksheet.Cell(HeaderRowTwo, columns.SupplyOrder).Value = "SO amt & dt";
        worksheet.Cell(HeaderRowTwo, columns.Pdc).Value = "PDC dt";

        MergeVertical(worksheet, columns.ProjectCase, "Proj Case");
        MergeVertical(worksheet, columns.Remarks, "Remarks");

        var header = worksheet.Range(HeaderRowOne, 1, HeaderRowTwo, columns.ColumnCount);
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF5");
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.FromHtml("#17365D");
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
        worksheet.Row(HeaderRowOne).Height = 22;
        worksheet.Row(HeaderRowTwo).Height = 22;
    }

    private static void MergeVertical(IXLWorksheet worksheet, int column, string text)
    {
        var range = worksheet.Range(HeaderRowOne, column, HeaderRowTwo, column).Merge();
        range.Value = text;
    }

    private static void SetDate(IXLCell cell, DateOnly? value)
    {
        if (!value.HasValue)
        {
            cell.Clear(XLClearOptions.Contents);
            return;
        }

        cell.Value = value.Value.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = "dd mmm yyyy";
    }

    private static string SupplyOrderText(ArppFyProjectUpdateRow row)
    {
        var amount = row.SupplyOrderAmountInCrores.HasValue
            ? $"₹{row.SupplyOrderAmountInCrores.Value.ToString("0.##", CultureInfo.InvariantCulture)} Cr"
            : null;
        var date = row.SupplyOrderDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        return amount is not null && date is not null
            ? $"{amount}\n{date}"
            : amount ?? date ?? string.Empty;
    }

    private static void ApplyColumnWidths(IXLWorksheet worksheet, ColumnLayout columns)
    {
        worksheet.Column(1).Width = 7;
        worksheet.Column(2).Width = 12;
        worksheet.Column(3).Width = columns.PresentStage.HasValue ? 30 : 36;
        worksheet.Column(4).Width = 17;
        worksheet.Column(5).Width = 12;
        worksheet.Column(6).Width = 16;
        worksheet.Column(7).Width = 8;
        worksheet.Column(columns.Aon).Width = 14;
        if (columns.PresentStage.HasValue)
        {
            worksheet.Column(columns.PresentStage.Value).Width = 20;
        }
        worksheet.Column(columns.SupplyOrder).Width = 19;
        worksheet.Column(columns.Pdc).Width = 14;
        worksheet.Column(columns.ProjectCase).Width = 11;
        worksheet.Column(columns.Remarks).Width = columns.PresentStage.HasValue ? 46 : 54;
    }

    private sealed record ColumnLayout(
        int ColumnCount,
        int Aon,
        int? PresentStage,
        int SupplyOrder,
        int Pdc,
        int ProjectCase,
        int Remarks)
    {
        public static ColumnLayout For(ArppFyProjectUpdatePresentationOptions options)
            => options.IncludePresentStage
                ? new ColumnLayout(13, 8, 9, 10, 11, 12, 13)
                : new ColumnLayout(12, 8, null, 9, 10, 11, 12);
    }
}
