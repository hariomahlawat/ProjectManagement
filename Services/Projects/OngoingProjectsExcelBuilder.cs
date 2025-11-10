using ClosedXML.Excel;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.Projects
{
    /// <summary>
    /// Builds an XLSX workbook for the "Ongoing projects" report.
    /// One row per project.
    /// Fixed columns + one column per stage (from StageCodes.All).
    /// Remarks column: only latest external remark.
    /// </summary>
    public sealed class OngoingProjectsExcelBuilder : IOngoingProjectsExcelBuilder
    {
        public byte[] Build(OngoingProjectsExportContext context)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Ongoing projects");

            var allStages = StageCodes.All; // your existing stage list

            var col = 1;
            ws.Cell(1, col++).Value = "Project name";
            ws.Cell(1, col++).Value = "Project category";
            ws.Cell(1, col++).Value = "Project officer";
            ws.Cell(1, col++).Value = "Current stage";
            ws.Cell(1, col++).Value = "Current stage date / PDC";
            ws.Cell(1, col++).Value = "Last completed stage";
            ws.Cell(1, col++).Value = "Last completed date";
            ws.Cell(1, col++).Value = "Latest external remark";

            // stage headers
            foreach (var stageCode in allStages)
            {
                ws.Cell(1, col++).Value = $"Stage {stageCode}";
            }

            var row = 2;
            foreach (var item in context.Items)
            {
                col = 1;
                ws.Cell(row, col++).Value = item.ProjectName;
                ws.Cell(row, col++).Value = item.ProjectCategoryName;
                ws.Cell(row, col++).Value = item.LeadPoName;

                // current stage (name)
                ws.Cell(row, col++).Value = item.CurrentStageName;

                // current stage date / PDC
                var currentStage = item.Stages.First(s => s.IsCurrent);
                if (currentStage.Status == StageStatus.Completed)
                {
                    ws.Cell(row, col++).Value =
                        currentStage.ActualCompletedOn?.ToString("dd-MMM-yyyy") ?? string.Empty;
                }
                else
                {
                    ws.Cell(row, col++).Value =
                        currentStage.PlannedDue.HasValue
                            ? $"PDC: {currentStage.PlannedDue.Value:dd-MMM-yyyy}"
                            : "PDC: N/A";
                }

                // last completed
                ws.Cell(row, col++).Value = item.LastCompletedStageName ?? string.Empty;
                ws.Cell(row, col++).Value =
                    item.LastCompletedStageDate?.ToString("dd-MMM-yyyy") ?? string.Empty;

                // latest external remark
                ws.Cell(row, col++).Value = item.LatestExternalRemark ?? string.Empty;

                //
                // >>> FIXED SECTION BELOW <<<
                //
                // Find the index of the last *actually* completed stage in the DTO list.
                // Everything BEFORE this index must be treated as completed for export.
                var lastCompletedIndex = -1;
                for (var i = 0; i < item.Stages.Count; i++)
                {
                    if (item.Stages[i].Status == StageStatus.Completed)
                    {
                        lastCompletedIndex = i;
                    }
                }

                // now write every stage column
                for (var stageIdx = 0; stageIdx < allStages.Length; stageIdx++)
                {
                    var stageCode = allStages[stageIdx];
                    var stageDto = item.Stages.FirstOrDefault(s =>
                        string.Equals(s.Code, stageCode, StringComparison.OrdinalIgnoreCase));

                    string text = string.Empty;

                    if (stageIdx <= lastCompletedIndex)
                    {
                        // this stage is at or before the last completed one
                        // => show actual date if we have it, otherwise blank
                        if (stageDto?.ActualCompletedOn is { } doneOn)
                        {
                            text = doneOn.ToString("dd-MMM-yyyy");
                        }
                        else
                        {
                            // DO NOT show PDC for historical/completed path
                            text = string.Empty;
                        }
                    }
                    else
                    {
                        // future or current-but-not-completed
                        if (stageDto is { Status: not StageStatus.Completed } &&
                            stageDto.PlannedDue.HasValue)
                        {
                            text = $"PDC: {stageDto.PlannedDue.Value:dd-MMM-yyyy}";
                        }
                    }

                    ws.Cell(row, col++).Value = text;
                }

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new System.IO.MemoryStream();
            wb.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
