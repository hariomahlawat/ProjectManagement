using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Api;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationAnalysisExcelBuilder
{
    public byte[] Build(ProliferationAnalysisResultDto report)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var workbook = new XLWorkbook();
        BuildSummarySheet(workbook, report);
        BuildProjectSheet(workbook, report);

        if (report.Units.Count > 0)
        {
            BuildUnitSheet(workbook, report);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildSummarySheet(XLWorkbook workbook, ProliferationAnalysisResultDto report)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.ShowGridlines = false;

        sheet.Range("A1:D1").Merge();
        sheet.Cell("A1").Value = "Proliferation Analysis";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 16;

        sheet.Cell("A3").Value = "Scope";
        sheet.Cell("B3").Value = report.ScopeLabel;
        sheet.Cell("A4").Value = "Period";
        sheet.Cell("B4").Value = report.PeriodLabel;
        sheet.Cell("A5").Value = "Source";
        sheet.Cell("B5").Value = report.SourceLabel;
        sheet.Cell("A6").Value = "Calculation basis";
        sheet.Cell("B6").Value = report.CalculationBasis;
        sheet.Range("B6:D6").Merge();
        sheet.Cell("A7").Value = "Unit-data coverage";
        sheet.Cell("B7").Value = report.CoverageMessage;
        sheet.Range("B7:D7").Merge();

        var labels = new[]
        {
            "Total proliferation",
            "SDD",
            "515 ABW",
            "Simulators with proliferation",
            "Receiving units recorded",
            "Approved annual quantity",
            "Approved detailed quantity"
        };
        var values = new[]
        {
            report.Summary.TotalProliferation,
            report.Summary.SddTotal,
            report.Summary.Abw515Total,
            report.Summary.ProjectCount,
            report.Summary.ReceivingUnitCount,
            report.Summary.ApprovedAnnualQuantity,
            report.Summary.ApprovedDetailedQuantity
        };

        const int firstMetricRow = 10;
        for (var index = 0; index < labels.Length; index++)
        {
            var row = firstMetricRow + index;
            sheet.Cell(row, 1).Value = labels[index];
            sheet.Cell(row, 2).Value = values[index];
            sheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
        }

        sheet.Range("A3:A7").Style.Font.Bold = true;
        sheet.Range(firstMetricRow, 1, firstMetricRow + labels.Length - 1, 1).Style.Font.Bold = true;
        sheet.Range(firstMetricRow, 1, firstMetricRow + labels.Length - 1, 2).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        sheet.Range(firstMetricRow, 1, firstMetricRow + labels.Length - 1, 2).Style.Border.BottomBorderColor = XLColor.LightGray;
        sheet.Range("B6:D7").Style.Alignment.WrapText = true;

        sheet.Column(1).Width = 32;
        sheet.Column(2).Width = 28;
        sheet.Column(3).Width = 22;
        sheet.Column(4).Width = 22;
        sheet.Rows(6, 7).AdjustToContents();
    }

    private static void BuildProjectSheet(XLWorkbook workbook, ProliferationAnalysisResultDto report)
    {
        var sheet = workbook.Worksheets.Add("Simulator breakdown");
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.ShowGridlines = false;

        var headers = new[]
        {
            "Simulator",
            "Code",
            "Technical category",
            "SDD",
            "515 ABW",
            "Total proliferation"
        };

        for (var index = 0; index < headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
        }

        StyleHeader(sheet.Range(1, 1, 1, headers.Length));

        var row = 2;
        foreach (var item in report.Projects)
        {
            sheet.Cell(row, 1).Value = item.ProjectName;
            sheet.Cell(row, 2).Value = item.ProjectCode ?? string.Empty;
            sheet.Cell(row, 3).Value = item.TechnicalCategory;
            sheet.Cell(row, 4).Value = item.SddQuantity;
            sheet.Cell(row, 5).Value = item.Abw515Quantity;
            sheet.Cell(row, 6).Value = item.TotalQuantity;
            row++;
        }

        if (row > 2)
        {
            sheet.Range(2, 4, row - 1, 6).Style.NumberFormat.Format = "#,##0";
            sheet.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Column(1).Width = 40;
        sheet.Column(2).Width = 18;
        sheet.Column(3).Width = 28;
        sheet.Columns(4, 6).Width = 18;
    }

    private static void BuildUnitSheet(XLWorkbook workbook, ProliferationAnalysisResultDto report)
    {
        var sheet = workbook.Worksheets.Add("Unit-wise breakdown");
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.ShowGridlines = false;

        var headers = new[]
        {
            "Receiving unit",
            "Simulator",
            "Code",
            "Source",
            "Quantity",
            "Entries",
            "First date",
            "Last date"
        };

        for (var index = 0; index < headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
        }

        StyleHeader(sheet.Range(1, 1, 1, headers.Length));

        var row = 2;
        foreach (var item in report.Units)
        {
            sheet.Cell(row, 1).Value = item.UnitName;
            sheet.Cell(row, 2).Value = item.ProjectName;
            sheet.Cell(row, 3).Value = item.ProjectCode ?? string.Empty;
            sheet.Cell(row, 4).Value = item.SourceLabel;
            sheet.Cell(row, 5).Value = item.Quantity;
            sheet.Cell(row, 6).Value = item.EntryCount;
            sheet.Cell(row, 7).Value = item.FirstDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 8).Value = item.LastDate.ToDateTime(TimeOnly.MinValue);
            row++;
        }

        if (row > 2)
        {
            sheet.Range(2, 5, row - 1, 6).Style.NumberFormat.Format = "#,##0";
            sheet.Range(2, 7, row - 1, 8).Style.NumberFormat.Format = "dd-mmm-yyyy";
            sheet.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Column(1).Width = 34;
        sheet.Column(2).Width = 40;
        sheet.Column(3).Width = 18;
        sheet.Column(4).Width = 14;
        sheet.Columns(5, 6).Width = 13;
        sheet.Columns(7, 8).Width = 16;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF0FF");
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorderColor = XLColor.FromHtml("#A8B7D8");
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }
}
