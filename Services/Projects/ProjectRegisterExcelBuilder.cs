using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

public interface IProjectRegisterExcelBuilder
{
    byte[] Build(ProjectRegisterExportContext context);
}

/// <summary>
/// What the Projects register export writes (docs/ux/projects-register.md): the current tab's
/// rows in screen order, the applied filters and the order, for the About sheet.
/// </summary>
public sealed record ProjectRegisterExportContext(
    ProjectLifecycleFilter Tab,
    string TabLabel,
    IReadOnlyList<ProjectRegisterExportRow> Rows,
    IReadOnlyList<string> Filters,
    string Order,
    DateTimeOffset ExportedAtUtc);

/// <summary>
/// One exported project. Every value is a projection of facts the register already resolved
/// (<see cref="ProjectRegisterActiveFacts"/>, <see cref="ProjectRegisterClosedRowVm"/> and the
/// stage position); nothing here recomputes them.
/// </summary>
public sealed record ProjectRegisterExportRow(
    string Name,
    string? CaseFile,
    string? Category,
    string? ProjectOfficer,
    string? Hod,
    string Position,
    ProjectRegisterClosedRowVm Closed,
    DateOnly? CancelledOn,
    ProjectRegisterActiveFacts? Facts)
{
    public static ProjectRegisterExportRow From(
        Project project,
        ProjectRegisterClosedRowVm closed,
        ProjectRepositoryStagePositionVm position,
        ProjectRegisterActiveFacts? facts)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(closed);
        ArgumentNullException.ThrowIfNull(position);

        return new ProjectRegisterExportRow(
            project.Name,
            string.IsNullOrWhiteSpace(project.CaseFileNumber) ? null : project.CaseFileNumber,
            project.Category?.Name,
            project.LeadPoUser?.FullName ?? project.LeadPoUser?.UserName,
            project.HodUser?.FullName ?? project.HodUser?.UserName,
            string.IsNullOrWhiteSpace(position.Secondary) ? position.Primary : $"{position.Primary} ({position.Secondary})",
            closed,
            project.CancelledOn,
            facts);
    }
}

public sealed class ProjectRegisterExcelBuilder : IProjectRegisterExcelBuilder
{
    private const string DateFormat = "dd-mmm-yyyy";

    public byte[] Build(ProjectRegisterExportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Projects");
        var columns = ColumnsFor(context.Tab);

        for (var index = 0; index < columns.Count; index++)
        {
            var cell = sheet.Cell(1, index + 1);
            cell.Value = columns[index].Header;
            cell.Style.Font.Bold = true;
        }

        var header = sheet.Range(1, 1, 1, columns.Count);
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        for (var rowIndex = 0; rowIndex < context.Rows.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var cell = sheet.Cell(rowIndex + 2, columnIndex + 1);
                columns[columnIndex].Write(cell, context.Rows[rowIndex], rowIndex + 1);
            }
        }

        if (context.Rows.Count > 0)
        {
            sheet.Range(1, 1, context.Rows.Count + 1, columns.Count).SetAutoFilter();
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(context.Rows.Count + 1, 500), 8, 60);

        WriteAboutSheet(workbook.Worksheets.Add("About"), context);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record Column(string Header, Action<IXLCell, ProjectRegisterExportRow, int> Write);

