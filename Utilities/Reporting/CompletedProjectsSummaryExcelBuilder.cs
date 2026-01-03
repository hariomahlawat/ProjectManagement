using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

// SECTION: Export context
public sealed record CompletedProjectsSummaryExportContext(
    IReadOnlyList<CompletedProjectSummaryDto> Items,
    DateTimeOffset GeneratedAtUtc,
    string? TechStatus,
    bool? AvailableForProliferation,
    bool? TotCompleted,
    int? CompletedYear,
    string? Search);

// SECTION: Builder contract
public interface ICompletedProjectsSummaryExcelBuilder
{
    byte[] Build(CompletedProjectsSummaryExportContext context);
}

// SECTION: Builder implementation
public sealed class CompletedProjectsSummaryExcelBuilder : ICompletedProjectsSummaryExcelBuilder
{
    private const string TotCompletedLabel = "Completed";
    private const string TotNotCompletedLabel = "Not completed";

    public byte[] Build(CompletedProjectsSummaryExportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Items);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Completed Projects");

        WriteHeader(worksheet);
        WriteRows(worksheet, context.Items);
        ApplyFormatting(worksheet, context);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // SECTION: Header rendering
    private static void WriteHeader(IXLWorksheet worksheet)
    {
        var headers = new[]
        {
            "S.No.",
            "Project",
            "R&D / L1 cost (lakh)",
            "Approx production cost (lakh)",
            "Latest LPP (lakh)",
            "Latest LPP date",
            "Tech status",
            "Available for proliferation",
            "ToT status",
            "Remarks"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = headers[column];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239);
        worksheet.SheetView.FreezeRows(1);
    }

    // SECTION: Row rendering
    private static void WriteRows(IXLWorksheet worksheet, IReadOnlyList<CompletedProjectSummaryDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var rowNumber = 2;
        for (var index = 0; index < items.Count; index++, rowNumber++)
        {
            var item = items[index];
            worksheet.Cell(rowNumber, 1).Value = index + 1;
            worksheet.Cell(rowNumber, 2).Value = item.Name;

            var rdCostCell = worksheet.Cell(rowNumber, 3);
            if (item.RdCostLakhs.HasValue)
            {
                rdCostCell.Value = (double)item.RdCostLakhs.Value;
            }
            else
            {
                rdCostCell.Clear(XLClearOptions.Contents);
            }
            rdCostCell.Style.NumberFormat.Format = "0.00";

            var productionCell = worksheet.Cell(rowNumber, 4);
            if (item.ApproxProductionCost.HasValue)
            {
                productionCell.Value = (double)item.ApproxProductionCost.Value;
            }
            else
            {
                productionCell.Clear(XLClearOptions.Contents);
            }
            productionCell.Style.NumberFormat.Format = "0.00";

            var lppCell = worksheet.Cell(rowNumber, 5);
            if (item.LatestLpp is { } latestLpp)
            {
                lppCell.Value = (double)latestLpp.Amount;
            }
            else
            {
                lppCell.Clear(XLClearOptions.Contents);
            }
            lppCell.Style.NumberFormat.Format = "0.00";

            if (item.LatestLpp?.Date is { } lppDate)
            {
                worksheet.Cell(rowNumber, 6).Value = lppDate.ToDateTime(TimeOnly.MinValue);
                worksheet.Cell(rowNumber, 6).Style.DateFormat.Format = "dd-MMM-yyyy";
            }

            worksheet.Cell(rowNumber, 7).Value = item.TechStatus ?? string.Empty;
            worksheet.Cell(rowNumber, 8).Value = item.AvailableForProliferation switch
            {
                true => "Yes",
                false => "No",
                _ => string.Empty
            };
            worksheet.Cell(rowNumber, 9).Value = FormatTotStatus(item.TotStatus);
            worksheet.Cell(rowNumber, 10).Value = item.Remarks ?? string.Empty;
        }
    }

    // SECTION: Formatting helpers
    private static void ApplyFormatting(IXLWorksheet worksheet, CompletedProjectsSummaryExportContext context)
    {
        var lastRow = Math.Max(2, context.Items.Count + 1);
        worksheet.Columns(1, 10).AdjustToContents(1, lastRow);

        foreach (var column in worksheet.Columns(1, 10))
        {
            if (column.Width > 60)
            {
                column.Width = 60;
            }
        }

        worksheet.Column(2).Width = Math.Min(worksheet.Column(2).Width, 40);
        worksheet.Column(10).Style.Alignment.WrapText = true;
        worksheet.Column(10).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        var metadataRow = context.Items.Count + 3;
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());

        worksheet.Cell(metadataRow, 1).Value = "Export generated";
        worksheet.Cell(metadataRow, 2).Value = generatedAtIst.DateTime;
        worksheet.Cell(metadataRow, 2).Style.DateFormat.Format = "yyyy-MM-dd HH:mm\" IST\"";

        worksheet.Cell(metadataRow + 1, 1).Value = "Tech status";
        worksheet.Cell(metadataRow + 1, 2).Value = context.TechStatus ?? "(all)";

        worksheet.Cell(metadataRow + 2, 1).Value = "Available";
        worksheet.Cell(metadataRow + 2, 2).Value = context.AvailableForProliferation switch
        {
            true => "Yes",
            false => "No",
            _ => "(all)"
        };

        worksheet.Cell(metadataRow + 3, 1).Value = "ToT status";
        worksheet.Cell(metadataRow + 3, 2).Value = FormatTotFilter(context.TotCompleted);

        worksheet.Cell(metadataRow + 4, 1).Value = "Completed year";
        worksheet.Cell(metadataRow + 4, 2).Value = context.CompletedYear?.ToString(CultureInfo.InvariantCulture) ?? "(all)";

        worksheet.Cell(metadataRow + 5, 1).Value = "Search";
        worksheet.Cell(metadataRow + 5, 2).Value = string.IsNullOrWhiteSpace(context.Search) ? "(none)" : context.Search;

        worksheet.Range(metadataRow, 1, metadataRow + 5, 1).Style.Font.Bold = true;
    }

    // SECTION: ToT status formatting
    private static string FormatTotStatus(Models.ProjectTotStatus? totStatus)
    {
        if (totStatus == Models.ProjectTotStatus.Completed)
        {
            return TotCompletedLabel;
        }

        return totStatus.HasValue ? TotNotCompletedLabel : string.Empty;
    }

    private static string FormatTotFilter(bool? totCompleted)
    {
        return totCompleted switch
        {
            true => TotCompletedLabel,
            false => TotNotCompletedLabel,
            _ => "(all)"
        };
    }
}
