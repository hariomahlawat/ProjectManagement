using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Utilities.Reporting
{
    // SECTION: Excel report builder
    public sealed class ProliferationReportExcelWorkbookBuilder : IProliferationReportExcelWorkbookBuilder
    {
        public byte[] Build(
            ProliferationReportKind report,
            IReadOnlyList<(string Key, string Label)> columns,
            IReadOnlyList<IDictionary<string, object?>> rows,
            string title,
            IDictionary<string, string> filters)
        {
            // SECTION: Parameter guard (unused report for now)
            _ = report;

            using var workbook = new XLWorkbook();

            // SECTION: Header
            var sheet = workbook.Worksheets.Add("Report");
            sheet.Cell(1, 1).Value = title;
            sheet.Cell(2, 1).Value = $"Generated on (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}";

            // SECTION: Filters
            var filterRow = 4;
            foreach (var kv in filters)
            {
                sheet.Cell(filterRow, 1).Value = kv.Key;
                sheet.Cell(filterRow, 2).Value = kv.Value;
                filterRow++;
            }

            // SECTION: Column headers
            var headerRow = filterRow + 1;
            for (var i = 0; i < columns.Count; i++)
            {
                sheet.Cell(headerRow, i + 1).Value = columns[i].Label;
                sheet.Cell(headerRow, i + 1).Style.Font.Bold = true;
            }

            // SECTION: Rows
            var rowIndex = headerRow + 1;
            foreach (var row in rows)
            {
                for (var i = 0; i < columns.Count; i++)
                {
                    var key = columns[i].Key;
                    row.TryGetValue(key, out var value);
                    sheet.Cell(rowIndex, i + 1).Value = value?.ToString() ?? "";
                }

                rowIndex++;
            }

            sheet.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }
    }
}