    private static IReadOnlyList<Column> ColumnsFor(ProjectLifecycleFilter tab)
    {
        var leading = new List<Column>
        {
            new("Ser", (cell, _, ser) => cell.Value = ser),
            new("Project", (cell, row, _) => cell.Value = row.Name),
            new("Case file", (cell, row, _) => Text(cell, row.CaseFile)),
            new("Category", (cell, row, _) => Text(cell, row.Category))
        };

        var people = new List<Column>
        {
            new("Project Officer", (cell, row, _) => Text(cell, row.ProjectOfficer)),
            new("HoD", (cell, row, _) => Text(cell, row.Hod))
        };

        var middle = tab switch
        {
            ProjectLifecycleFilter.Active => new List<Column>
            {
                new("Current stage", (cell, row, _) => Text(cell, row.Facts?.CurrentStageName)),
                new("Stage state", (cell, row, _) => cell.Value = row.Facts is null ? "Archived: timing not tracked" : StateLabel(row.Facts.CurrentStageState)),
                new("In stage since", (cell, row, _) => Date(cell, row.Facts?.StageAgeFrom)),
                new("Days in stage", (cell, row, _) => Number(cell, row.Facts?.DaysInStage)),
                new("Stage PDC", (cell, row, _) => Date(cell, row.Facts?.StagePdc)),
                new("Planned days", (cell, row, _) => Number(cell, row.Facts?.PlannedDays)),
                new("Days late", (cell, row, _) => Number(cell, row.Facts?.DaysLate)),
                new("Days left", (cell, row, _) => Number(cell, row.Facts?.DaysLeft)),
                new("Plan status", (cell, row, _) => Text(cell, row.Facts is null ? null : PlanLabel(row.Facts)))
            },
            ProjectLifecycleFilter.Completed => new List<Column>
            {
                new("Build", (cell, row, _) => cell.Value = row.Closed.BuildText),
                new("Completed", (cell, row, _) => cell.Value = row.Closed.CompletedText),
                new("ToT", (cell, row, _) => cell.Value = row.Closed.TotText),
                new("Record", (cell, row, _) => cell.Value = row.Closed.RecordText)
            },
            ProjectLifecycleFilter.Cancelled => new List<Column>
            {
                new("Ceased at", (cell, row, _) => cell.Value = row.Position),
                new("Cancelled on", (cell, row, _) => Date(cell, row.CancelledOn)),
                new("Reason", (cell, row, _) => cell.Value = row.Closed.ReasonText)
            },
            _ => new List<Column>
            {
                new("Status", (cell, row, _) => cell.Value = row.Closed.StatusText),
                new("Current position", (cell, row, _) => cell.Value = row.Position)
            }
        };

        var trailing = tab == ProjectLifecycleFilter.Active
            ? new List<Column>
            {
                new("Last update", (cell, row, _) => Date(cell, row.Facts?.LastUpdatedOn)),
                new("Days since update", (cell, row, _) => Number(cell, row.Facts?.DaysSinceUpdate))
            }
            : new List<Column>();

        return leading.Concat(middle).Concat(people).Concat(trailing).ToList();
    }

    private static void WriteAboutSheet(IXLWorksheet sheet, ProjectRegisterExportContext context)
    {
        var exportedIst = IstClock.ToIst(context.ExportedAtUtc);
        var lines = new List<(string Label, string Value)>
        {
            ("Report", "PRISM Projects register"),
            ("Tab", context.TabLabel),
            ("Projects", context.Rows.Count.ToString(CultureInfo.InvariantCulture)),
            ("Order", context.Order),
            ("Exported", exportedIst.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture) + " IST"),
            ("Filters", context.Filters.Count == 0 ? "None" : context.Filters[0])
        };
        lines.AddRange(context.Filters.Skip(1).Select(filter => (string.Empty, filter)));

        for (var index = 0; index < lines.Count; index++)
        {
            sheet.Cell(index + 1, 1).Value = lines[index].Label;
            sheet.Cell(index + 1, 1).Style.Font.Bold = true;
            sheet.Cell(index + 1, 2).Value = lines[index].Value;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void Text(IXLCell cell, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            cell.Value = value;
        }
    }

    private static void Number(IXLCell cell, int? value)
    {
        if (value is int number)
        {
            cell.Value = number;
        }
    }

    private static void Date(IXLCell cell, DateOnly? value)
    {
        if (value is { } date)
        {
            cell.Value = date.ToDateTime(TimeOnly.MinValue);
            cell.Style.DateFormat.Format = DateFormat;
        }
    }

    private static string StateLabel(ProjectRegisterStageState state) => state switch
    {
        ProjectRegisterStageState.InProgress => "In progress",
        ProjectRegisterStageState.Blocked => "Blocked",
        ProjectRegisterStageState.Finished => "All stages complete",
        _ => "Not started"
    };

    private static string PlanLabel(ProjectRegisterActiveFacts facts)
    {
        if (facts.CurrentStageState == ProjectRegisterStageState.Finished)
        {
            return "Complete";
        }

        return facts.PlanStatus switch
        {
            ProjectRegisterPlanStatus.Late => "Late",
            ProjectRegisterPlanStatus.OnPlan => "On plan",
            _ => "No PDC"
        };
    }
}
