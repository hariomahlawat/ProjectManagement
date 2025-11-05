using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Utilities.Reporting
{
    /// <summary>
    /// Builds an Excel file for the "Project breakdown by year" card.
    /// One sheet per year, ordered descending.
    /// </summary>
    public sealed class ProliferationYearBreakdownCardExcelWorkbookBuilder
    {
        public byte[] Build(ProliferationSummaryViewModel summary)
        {
            if (summary is null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            using var workbook = new XLWorkbook();

            // group first so we can detect "no data" safely
            var grouped = summary.ByProjectYear
                .GroupBy(x => x.Year)
                .OrderByDescending(g => g.Key)
                .ToList();

            // if there's no project-year data at all, create a placeholder sheet
            if (grouped.Count == 0)
            {
                var wsEmpty = workbook.AddWorksheet("No data");
                wsEmpty.Cell(1, 1).Value = "No project breakdown data is available.";
                wsEmpty.Range(1, 1, 1, 1).Style.Font.SetBold(true);

                using var msEmpty = new MemoryStream();
                workbook.SaveAs(msEmpty);
                return msEmpty.ToArray();
            }

            foreach (var yearGroup in grouped)
            {
                // sheet name must be safe and short
                var sheetName = yearGroup.Key.ToString();
                if (sheetName.Length > 31)
                {
                    sheetName = sheetName[..31];
                }

                var ws = workbook.AddWorksheet(sheetName);

                ws.Cell(1, 1).Value = "Project";
                ws.Cell(1, 2).Value = "Code";
                ws.Cell(1, 3).Value = "Total";
                ws.Cell(1, 4).Value = "515 ABW";
                ws.Cell(1, 5).Value = "SDD";

                var ordered = yearGroup
                    .OrderByDescending(r => r.Totals.Total)
                    .ThenBy(r => r.ProjectName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var row = 2;
                foreach (var item in ordered)
                {
                    ws.Cell(row, 1).Value = item.ProjectName;
                    ws.Cell(row, 2).Value = item.ProjectCode ?? string.Empty;
                    ws.Cell(row, 3).Value = item.Totals.Total;
                    ws.Cell(row, 4).Value = item.Totals.Abw515;
                    ws.Cell(row, 5).Value = item.Totals.Sdd;
                    row++;
                }

                // optional totals from summary.ByYear
                var yearTotals = summary.ByYear.FirstOrDefault(y => y.Year == yearGroup.Key);
                if (yearTotals is not null)
                {
                    var totalsRow = row + 1;
                    ws.Cell(totalsRow, 1).Value = "Year total";
                    ws.Cell(totalsRow, 3).Value = yearTotals.Totals.Total;
                    ws.Cell(totalsRow, 4).Value = yearTotals.Totals.Abw515;
                    ws.Cell(totalsRow, 5).Value = yearTotals.Totals.Sdd;
                    ws.Range(totalsRow, 1, totalsRow, 5).Style.Font.SetBold(true);
                }

                ws.Range(1, 1, 1, 5).Style.Font.SetBold(true);
                ws.Columns().AdjustToContents();
            }

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }
    }
}
