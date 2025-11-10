using ClosedXML.Excel;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProjectManagement.Services.Projects
{
    /// <summary>
    /// Builds an XLSX workbook for the "Ongoing projects" report.
    /// One row per project, and one column per stage (from StageCodes.All).
    /// Remarks column: only latest external remark.
    /// </summary>
    public sealed class OngoingProjectsExcelBuilder : IOngoingProjectsExcelBuilder
    {
        public byte[] Build(OngoingProjectsExportContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Ongoing projects");

            // ----------------------
            // 1. header
            // ----------------------
            var headers = new List<string>
            {
                "Project name",
                "Project category",
                "Project officer",
                "Current stage",
                "Current stage date / PDC",
                "Last completed stage",
                "Last completed date",
                "Latest external remark"
            };

            // dynamic stage columns
            var allStages = StageCodes.All;
            headers.AddRange(allStages.Select(c => $"Stage {c}"));

            for (var i = 0; i < headers.Count; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }

            // ----------------------
            // 2. rows
            // ----------------------
            var row = 2;
            foreach (var item in context.Items)
            {
                var col = 1;

                ws.Cell(row, col++).Value = item.ProjectName;
                ws.Cell(row, col++).Value = item.ProjectCategoryName ?? string.Empty;
                ws.Cell(row, col++).Value = item.LeadPoName ?? string.Empty;

                // current stage
                var current = item.Stages.FirstOrDefault(s => s.IsCurrent)
                              ?? item.Stages.FirstOrDefault();

                ws.Cell(row, col++).Value = current?.Name ?? string.Empty;
                ws.Cell(row, col++).Value = GetCurrentStageDateText(current);

                // last completed
                ws.Cell(row, col++).Value = item.LastCompletedStageName ?? string.Empty;
                ws.Cell(row, col++).Value = item.LastCompletedStageDate?.ToString("dd-MMM-yyyy") ?? string.Empty;

                // latest external remark
                ws.Cell(row, col++).Value = item.LatestExternalRemark ?? string.Empty;

                // stage-by-stage columns
                foreach (var stageCode in allStages)
                {
                    var stageDto = item.Stages.FirstOrDefault(s =>
                        string.Equals(s.Code, stageCode, StringComparison.OrdinalIgnoreCase));

                    var text = string.Empty;

                    if (stageDto != null)
                    {
                        // your rule: completed ⇒ show actual date if we have it
                        if (stageDto.Status == StageStatus.Completed)
                        {
                            text = stageDto.ActualCompletedOn?.ToString("dd-MMM-yyyy") ?? string.Empty;
                        }
                        else
                        {
                            // future / not completed ⇒ show PDC if present
                            if (stageDto.PlannedDue.HasValue)
                            {
                                text = $"PDC: {stageDto.PlannedDue.Value:dd-MMM-yyyy}";
                            }
                        }
                    }

                    ws.Cell(row, col++).Value = text;
                }

                row++;
            }

            // ----------------------
            // 3. tidy up
            // ----------------------
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return stream.ToArray();
        }

        private static string GetCurrentStageDateText(OngoingProjectStageDto? stage)
        {
            if (stage == null)
                return string.Empty;

            if (stage.Status == StageStatus.Completed)
                return stage.ActualCompletedOn?.ToString("dd-MMM-yyyy") ?? "date NA";

            if (stage.PlannedDue.HasValue)
                return $"PDC: {stage.PlannedDue.Value:dd-MMM-yyyy}";

            return "PDC: N/A";
        }
    }
}
