using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

public interface IProliferationExcelWorkbookBuilder
{
    byte[] Build(ProliferationExcelWorkbookContext context);
}

public sealed record ProliferationExcelWorkbookContext(
    IReadOnlyList<ProliferationExcelRow> Rows,
    DateTimeOffset GeneratedAtUtc,
    string RequestedByUserId,
    ProliferationExportFilterMetadata Filters);

public sealed record ProliferationExcelRow(
    int Year,
    string Project,
    string? ProjectCode,
    ProliferationSource Source,
    string DataType,
    string? UnitName,
    DateOnly? Date,
    int Quantity,
    int EffectiveTotal,
    string ApprovalStatus,
    YearPreferenceMode? PreferenceMode);

public sealed record ProliferationExportFilterMetadata(
    IReadOnlyList<int> Years,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string SourceLabel,
    string ProjectCategoryLabel,
    string TechnicalCategoryLabel,
    string? SearchTerm);

public sealed class ProliferationExcelWorkbookBuilder : IProliferationExcelWorkbookBuilder
{
    public byte[] Build(ProliferationExcelWorkbookContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Proliferation Overview");
        WriteHeader(worksheet);
        WriteRows(worksheet, context.Rows);
        ApplyMetadata(worksheet, context);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet worksheet)
    {
        var headers = new[]
        {
            "Year",
            "Project",
            "Source",
            "Data type",
            "Unit",
            "Date",
            "Quantity",
            "Effective total",
            "Approval state",
            "Preference mode"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = headers[column];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239);
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.SheetView.FreezeRows(1);
    }

    private static void WriteRows(IXLWorksheet worksheet, IReadOnlyList<ProliferationExcelRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var rowNumber = index + 2;
            var row = rows[index];
            worksheet.Cell(rowNumber, 1).Value = row.Year;
            worksheet.Cell(rowNumber, 2).Value = string.IsNullOrWhiteSpace(row.ProjectCode)
                ? row.Project
                : $"{row.Project} ({row.ProjectCode})";
            worksheet.Cell(rowNumber, 3).Value = row.Source.ToDisplayName();
            worksheet.Cell(rowNumber, 4).Value = row.DataType;
            worksheet.Cell(rowNumber, 5).Value = row.UnitName ?? string.Empty;
            if (row.Date.HasValue)
            {
                worksheet.Cell(rowNumber, 6).Value = row.Date.Value.ToDateTime(TimeOnly.MinValue);
                worksheet.Cell(rowNumber, 6).Style.DateFormat.Format = "yyyy-MM-dd";
            }
            worksheet.Cell(rowNumber, 7).Value = row.Quantity;
            worksheet.Cell(rowNumber, 8).Value = row.EffectiveTotal;
            worksheet.Cell(rowNumber, 9).Value = row.ApprovalStatus;
            var preferenceDisplay = row.PreferenceMode ??
                (row.Source == ProliferationSource.Abw515
                    ? YearPreferenceMode.UseYearly
                    : YearPreferenceMode.UseYearlyAndGranular);
            worksheet.Cell(rowNumber, 10).Value = preferenceDisplay.ToString();
        }

        worksheet.Columns(2, 5).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        worksheet.Column(2).Style.Alignment.WrapText = true;
    }

    private static void ApplyMetadata(IXLWorksheet worksheet, ProliferationExcelWorkbookContext context)
    {
        var lastRow = Math.Max(2, context.Rows.Count + 1);
        worksheet.Columns(1, 10).AdjustToContents(1, lastRow);

        foreach (var column in worksheet.Columns(1, 10))
        {
            if (column.Width > 60)
            {
                column.Width = 60;
            }
        }

        var metadataRow = context.Rows.Count + 3;
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());

        worksheet.Cell(metadataRow, 1).Value = "Export generated";
        worksheet.Cell(metadataRow, 2).Value = generatedAtIst.DateTime;
        worksheet.Cell(metadataRow, 2).Style.DateFormat.Format = "yyyy-MM-dd HH:mm\" IST\"";

        worksheet.Cell(metadataRow + 1, 1).Value = "Requested by";
        worksheet.Cell(metadataRow + 1, 2).Value = string.IsNullOrWhiteSpace(context.RequestedByUserId)
            ? "(unknown)"
            : context.RequestedByUserId;

        var filters = context.Filters;
        worksheet.Cell(metadataRow + 2, 1).Value = "Years";
        worksheet.Cell(metadataRow + 2, 2).Value = filters.Years.Count > 0
            ? string.Join(", ", filters.Years)
            : "(all years)";

        worksheet.Cell(metadataRow + 3, 1).Value = "Date range";
        worksheet.Cell(metadataRow + 3, 2).Value = filters.FromDate.HasValue || filters.ToDate.HasValue
            ? $"{filters.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "start"} → {filters.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "end"}"
            : "(not set)";

        worksheet.Cell(metadataRow + 4, 1).Value = "Source";
        worksheet.Cell(metadataRow + 4, 2).Value = string.IsNullOrWhiteSpace(filters.SourceLabel)
            ? "(all sources)"
            : filters.SourceLabel;

        worksheet.Cell(metadataRow + 5, 1).Value = "Project category";
        worksheet.Cell(metadataRow + 5, 2).Value = string.IsNullOrWhiteSpace(filters.ProjectCategoryLabel)
            ? "(all categories)"
            : filters.ProjectCategoryLabel;

        worksheet.Cell(metadataRow + 6, 1).Value = "Technical category";
        worksheet.Cell(metadataRow + 6, 2).Value = string.IsNullOrWhiteSpace(filters.TechnicalCategoryLabel)
            ? "(all technical)"
            : filters.TechnicalCategoryLabel;

        worksheet.Cell(metadataRow + 7, 1).Value = "Search";
        worksheet.Cell(metadataRow + 7, 2).Value = string.IsNullOrWhiteSpace(filters.SearchTerm)
            ? "(not set)"
            : filters.SearchTerm;

        worksheet.Range(metadataRow, 1, metadataRow + 7, 1).Style.Font.Bold = true;
    }
}
