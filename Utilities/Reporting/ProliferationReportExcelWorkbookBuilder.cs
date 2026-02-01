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
            _ = report;

            using var workbook = new XLWorkbook();

            // SECTION: Header
            var sheet = workbook.Worksheets.Add("Report");
            sheet.Cell(1, 1).Value = title;
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(2, 1).Value = $"Generated on (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}";

            // SECTION: Filters
            var filterRow = 4;
            foreach (var kv in filters)
            {
                sheet.Cell(filterRow, 1).Value = kv.Key;
                sheet.Cell(filterRow, 1).Style.Font.Bold = true;
                sheet.Cell(filterRow, 2).Value = kv.Value ?? string.Empty;
                filterRow++;
            }

            // SECTION: Column headers
            var headerRow = filterRow + 1;
            for (var i = 0; i < columns.Count; i++)
            {
                sheet.Cell(headerRow, i + 1).Value = columns[i].Label;
                sheet.Cell(headerRow, i + 1).Style.Font.Bold = true;
            }

            // SECTION: Sheet controls
            sheet.SheetView.FreezeRows(headerRow);
            if (columns.Count > 0)
            {
                sheet.Range(headerRow, 1, headerRow, columns.Count).SetAutoFilter();
            }

            // SECTION: Data rows
            var rowIndex = headerRow + 1;

            var dateFormatted = new bool[columns.Count];
            var numberFormatted = new bool[columns.Count];

            foreach (var row in rows)
            {
                for (var i = 0; i < columns.Count; i++)
                {
                    var key = columns[i].Key;
                    row.TryGetValue(key, out var value);

                    var cell = sheet.Cell(rowIndex, i + 1);

                    if (value is null)
                    {
                        cell.Value = string.Empty;
                        continue;
                    }

                    switch (value)
                    {
                        case DateTime dt:
                            cell.Value = dt;
                            if (!dateFormatted[i])
                            {
                                sheet.Column(i + 1).Style.NumberFormat.Format = "yyyy-mm-dd";
                                dateFormatted[i] = true;
                            }
                            break;

                        case DateTimeOffset dto:
                            cell.Value = dto.UtcDateTime;
                            if (!dateFormatted[i])
                            {
                                sheet.Column(i + 1).Style.NumberFormat.Format = "yyyy-mm-dd";
                                dateFormatted[i] = true;
                            }
                            break;

                        case int intValue:
                            cell.Value = intValue;
                            if (!numberFormatted[i])
                            {
                                sheet.Column(i + 1).Style.NumberFormat.Format = "0";
                                numberFormatted[i] = true;
                            }
                            break;

                        case long longValue:
                            cell.Value = longValue;
                            if (!numberFormatted[i])
                            {
                                sheet.Column(i + 1).Style.NumberFormat.Format = "0";
                                numberFormatted[i] = true;
                            }
                            break;

                        case short shortValue:
                            cell.Value = shortValue;
                            if (!numberFormatted[i])
                            {
                                sheet.Column(i + 1).Style.NumberFormat.Format = "0";
                                numberFormatted[i] = true;
                            }
                            break;

                        case byte byteValue:
                            cell.Value = byteValue;
                            if (!numberFormatted[i])
                            {
                                sheet.Column(i + 1).Style.NumberFormat.Format = "0";
                                numberFormatted[i] = true;
                            }
                            break;

                        case decimal decimalValue:
                            cell.Value = decimalValue;
                            if (!numberFormatted[i])
                            {
                                sheet.Column(i + 1).Style.NumberFormat.Format = "0";
                                numberFormatted[i] = true;
                            }
                            break;

                        case double doubleValue:
                            cell.Value = doubleValue;
                            if (!numberFormatted[i])
                            {
                                sheet.Column(i + 1).Style.NumberFormat.Format = "0";
                                numberFormatted[i] = true;
                            }
                            break;

                        case float floatValue:
                            cell.Value = floatValue;
                            if (!numberFormatted[i])
                            {
                                sheet.Column(i + 1).Style.NumberFormat.Format = "0";
                                numberFormatted[i] = true;
                            }
                            break;

                        case bool b:
                            cell.Value = b;
                            break;

                        default:
                            cell.Value = value.ToString() ?? string.Empty;
                            break;
                    }
                }

                rowIndex++;
            }

            // SECTION: Column sizing
            var lastRowToMeasure = Math.Min(rowIndex, headerRow + 200);
            if (columns.Count > 0)
            {
                sheet.Columns(1, columns.Count).AdjustToContents(1, lastRowToMeasure);
            }

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }
    }
}
