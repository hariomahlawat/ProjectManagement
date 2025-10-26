using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

public interface ITrainingExcelWorkbookBuilder
{
    byte[] Build(TrainingExcelWorkbookContext context);
}

public sealed record TrainingExcelWorkbookContext(
    IReadOnlyList<TrainingExportDetail> Trainings,
    DateTimeOffset GeneratedAtUtc,
    DateOnly? From,
    DateOnly? To,
    string? Search,
    bool IncludeRoster,
    string? TrainingTypeName,
    string? CategoryName,
    string? ProjectTechnicalCategoryName,
    string? ProjectTechnicalCategoryDisplayName);

public sealed class TrainingExcelWorkbookBuilder : ITrainingExcelWorkbookBuilder
{
    public byte[] Build(TrainingExcelWorkbookContext context)
    {
        if (context.Trainings is null)
        {
            throw new ArgumentNullException(nameof(context.Trainings));
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Trainings");

        WriteHeader(worksheet);
        WriteRows(worksheet, context.Trainings);
        ApplyMetadata(worksheet, context);

        if (context.IncludeRoster)
        {
            var rosterWorksheet = workbook.Worksheets.Add("Roster");
            WriteRosterWorksheet(rosterWorksheet, context.Trainings);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet worksheet)
    {
        var headers = new[]
        {
            "S.No.",
            "Training type",
            "Period",
            "Officers",
            "JCOs",
            "ORs",
            "Strength (O-J-OR)",
            "Total",
            "Source",
            "Projects",
            "Notes"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.SheetView.FreezeRows(1);
    }

    private static void WriteRows(IXLWorksheet worksheet, IReadOnlyList<TrainingExportDetail> trainings)
    {
        var rowNumber = 2;

        for (var index = 0; index < trainings.Count; index++, rowNumber++)
        {
            var row = trainings[index].Summary;
            worksheet.Cell(rowNumber, 1).Value = index + 1;
            worksheet.Cell(rowNumber, 2).Value = row.TrainingTypeName;
            worksheet.Cell(rowNumber, 3).Value = row.Period;
            worksheet.Cell(rowNumber, 4).Value = row.Officers;
            worksheet.Cell(rowNumber, 5).Value = row.JuniorCommissionedOfficers;
            worksheet.Cell(rowNumber, 6).Value = row.OtherRanks;
            worksheet.Cell(rowNumber, 7).Value = $"{row.Officers}-{row.JuniorCommissionedOfficers}-{row.OtherRanks}";
            worksheet.Cell(rowNumber, 8).Value = row.Total;
            worksheet.Cell(rowNumber, 9).Value = FormatSource(row.Source);
            if (row.Projects is { Count: > 0 } projects)
            {
                worksheet.Cell(rowNumber, 10).Value = string.Join(", ", projects);
            }
            else
            {
                worksheet.Cell(rowNumber, 10).Value = string.Empty;
            }
            worksheet.Cell(rowNumber, 11).Value = row.Notes ?? string.Empty;
        }

        worksheet.Column(11).Style.Alignment.WrapText = true;
        worksheet.Column(11).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    private static void ApplyMetadata(IXLWorksheet worksheet, TrainingExcelWorkbookContext context)
    {
        var lastRow = Math.Max(2, context.Trainings.Count + 1);
        worksheet.Columns(1, 11).AdjustToContents(1, lastRow);

        foreach (var column in worksheet.Columns(1, 11))
        {
            if (column.Width > 60)
            {
                column.Width = 60;
            }
        }

        worksheet.Column(10).Width = Math.Min(worksheet.Column(10).Width, 40);
        worksheet.Column(11).Width = Math.Min(worksheet.Column(11).Width, 60);

        var metadataRow = context.Trainings.Count + 3;
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());

        worksheet.Cell(metadataRow, 1).Value = "Export generated";
        worksheet.Cell(metadataRow, 2).Value = generatedAtIst.DateTime;
        worksheet.Cell(metadataRow, 2).Style.DateFormat.Format = "yyyy-MM-dd HH:mm\" IST\"";

        var metadataItems = new (string Label, string Value)[]
        {
            ("From", context.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(not set)"),
            ("To", context.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(not set)"),
            ("Search", string.IsNullOrWhiteSpace(context.Search) ? "(not set)" : context.Search!),
            ("Include roster", context.IncludeRoster ? "Yes" : "No"),
            ("Training type", string.IsNullOrWhiteSpace(context.TrainingTypeName) ? "(not set)" : context.TrainingTypeName!),
            ("Category", string.IsNullOrWhiteSpace(context.CategoryName) ? "(not set)" : context.CategoryName!),
            (
                "Technical category",
                string.IsNullOrWhiteSpace(context.ProjectTechnicalCategoryName)
                    ? "(not set)"
                    : context.ProjectTechnicalCategoryName!),
            (
                "Technical category display",
                string.IsNullOrWhiteSpace(context.ProjectTechnicalCategoryDisplayName)
                    ? "(not set)"
                    : context.ProjectTechnicalCategoryDisplayName!)
        };

        var metadataRowIndex = metadataRow + 1;
        foreach (var (label, value) in metadataItems)
        {
            worksheet.Cell(metadataRowIndex, 1).Value = label;
            worksheet.Cell(metadataRowIndex, 2).Value = value;
            metadataRowIndex++;
        }

        worksheet.Range(metadataRow, 1, metadataRowIndex - 1, 1).Style.Font.Bold = true;
    }

    private static void WriteRosterWorksheet(IXLWorksheet worksheet, IReadOnlyList<TrainingExportDetail> trainings)
    {
        var headers = new[]
        {
            "Training type",
            "Period",
            "Army number",
            "Rank",
            "Name",
            "Unit",
            "Category"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        worksheet.SheetView.FreezeRows(1);

        var rowNumber = 2;
        foreach (var training in trainings)
        {
            foreach (var trainee in training.Roster)
            {
                worksheet.Cell(rowNumber, 1).Value = training.Summary.TrainingTypeName;
                worksheet.Cell(rowNumber, 2).Value = training.Summary.Period;
                worksheet.Cell(rowNumber, 3).Value = trainee.ArmyNumber ?? string.Empty;
                worksheet.Cell(rowNumber, 4).Value = trainee.Rank;
                worksheet.Cell(rowNumber, 5).Value = trainee.Name;
                worksheet.Cell(rowNumber, 6).Value = trainee.UnitName;
                worksheet.Cell(rowNumber, 7).Value = FormatRosterCategory(trainee.Category);
                rowNumber++;
            }
        }

        if (rowNumber > 2)
        {
            worksheet.Columns(1, headers.Length).AdjustToContents(1, rowNumber - 1);
        }
    }

    private static string FormatSource(TrainingCounterSource source)
        => source == TrainingCounterSource.Legacy ? "Legacy" : "Roster";

    private static string FormatRosterCategory(byte category)
        => category switch
        {
            (byte)TrainingCategory.Officer => "Officer",
            (byte)TrainingCategory.JuniorCommissionedOfficer => "Junior Commissioned Officer",
            (byte)TrainingCategory.OtherRank => "Other Rank",
            _ => "Unknown"
        };
}
