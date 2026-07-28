using System.Globalization;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

internal static class ProliferationExcelWorkbookFormatter
{
    internal const string Navy = "#17365D";
    internal const string BlueHeader = "#DCE6F1";
    internal const string Border = "#AAB7C6";
    internal const string LightBorder = "#D8E0E8";
    internal const string Muted = "#526174";
    internal const string WarningFill = "#FFF4CE";
    internal const string WarningText = "#7A5200";

    public static DateTimeOffset ToIst(DateTimeOffset value)
        => TimeZoneInfo.ConvertTime(value, TimeZoneHelper.GetIst());

    public static void ConfigureWorkbook(
        XLWorkbook workbook,
        string title,
        ProliferationExportMetadata metadata)
    {
        workbook.Properties.Title = title;
        workbook.Properties.Subject = "Approved proliferation reporting from PRISM ERP";
        workbook.Properties.Author = metadata.GeneratedBy;
        workbook.Properties.Comments = "Generated from approved PRISM ERP proliferation records.";
    }

    public static int WriteHeading(
        IXLWorksheet sheet,
        string title,
        string subtitle,
        ProliferationExportMetadata metadata,
        int columnCount)
    {
        sheet.Style.Font.FontName = "Arial";
        sheet.Style.Font.FontSize = 10;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.PageSetup.ShowGridlines = false;

        var titleRange = sheet.Range(1, 1, 1, columnCount).Merge();
        titleRange.Value = title;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 18;
        titleRange.Style.Font.FontColor = XLColor.FromHtml(Navy);
        sheet.Row(1).Height = 28;

        var subtitleRange = sheet.Range(2, 1, 2, columnCount).Merge();
        subtitleRange.Value = subtitle;
        subtitleRange.Style.Font.FontColor = XLColor.FromHtml(Muted);
        subtitleRange.Style.Alignment.WrapText = true;

        var generatedAtIst = ToIst(metadata.GeneratedAtUtc);
        var generatedRange = sheet.Range(3, 1, 3, columnCount).Merge();
        generatedRange.Value = $"Generated {generatedAtIst:dd MMM yyyy, HH:mm:ss} IST by {metadata.GeneratedBy}";
        generatedRange.Style.Font.FontColor = XLColor.FromHtml(Muted);

        var basisRange = sheet.Range(4, 1, 4, columnCount).Merge();
        basisRange.Value = "Data basis: approved records only · Authoritative totals follow the configured project/source/year counting rule.";
        basisRange.Style.Font.FontColor = XLColor.FromHtml(Muted);
        basisRange.Style.Alignment.WrapText = true;

        return 6;
    }

    public static int WriteDataQualityDisclosure(
        IXLWorksheet sheet,
        int row,
        int columnCount,
        ProliferationExportMetadata metadata)
    {
        var range = sheet.Range(row, 1, row, columnCount).Merge();
        range.Value = metadata.DataQualityMessage;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = XLColor.FromHtml(Border);
        range.Style.Fill.BackgroundColor = metadata.ChronologyQuality.HasIssues
            ? XLColor.FromHtml(WarningFill)
            : XLColor.FromHtml("#EEF7EE");
        range.Style.Font.FontColor = metadata.ChronologyQuality.HasIssues
            ? XLColor.FromHtml(WarningText)
            : XLColor.FromHtml("#2E6B32");
        sheet.Row(row).Height = metadata.ChronologyQuality.HasIssues ? 36 : 24;
        return row + 2;
    }


    public static void WriteChronologyDate(
        IXLCell cell,
        DateOnly value,
        int minimumValidYear,
        int maximumValidYear)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var isChronologicallyValid = value.Year >= minimumValidYear
            && value.Year <= maximumValidYear;

        if (isChronologicallyValid)
        {
            cell.Value = value.ToDateTime(TimeOnly.MinValue);
            cell.Style.NumberFormat.Format = "dd-mmm-yyyy";
            return;
        }

