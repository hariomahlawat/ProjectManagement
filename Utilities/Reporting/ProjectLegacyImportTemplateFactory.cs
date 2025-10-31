using ClosedXML.Excel;

namespace ProjectManagement.Utilities.Reporting;

public static class ProjectLegacyImportTemplateFactory
{
    private static readonly string[] Headers =
    {
        "SNo",
        "Nomenclature",
        "ArmService",
        "YearOfDevp",
        "CostLakhs"
    };

    public static XLWorkbook CreateWorkbook()
    {
        var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Projects");

        for (var i = 0; i < Headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = Headers[i];
        }

        var headerRange = worksheet.Range(1, 1, 1, Headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(0xE5, 0xF1, 0xFB);

        worksheet.Columns().AdjustToContents();
        worksheet.SheetView.FreezeRows(1);

        return workbook;
    }
}
