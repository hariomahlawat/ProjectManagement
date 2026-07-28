using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Builds a normalized chronological workbook. Year data is kept in analytical tables
/// rather than creating one worksheet for every year.
/// </summary>
public sealed class ProliferationYearBreakdownCardExcelWorkbookBuilder
{
    public byte[] Build(
        ProliferationSummaryViewModel summary,
        ProliferationExportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(metadata);

        using var workbook = new XLWorkbook();
        ProliferationExcelWorkbookFormatter.ConfigureWorkbook(
            workbook,
            "Proliferation year-wise position",
            metadata);

        BuildSummarySheet(workbook, summary, metadata);
        BuildYearTotalsSheet(workbook, summary, metadata);
        BuildProjectYearSheet(workbook, summary, metadata);
        ProliferationExcelWorkbookFormatter.WriteQualitySheet(
            workbook,
            metadata,
            allTimeTotalsIncluded: false);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildSummarySheet(
        XLWorkbook workbook,
        ProliferationSummaryViewModel summary,
        ProliferationExportMetadata metadata)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        var nextRow = ProliferationExcelWorkbookFormatter.WriteHeading(
            sheet,
            "Proliferation year-wise position",
            "Approved proliferation assigned to valid chronological years.",
            metadata,
            8);

        nextRow = ProliferationExcelWorkbookFormatter.WriteDataQualityDisclosure(
            sheet,
            nextRow,
            8,
            metadata);

        var chronologicalTotal = summary.ByYear.Sum(row => row.Totals.Total);
        var chronologicalSdd = summary.ByYear.Sum(row => row.Totals.Sdd);
        var chronologicalAbw = summary.ByYear.Sum(row => row.Totals.Abw515);

        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 1, "Chronological total", chronologicalTotal);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 3, "Valid years", summary.ByYear.Count);
        ProliferationExcelWorkbookFormatter.WriteMetric(
            sheet,
            nextRow,
            5,
            "Projects in year-wise data",
            summary.ByProjectYear.Select(row => row.ProjectId).Distinct().Count());
        ProliferationExcelWorkbookFormatter.WriteMetric(
            sheet,
            nextRow,
            7,
            "Excluded invalid-year quantity",
            metadata.ChronologyQuality.ReportedQuantity);

        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow + 1, 1, "SDD", chronologicalSdd);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow + 1, 3, "515 ABW", chronologicalAbw);
        ProliferationExcelWorkbookFormatter.WriteMetric(
            sheet,
            nextRow + 1,
            5,
            "Project-year rows",
            summary.ByProjectYear.Count);
        ProliferationExcelWorkbookFormatter.WriteMetric(
            sheet,
            nextRow + 1,
            7,
            "Invalid-year approved records",
            metadata.ChronologyQuality.ApprovedRecordCount);

        var metricsRange = sheet.Range(nextRow, 1, nextRow + 1, 8);
        metricsRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        metricsRange.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        metricsRange.Style.Border.OutsideBorderColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Border);
        metricsRange.Style.Border.InsideBorderColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.LightBorder);

        sheet.Columns(1, 8).Width = 18;
        sheet.Column(1).Width = 24;
        sheet.Column(5).Width = 25;
        sheet.Column(7).Width = 28;
        ProliferationExcelWorkbookFormatter.ConfigurePrint(sheet, landscape: true);
    }

    private static void BuildYearTotalsSheet(
        XLWorkbook workbook,
        ProliferationSummaryViewModel summary,
        ProliferationExportMetadata metadata)
    {
        var sheet = workbook.Worksheets.Add("Year totals");
        var headerRow = ProliferationExcelWorkbookFormatter.WriteHeading(
            sheet,
            "Year totals",
            "Approved proliferation by valid reporting year.",
            metadata,
            4);

        var headers = new[] { "Year", "SDD", "515 ABW", "Total proliferation" };
        for (var column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(headerRow, column).Value = headers[column - 1];
        }
        ProliferationExcelWorkbookFormatter.StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var rows = summary.ByYear.OrderByDescending(row => row.Year).ToList();
        var rowNumber = headerRow + 1;
        foreach (var item in rows)
        {
            sheet.Cell(rowNumber, 1).Value = item.Year;
            sheet.Cell(rowNumber, 2).Value = item.Totals.Sdd;
            sheet.Cell(rowNumber, 3).Value = item.Totals.Abw515;
            sheet.Cell(rowNumber, 4).Value = item.Totals.Total;
            rowNumber++;
        }

        var lastDataRow = rowNumber - 1;
        ProliferationExcelWorkbookFormatter.CreateTable(
            sheet,
            headerRow,
            lastDataRow,
            headers.Length,
            "ProliferationYearTotalsTable");

        if (lastDataRow >= headerRow + 1)
        {
            sheet.Range(headerRow + 1, 1, lastDataRow, 1)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Range(headerRow + 1, 2, lastDataRow, 4)
                .Style.NumberFormat.Format = "#,##0";
            sheet.Range(headerRow + 1, 2, lastDataRow, 4)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        var totalRow = Math.Max(headerRow + 2, rowNumber + 1);
        sheet.Cell(totalRow, 1).Value = "Chronological total";
        sheet.Cell(totalRow, 2).Value = rows.Sum(row => row.Totals.Sdd);
        sheet.Cell(totalRow, 3).Value = rows.Sum(row => row.Totals.Abw515);
        sheet.Cell(totalRow, 4).Value = rows.Sum(row => row.Totals.Total);
        sheet.Range(totalRow, 1, totalRow, 4).Style.Font.Bold = true;
        sheet.Range(totalRow, 2, totalRow, 4).Style.NumberFormat.Format = "#,##0";
        sheet.Range(totalRow, 1, totalRow, 4).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Column(1).Width = 14;
        sheet.Columns(2, 4).Width = 20;
        ProliferationExcelWorkbookFormatter.ConfigurePrint(sheet, landscape: false, repeatingHeaderRow: headerRow);
    }

    private static void BuildProjectYearSheet(
        XLWorkbook workbook,
        ProliferationSummaryViewModel summary,
        ProliferationExportMetadata metadata)
    {
        var sheet = workbook.Worksheets.Add("Project-year data");
        var headerRow = ProliferationExcelWorkbookFormatter.WriteHeading(
            sheet,
            "Project-year data",
            "One row per simulator and valid reporting year for analysis and PivotTables.",
            metadata,
            5);

        var headers = new[]
        {
            "Year",
            "Project",
            "SDD",
            "515 ABW",
            "Total proliferation"
        };
        for (var column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(headerRow, column).Value = headers[column - 1];
        }
        ProliferationExcelWorkbookFormatter.StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var rows = summary.ByProjectYear
            .OrderByDescending(row => row.Year)
            .ThenByDescending(row => row.Totals.Total)
            .ThenBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rowNumber = headerRow + 1;
        foreach (var item in rows)
        {
            sheet.Cell(rowNumber, 1).Value = item.Year;
            sheet.Cell(rowNumber, 2).Value = item.ProjectName;
            sheet.Cell(rowNumber, 3).Value = item.Totals.Sdd;
            sheet.Cell(rowNumber, 4).Value = item.Totals.Abw515;
            sheet.Cell(rowNumber, 5).Value = item.Totals.Total;
            rowNumber++;
        }

        var lastDataRow = rowNumber - 1;
        ProliferationExcelWorkbookFormatter.CreateTable(
            sheet,
            headerRow,
            lastDataRow,
            headers.Length,
            "ProliferationProjectYearTable");

        if (lastDataRow >= headerRow + 1)
        {
            sheet.Range(headerRow + 1, 1, lastDataRow, 1)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Range(headerRow + 1, 3, lastDataRow, 5)
                .Style.NumberFormat.Format = "#,##0";
            sheet.Range(headerRow + 1, 3, lastDataRow, 5)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        sheet.SheetView.FreezeRows(headerRow);
        sheet.SheetView.FreezeColumns(1);
        sheet.Column(1).Width = 12;
        sheet.Column(2).Width = 62;
        sheet.Columns(3, 5).Width = 18;
        sheet.Column(2).Style.Alignment.WrapText = true;
        ProliferationExcelWorkbookFormatter.ConfigurePrint(sheet, landscape: true, repeatingHeaderRow: headerRow);
    }
}