        // Invalid chronology values are deliberately written as text. Excel stores
        // dates as serial numbers and cannot safely represent legacy sentinel dates
        // such as 01-Jan-0001. Writing the original value as text preserves the
        // source record and prevents ClosedXML from failing the entire workbook.
        cell.Style.NumberFormat.Format = "@";
        cell.Value = value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(WarningFill);
        cell.Style.Font.FontColor = XLColor.FromHtml(WarningText);
    }

    public static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.FromHtml(Navy);
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(BlueHeader);
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.TopBorderColor = XLColor.FromHtml(Border);
        range.Style.Border.BottomBorderColor = XLColor.FromHtml("#7F91A5");
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
    }

    public static void CreateTable(
        IXLWorksheet sheet,
        int headerRow,
        int lastRow,
        int lastColumn,
        string tableName)
    {
        if (lastRow <= headerRow)
        {
            return;
        }

        var table = sheet.Range(headerRow, 1, lastRow, lastColumn).CreateTable(tableName);
        table.Theme = XLTableTheme.TableStyleMedium2;
        table.ShowAutoFilter = true;
        table.ShowRowStripes = true;
    }

    public static void ConfigurePrint(
        IXLWorksheet sheet,
        bool landscape,
        int? repeatingHeaderRow = null)
    {
        sheet.PageSetup.PageOrientation = landscape
            ? XLPageOrientation.Landscape
            : XLPageOrientation.Portrait;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.Margins.Top = 0.35;
        sheet.PageSetup.Margins.Bottom = 0.45;
        sheet.PageSetup.Margins.Left = 0.3;
        sheet.PageSetup.Margins.Right = 0.3;

        if (repeatingHeaderRow.HasValue)
        {
            sheet.PageSetup.SetRowsToRepeatAtTop(repeatingHeaderRow.Value, repeatingHeaderRow.Value);
        }

        sheet.PageSetup.Footer.Left.AddText("PRISM ERP · Simulator Development Division");
        sheet.PageSetup.Footer.Right.AddText("Page &P of &N");
    }

    public static void WriteMetric(
        IXLWorksheet sheet,
        int row,
        int labelColumn,
        string label,
        int value)
    {
        var labelCell = sheet.Cell(row, labelColumn);
        labelCell.Value = label;
        labelCell.Style.Font.Bold = true;
        labelCell.Style.Font.FontColor = XLColor.FromHtml(Navy);
        labelCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F7FA");

        var valueCell = sheet.Cell(row, labelColumn + 1);
        valueCell.Value = value;
        valueCell.Style.NumberFormat.Format = "#,##0";
        valueCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    public static void WriteQualitySheet(
        XLWorkbook workbook,
        ProliferationExportMetadata metadata,
        bool allTimeTotalsIncluded)
    {
        var sheet = workbook.Worksheets.Add("Data quality");
        var nextRow = WriteHeading(
            sheet,
            "Proliferation data quality",
            "Chronological reporting disclosure for this workbook.",
            metadata,
            4);

        sheet.Cell(nextRow, 1).Value = "Supported chronological years";
        sheet.Cell(nextRow, 2).Value = $"{metadata.ChronologyQuality.MinimumValidYear}–{metadata.ChronologyQuality.MaximumValidYear}";
        sheet.Cell(nextRow + 1, 1).Value = "Approved records outside range";
        sheet.Cell(nextRow + 1, 2).Value = metadata.ChronologyQuality.ApprovedRecordCount;
        sheet.Cell(nextRow + 2, 1).Value = "Affected project/source/year positions";
        sheet.Cell(nextRow + 2, 2).Value = metadata.ChronologyQuality.AffectedPositionCount;
        sheet.Cell(nextRow + 3, 1).Value = "Authoritative reported quantity affected";
        sheet.Cell(nextRow + 3, 2).Value = metadata.ChronologyQuality.ReportedQuantity;
        sheet.Cell(nextRow + 4, 1).Value = "Treatment in this workbook";
        sheet.Cell(nextRow + 4, 2).Value = allTimeTotalsIncluded
            ? "Included in all-time/project totals; excluded from chronological sheets."
            : "Excluded from chronological totals and detailed year-wise rows.";

        sheet.Range(nextRow, 1, nextRow + 4, 1).Style.Font.Bold = true;
        sheet.Range(nextRow, 1, nextRow + 4, 2).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        sheet.Range(nextRow, 1, nextRow + 4, 2).Style.Border.BottomBorderColor = XLColor.FromHtml(LightBorder);
        sheet.Range(nextRow + 1, 2, nextRow + 3, 2).Style.NumberFormat.Format = "#,##0";
        sheet.Range(nextRow, 2, nextRow + 4, 2).Style.Alignment.WrapText = true;

        var noteRow = nextRow + 7;
        WriteDataQualityDisclosure(sheet, noteRow, 4, metadata);

        sheet.Column(1).Width = 40;
        sheet.Column(2).Width = 72;
        sheet.Columns(3, 4).Width = 12;
        ConfigurePrint(sheet, landscape: false);
    }
}
