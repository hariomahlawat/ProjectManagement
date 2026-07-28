using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationAnalysisExcelBuilder
{
    public byte[] Build(
        ProliferationAnalysisResultDto report,
        ProliferationExportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(metadata);

        using var workbook = new XLWorkbook();
        ProliferationExcelWorkbookFormatter.ConfigureWorkbook(
            workbook,
            "Proliferation analysis",
            metadata);

        BuildSummarySheet(workbook, report, metadata);
        BuildProjectSheet(workbook, report, metadata);

        if (metadata.IncludesUnitSummary)
        {
            BuildUnitSheet(workbook, report, metadata);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildSummarySheet(
        XLWorkbook workbook,
        ProliferationAnalysisResultDto report,
        ProliferationExportMetadata metadata)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        var nextRow = ProliferationExcelWorkbookFormatter.WriteHeading(
            sheet,
            "Proliferation analysis",
            "Filtered analytical position generated from approved PRISM records.",
            metadata,
            8);

        sheet.Cell(nextRow, 1).Value = "Scope";
        sheet.Cell(nextRow, 2).Value = report.ScopeLabel;
        sheet.Cell(nextRow, 3).Value = "Period";
        sheet.Cell(nextRow, 4).Value = report.PeriodLabel;
        sheet.Cell(nextRow, 5).Value = "Source";
        sheet.Cell(nextRow, 6).Value = report.SourceLabel;
        sheet.Cell(nextRow, 7).Value = "Unit summary";
        sheet.Cell(nextRow, 8).Value = metadata.IncludesUnitSummary ? "Included" : "Not included";

        var contextRange = sheet.Range(nextRow, 1, nextRow, 8);
        for (var column = 1; column <= 8; column += 2)
        {
            sheet.Cell(nextRow, column).Style.Font.Bold = true;
            sheet.Cell(nextRow, column).Style.Font.FontColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Navy);
            sheet.Cell(nextRow, column).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F7FA");
        }
        contextRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        contextRange.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        contextRange.Style.Border.OutsideBorderColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Border);
        contextRange.Style.Border.InsideBorderColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.LightBorder);
        contextRange.Style.Alignment.WrapText = true;

        nextRow += 2;
        var basisRange = sheet.Range(nextRow, 1, nextRow, 8).Merge();
        basisRange.Value = $"Calculation basis: {report.CalculationBasis}";
        basisRange.Style.Alignment.WrapText = true;
        basisRange.Style.Font.FontColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Muted);

        var coverageRange = sheet.Range(nextRow + 1, 1, nextRow + 1, 8).Merge();
        coverageRange.Value = $"Unit-data coverage: {report.CoverageMessage}";
        coverageRange.Style.Alignment.WrapText = true;
        coverageRange.Style.Font.FontColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Muted);

        nextRow += 3;
        nextRow = ProliferationExcelWorkbookFormatter.WriteDataQualityDisclosure(
            sheet,
            nextRow,
            8,
            metadata);

        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 1, "Total proliferation", report.Summary.TotalProliferation);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 3, "SDD", report.Summary.SddTotal);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 5, "515 ABW", report.Summary.Abw515Total);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow, 7, "Simulators", report.Summary.ProjectCount);

        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow + 1, 1, "Technical categories", report.Summary.TechnicalCategoryCount);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow + 1, 3, "Approved annual quantity", report.Summary.ApprovedAnnualQuantity);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow + 1, 5, "Approved detailed quantity", report.Summary.ApprovedDetailedQuantity);
        ProliferationExcelWorkbookFormatter.WriteMetric(sheet, nextRow + 1, 7, "Receiving units", report.Summary.ReceivingUnitCount);

        var metricsRange = sheet.Range(nextRow, 1, nextRow + 1, 8);
        metricsRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        metricsRange.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        metricsRange.Style.Border.OutsideBorderColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Border);
        metricsRange.Style.Border.InsideBorderColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.LightBorder);

        sheet.Columns(1, 8).Width = 18;
        sheet.Column(1).Width = 24;
        sheet.Column(3).Width = 24;
        sheet.Column(5).Width = 24;
        sheet.Column(7).Width = 24;
        sheet.Rows(1, nextRow + 2).AdjustToContents();
        ProliferationExcelWorkbookFormatter.ConfigurePrint(sheet, landscape: true);
    }

    private static void BuildProjectSheet(
        XLWorkbook workbook,
        ProliferationAnalysisResultDto report,
        ProliferationExportMetadata metadata)
    {
        var sheet = workbook.Worksheets.Add("Simulator breakdown");
        var headerRow = ProliferationExcelWorkbookFormatter.WriteHeading(
            sheet,
            "Simulator breakdown",
            $"{report.ScopeLabel} · {report.PeriodLabel} · {report.SourceLabel}",
            metadata,
            5);

        var headers = new[]
        {
            "Simulator",
            "Technical category",
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

        var rowNumber = headerRow + 1;
        foreach (var item in report.Projects)
        {
            sheet.Cell(rowNumber, 1).Value = item.ProjectName;
            sheet.Cell(rowNumber, 2).Value = item.TechnicalCategory;
            sheet.Cell(rowNumber, 3).Value = item.SddQuantity;
            sheet.Cell(rowNumber, 4).Value = item.Abw515Quantity;
            sheet.Cell(rowNumber, 5).Value = item.TotalQuantity;
            rowNumber++;
        }

        var lastDataRow = rowNumber - 1;
        ProliferationExcelWorkbookFormatter.CreateTable(
            sheet,
            headerRow,
            lastDataRow,
            headers.Length,
            "SimulatorBreakdownTable");

        if (lastDataRow >= headerRow + 1)
        {
            sheet.Range(headerRow + 1, 3, lastDataRow, 5).Style.NumberFormat.Format = "#,##0";
            sheet.Range(headerRow + 1, 3, lastDataRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        var totalRow = Math.Max(headerRow + 2, rowNumber + 1);
        sheet.Cell(totalRow, 1).Value = "Report total";
        sheet.Cell(totalRow, 3).Value = report.Summary.SddTotal;
        sheet.Cell(totalRow, 4).Value = report.Summary.Abw515Total;
        sheet.Cell(totalRow, 5).Value = report.Summary.TotalProliferation;
        sheet.Range(totalRow, 1, totalRow, 5).Style.Font.Bold = true;
        sheet.Range(totalRow, 3, totalRow, 5).Style.NumberFormat.Format = "#,##0";
        sheet.Range(totalRow, 1, totalRow, 5).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        sheet.Range(totalRow, 1, totalRow, 5).Style.Border.TopBorderColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Border);

        sheet.SheetView.FreezeRows(headerRow);
        sheet.SheetView.FreezeColumns(1);
        sheet.Column(1).Width = 58;
        sheet.Column(2).Width = 30;
        sheet.Columns(3, 5).Width = 18;
        sheet.Columns(1, 2).Style.Alignment.WrapText = true;
        ProliferationExcelWorkbookFormatter.ConfigurePrint(sheet, landscape: true, repeatingHeaderRow: headerRow);
    }

    private static void BuildUnitSheet(
        XLWorkbook workbook,
        ProliferationAnalysisResultDto report,
        ProliferationExportMetadata metadata)
    {
        var sheet = workbook.Worksheets.Add("Unit summary");
        var headerRow = ProliferationExcelWorkbookFormatter.WriteHeading(
            sheet,
            "Unit summary",
            "Rows consolidate approved detailed entries by receiving unit, simulator and source.",
            metadata,
            7);

        var coverage = sheet.Range(headerRow, 1, headerRow, 7).Merge();
        coverage.Value = report.CoverageMessage;
        coverage.Style.Alignment.WrapText = true;
        coverage.Style.Font.FontColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Muted);
        headerRow += 2;

        var headers = new[]
        {
            "Receiving unit",
            "Simulator",
            "Source",
            "Quantity",
            "Entries",
            "First date",
            "Last date"
        };

        for (var column = 1; column <= headers.Length; column++)
        {
            sheet.Cell(headerRow, column).Value = headers[column - 1];
        }
        ProliferationExcelWorkbookFormatter.StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var rowNumber = headerRow + 1;
        foreach (var item in report.Units)
        {
            sheet.Cell(rowNumber, 1).Value = item.UnitName;
            sheet.Cell(rowNumber, 2).Value = item.ProjectName;
            sheet.Cell(rowNumber, 3).Value = item.SourceLabel;
            sheet.Cell(rowNumber, 4).Value = item.Quantity;
            sheet.Cell(rowNumber, 5).Value = item.EntryCount;
            ProliferationExcelWorkbookFormatter.WriteChronologyDate(
                sheet.Cell(rowNumber, 6),
                item.FirstDate,
                report.MinimumValidYear,
                report.MaximumValidYear);
            ProliferationExcelWorkbookFormatter.WriteChronologyDate(
                sheet.Cell(rowNumber, 7),
                item.LastDate,
                report.MinimumValidYear,
                report.MaximumValidYear);
            rowNumber++;
        }

        var lastDataRow = rowNumber - 1;
        ProliferationExcelWorkbookFormatter.CreateTable(
            sheet,
            headerRow,
            lastDataRow,
            headers.Length,
            "UnitSummaryTable");

        if (lastDataRow >= headerRow + 1)
        {
            sheet.Range(headerRow + 1, 4, lastDataRow, 5).Style.NumberFormat.Format = "#,##0";
            sheet.Range(headerRow + 1, 4, lastDataRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }
        else
        {
            var emptyRow = headerRow + 1;
            var emptyRange = sheet.Range(emptyRow, 1, emptyRow, 7).Merge();
            emptyRange.Value = "No approved detailed entries with a usable receiving-unit name were available for the selected report.";
            emptyRange.Style.Font.FontColor = XLColor.FromHtml(ProliferationExcelWorkbookFormatter.Muted);
            emptyRange.Style.Alignment.WrapText = true;
        }

        var totalRow = Math.Max(headerRow + 3, rowNumber + 1);
        sheet.Cell(totalRow, 1).Value = "Unit-summary total";
        sheet.Cell(totalRow, 4).Value = report.Units.Sum(item => item.Quantity);
        sheet.Cell(totalRow, 5).Value = report.Units.Sum(item => item.EntryCount);
        sheet.Range(totalRow, 1, totalRow, 7).Style.Font.Bold = true;
        sheet.Range(totalRow, 4, totalRow, 5).Style.NumberFormat.Format = "#,##0";
        sheet.Range(totalRow, 1, totalRow, 7).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        sheet.SheetView.FreezeRows(headerRow);
        sheet.SheetView.FreezeColumns(1);
        sheet.Column(1).Width = 36;
        sheet.Column(2).Width = 52;
        sheet.Column(3).Width = 15;
        sheet.Columns(4, 5).Width = 13;
        sheet.Columns(6, 7).Width = 16;
        sheet.Columns(1, 3).Style.Alignment.WrapText = true;
        ProliferationExcelWorkbookFormatter.ConfigurePrint(sheet, landscape: true, repeatingHeaderRow: headerRow);
    }
}
