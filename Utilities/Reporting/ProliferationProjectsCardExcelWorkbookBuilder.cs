using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Builds the professional all-project proliferation totals workbook used by the Overview page.
/// </summary>
public sealed class ProliferationProjectsCardExcelWorkbookBuilder
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
            "Proliferation project totals",
            metadata);

        BuildSummarySheet(workbook, summary, metadata);
        BuildProjectTotalsSheet(workbook, summary, metadata);
        ProliferationExcelWorkbookFormatter.WriteQualitySheet(
            workbook,
            metadata,
            allTimeTotalsIncluded: true);

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
            "Proliferation project totals",
            "Approved all-time proliferation ranked by simulator.",
            metadata,
            8);

        nextRow = ProliferationExcelWorkbookFormatter.WriteDataQualityDisclosure(
            sheet,
            nextRow,
            8,
            metadata);

        var total = summary.ByProject.Sum(row => row.Totals.Total);
        var sdd = summary.ByProject.Sum(row => row.Totals.Sdd);
        var abw = summary.ByProject.Sum(row => row.Totals.Abw515);
        var chronologicalTotal = summary.ByYear.Sum(row => row.Totals.Total);

        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 1, "Total proliferation", total);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 3, "Projects", summary.ByProject.Count);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 5, "Valid chronological years", summary.ByYear.Count);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 7, "Chronological total", chronologicalTotal);

        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow + 1, 1, "SDD", sdd);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow + 1, 3, "515 ABW", abw);
        ProliferationExcelWorkbookFormatter.WriteMetric(
            sheet,
            nextRow + 1,
            5,
            "Quantity outside valid years",
            metadata.ChronologyQuality.ReportedQuantity);
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

        var noteRow = nextRow + 4;
        var note = sheet.Range(noteRow, 1, noteRow, 8).Merge();
        note.Value = "Project totals are authoritative all-time totals. The Project totals sheet is ordered by total proliferation, highest first.";
        note.Style.Font.FontColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Muted);
        note.Style.Alignment.WrapText = true;

        sheet.Columns(1, 8).Width = 18;
        sheet.Column(1).Width = 24;
        sheet.Column(3).Width = 18;
        sheet.Column(5).Width = 25;
        sheet.Column(7).Width = 24;
        ProliferationExcelWorkbookFormatter.ConfigurePrint(sheet, landscape: true);
    }

    private static void BuildProjectTotalsSheet(
        XLWorkbook workbook,
        ProliferationSummaryViewModel summary,
        ProliferationExportMetadata metadata)
    {
        var sheet = workbook.Worksheets.Add("Project totals");
        var headerRow = ProliferationExcelWorkbookFormatter.WriteHeading(
            sheet,
            "Project totals",
            "Approved all-time proliferation. Rows are ranked by total proliferation.",
            metadata,
            5);

        var headers = new[]
        {
            "S.No.",
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
        sheet.Row(headerRow).Height = 28;

        var ordered = summary.ByProject
            .OrderByDescending(row => row.Totals.Total)
            .ThenBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rowNumber = headerRow + 1;
        for (var index = 0; index < ordered.Count; index++)
        {
            var project = ordered[index];
            sheet.Cell(rowNumber, 1).Value = index + 1;
            sheet.Cell(rowNumber, 2).Value = project.ProjectName;
            sheet.Cell(rowNumber, 3).Value = project.Totals.Sdd;
            sheet.Cell(rowNumber, 4).Value = project.Totals.Abw515;
            sheet.Cell(rowNumber, 5).Value = project.Totals.Total;
            rowNumber++;
        }

        var lastDataRow = rowNumber - 1;
        ProliferationExcelWorkbookFormatter.CreateTable(
            sheet,
            headerRow,
            lastDataRow,
            headers.Length,
            "ProliferationProjectTotalsTable");

        if (lastDataRow >= headerRow + 1)
        {
            sheet.Range(headerRow + 1, 1, lastDataRow, 1)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Range(headerRow + 1, 3, lastDataRow, 5)
                .Style.NumberFormat.Format = "#,##0";
            sheet.Range(headerRow + 1, 3, lastDataRow, 5)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        var totalsRow = Math.Max(headerRow + 2, rowNumber + 1);
        sheet.Cell(totalsRow, 2).Value = "Grand total";
        sheet.Cell(totalsRow, 3).Value = ordered.Sum(row => row.Totals.Sdd);
        sheet.Cell(totalsRow, 4).Value = ordered.Sum(row => row.Totals.Abw515);
        sheet.Cell(totalsRow, 5).Value = ordered.Sum(row => row.Totals.Total);
        sheet.Range(totalsRow, 2, totalsRow, 5).Style.Font.Bold = true;
        sheet.Range(totalsRow, 3, totalsRow, 5).Style.NumberFormat.Format = "#,##0";
        sheet.Range(totalsRow, 2, totalsRow, 5).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        sheet.Range(totalsRow, 2, totalsRow, 5).Style.Border.TopBorderColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Border);

        sheet.SheetView.FreezeRows(headerRow);
        sheet.SheetView.FreezeColumns(1);
        sheet.Column(1).Width = 9;
        sheet.Column(2).Width = 62;
        sheet.Columns(3, 5).Width = 18;
        sheet.Column(2).Style.Alignment.WrapText = true;
        ProliferationExcelWorkbookFormatter.ConfigurePrint(sheet, landscape: true, repeatingHeaderRow: headerRow);
    }
}
