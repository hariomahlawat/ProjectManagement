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
    string? TechnicalCategory,
    string? TechStatus,
    bool? AvailableForProliferation,
    bool? TotCompleted,
    int? CompletedYear,
    string? Search,
    string? Build,
    string? PortfolioStatus);

// SECTION: Builder contract
public interface ICompletedProjectsSummaryExcelBuilder
{
    byte[] Build(CompletedProjectsSummaryExportContext context);
}

// SECTION: Builder implementation
public sealed class CompletedProjectsSummaryExcelBuilder : ICompletedProjectsSummaryExcelBuilder
{
    private const int FirstDataRow = 2;
    private const int ColumnCount = 15;

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
            "Technical category",
            "Build type",
            "Completed",
            "Development / L1 cost (lakh)",
            "Proliferation cost (lakh)",
            "Latest LPP (lakh)",
            "Latest LPP date",
            "Technology status",
            "Availability for proliferation",
            "ToT status",
            "Critical data gaps",
            "Supplementary data gaps",
            "Remarks"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239);
        worksheet.SheetView.FreezeRows(1);
        worksheet.SheetView.FreezeColumns(2);
    }

    // SECTION: Row rendering
    private static void WriteRows(IXLWorksheet worksheet, IReadOnlyList<CompletedProjectSummaryDto> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var rowNumber = FirstDataRow + index;
            var item = items[index];

            worksheet.Cell(rowNumber, 1).Value = index + 1;
            worksheet.Cell(rowNumber, 2).Value = item.Name;
            worksheet.Cell(rowNumber, 3).Value = item.TechnicalCategoryName ?? string.Empty;
            worksheet.Cell(rowNumber, 4).Value = item.BuildType;

            var completion = item.FormatCompletion(string.Empty);
            if (!string.IsNullOrWhiteSpace(completion))
            {
                worksheet.Cell(rowNumber, 5).Value = completion;
            }

            WriteDecimal(worksheet.Cell(rowNumber, 6), item.RdCostLakhs);
            WriteDecimal(worksheet.Cell(rowNumber, 7), item.ProliferationCostLakhs);
            WriteDecimal(worksheet.Cell(rowNumber, 8), item.LatestLpp?.Amount);

            if (item.LatestLpp?.Date is { } lppDate)
            {
                worksheet.Cell(rowNumber, 9).Value = lppDate.ToDateTime(TimeOnly.MinValue);
                worksheet.Cell(rowNumber, 9).Style.DateFormat.Format = "dd-MMM-yyyy";
            }

            worksheet.Cell(rowNumber, 10).Value = CompletedProjectPortfolioPolicy.GetTechnologyLabel(item.TechStatus);
            worksheet.Cell(rowNumber, 11).Value = CompletedProjectPortfolioPolicy.GetAvailabilityLabel(item.AvailableForProliferation);
            worksheet.Cell(rowNumber, 12).Value = CompletedProjectPortfolioPolicy.GetTotLabel(item.TotStatus);
            worksheet.Cell(rowNumber, 13).Value = string.Join(", ", CompletedProjectPortfolioPolicy.GetCriticalMissingFields(item));
            worksheet.Cell(rowNumber, 14).Value = string.Join(", ", CompletedProjectPortfolioPolicy.GetSupplementaryMissingFields(item));
            worksheet.Cell(rowNumber, 15).Value = item.Remarks ?? string.Empty;
        }
    }

    // SECTION: Formatting helpers
    private static void ApplyFormatting(IXLWorksheet worksheet, CompletedProjectsSummaryExportContext context)
    {
        var lastDataRow = Math.Max(FirstDataRow, context.Items.Count + 1);
        worksheet.Columns(1, ColumnCount).AdjustToContents(1, lastDataRow);

        foreach (var column in worksheet.Columns(1, ColumnCount))
        {
            if (column.Width > 60)
            {
                column.Width = 60;
            }
        }

        worksheet.Column(1).Width = 8;
        worksheet.Column(2).Width = Math.Clamp(worksheet.Column(2).Width, 28, 46);
        worksheet.Column(3).Width = Math.Clamp(worksheet.Column(3).Width, 18, 30);
        worksheet.Column(4).Width = 12;
        worksheet.Column(5).Width = 16;
        worksheet.Column(6).Width = 18;
        worksheet.Column(7).Width = 21;
        worksheet.Column(8).Width = 18;
        worksheet.Column(9).Width = 16;
        worksheet.Columns(10, 12).Width = 20;
        worksheet.Columns(13, 14).Width = 34;
        worksheet.Column(15).Width = 55;

        worksheet.Columns(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Columns(4, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Columns(6, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        worksheet.Columns(9, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Columns(13, 15).Style.Alignment.WrapText = true;
        worksheet.Columns(1, ColumnCount).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        if (context.Items.Count > 0)
        {
            var dataRange = worksheet.Range(1, 1, context.Items.Count + 1, ColumnCount);
            ApplyGridBorders(dataRange, XLColor.FromArgb(218, 224, 233));
            worksheet.Range(FirstDataRow, 1, context.Items.Count + 1, ColumnCount)
                .Style.Font.FontSize = 10;
            dataRange.SetAutoFilter();
        }

        WriteMetadata(worksheet, context);
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.Margins.Top = 0.45;
        worksheet.PageSetup.Margins.Bottom = 0.45;
        worksheet.PageSetup.Margins.Left = 0.35;
        worksheet.PageSetup.Margins.Right = 0.35;
        worksheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
    }

    private static void ApplyGridBorders(IXLRange range, XLColor borderColor)
    {
        var border = range.Style.Border;

        // ClosedXML 0.104 exposes the individual border sides on IXLBorder.
        // Applying them to the range produces a consistent, light grid without
        // relying on version-specific aggregate-border members.
        border.TopBorder = XLBorderStyleValues.Thin;
        border.RightBorder = XLBorderStyleValues.Thin;
        border.BottomBorder = XLBorderStyleValues.Thin;
        border.LeftBorder = XLBorderStyleValues.Thin;

        border.TopBorderColor = borderColor;
        border.RightBorderColor = borderColor;
        border.BottomBorderColor = borderColor;
        border.LeftBorderColor = borderColor;
    }

    private static void WriteMetadata(IXLWorksheet worksheet, CompletedProjectsSummaryExportContext context)
    {
        var metadataRow = context.Items.Count + 3;
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());

        worksheet.Cell(metadataRow, 1).Value = "Export generated";
        worksheet.Cell(metadataRow, 2).Value = generatedAtIst.DateTime;
        worksheet.Cell(metadataRow, 2).Style.DateFormat.Format = "yyyy-MM-dd HH:mm\" IST\"";

        var metadata = new (string Label, string Value)[]
        {
            ("Technical category", context.TechnicalCategory ?? "(all)"),
            ("Technology status", context.TechStatus ?? "(all)"),
            ("Availability for proliferation", context.AvailableForProliferation switch
            {
                true => "Yes",
                false => "No",
                _ => "(all)"
            }),
            ("ToT filter", FormatTotFilter(context.TotCompleted)),
            ("Completed year", context.CompletedYear?.ToString(CultureInfo.InvariantCulture) ?? "(all)"),
            ("Build type", context.Build ?? "(all)"),
            ("Portfolio focus", CompletedProjectPortfolioStatusCodes.GetLabel(context.PortfolioStatus)),
            ("Search", string.IsNullOrWhiteSpace(context.Search) ? "(none)" : context.Search)
        };

        for (var index = 0; index < metadata.Length; index++)
        {
            worksheet.Cell(metadataRow + index + 1, 1).Value = metadata[index].Label;
            worksheet.Cell(metadataRow + index + 1, 2).Value = metadata[index].Value;
        }

        worksheet.Range(metadataRow, 1, metadataRow + metadata.Length, 1).Style.Font.Bold = true;
        worksheet.Range(metadataRow, 1, metadataRow + metadata.Length, 2).Style.Font.FontSize = 9;
    }

    private static void WriteDecimal(IXLCell cell, decimal? value)
    {
        if (value.HasValue)
        {
            cell.Value = (double)value.Value;
            cell.Style.NumberFormat.Format = "0.00";
        }
        else
        {
            cell.Clear(XLClearOptions.Contents);
        }
    }

    private static string FormatTotFilter(bool? totCompleted) => totCompleted switch
    {
        true => "Completed",
        false => "Not completed",
        _ => "(all)"
    };
}
