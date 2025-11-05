using System;
using System.Linq;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Utilities.Reporting
{
    /// <summary>
    /// Builds an Excel file for the "Projects ranked by proliferations" card.
    /// Single sheet, ordered exactly like the card.
    /// </summary>
    public sealed class ProliferationProjectsCardExcelWorkbookBuilder
    {
        public byte[] Build(ProliferationSummaryViewModel summary)
        {
            if (summary is null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Projects");

            // header
            ws.Cell(1, 1).Value = "Rank";
            ws.Cell(1, 2).Value = "Project";
            ws.Cell(1, 3).Value = "Code";
            ws.Cell(1, 4).Value = "Total";
            ws.Cell(1, 5).Value = "515 ABW";
            ws.Cell(1, 6).Value = "SDD";

            var ordered = summary.ByProject
                .OrderByDescending(p => p.Totals.Total)
                .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var row = 2;
            var rank = 1;
            foreach (var project in ordered)
            {
                ws.Cell(row, 1).Value = rank;
                ws.Cell(row, 2).Value = project.ProjectName;
                ws.Cell(row, 3).Value = project.ProjectCode ?? string.Empty;
                ws.Cell(row, 4).Value = project.Totals.Total;
                ws.Cell(row, 5).Value = project.Totals.Abw515;
                ws.Cell(row, 6).Value = project.Totals.Sdd;

                row++;
                rank++;
            }

            ws.Range(1, 1, 1, 6).Style.Font.SetBold(true);
            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }
    }
}
