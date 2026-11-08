using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.ItProjects;

/// <summary>
/// Formatting the board page needs. Business facts (status, counts, order) come from
/// <see cref="IItProjectReadService"/>; this only turns them into text and bands.
/// </summary>
public static class ItProjectBoardPresentation
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-IN");

    public static string FinancialYear(int startYear) => $"FY {FinancialYearHelper.Format(startYear)}";

    public static string ShortDate(DateOnly date) => date.ToString("dd MMM yy", CultureInfo.InvariantCulture);

    public static string Cost(decimal lakhs) => $"₹ {lakhs.ToString("#,##0.00", Culture)} L";

    public static string StatusChip(ItProjectStatus status) => status.Kind switch
    {
        ItProjectStatusKind.NotStarted => "Not started",
        ItProjectStatusKind.InProgress => $"At {status.CurrentStageLabel}",
        ItProjectStatusKind.Completed => "Completed",
        ItProjectStatusKind.Dropped => "Dropped",
        _ => status.Label
    };

    /// <summary>The first line a stage cell shows under its dot (date, "no date" or "now"), or null for nothing.</summary>
    public static string? CellText(ItProjectBoardStageCell cell)
    {
        if (cell.IsSkipped)
        {
            return null;
        }

        if (cell.IsCompleted)
        {
            return cell.CompletedOn.HasValue ? ShortDate(cell.CompletedOn.Value) : "no date";
        }

        return cell.IsCurrent ? "now" : null;
    }

    /// <summary>The cost line of a stage cell (SO only), shown whether or not the stage is complete.</summary>
    public static string? CellCost(ItProjectBoardStageCell cell)
        => !cell.IsSkipped && cell.CostLakhs.HasValue ? Cost(cell.CostLakhs.Value) : null;

    /// <summary>The tooltip of a stage cell.</summary>
    public static string CellTitle(ItProjectBoardStageCell cell)
    {
        var label = ItProjectStages.LabelOf(cell.StageCode);
        if (cell.IsSkipped)
        {
            return $"{label}: skipped";
        }

        var parts = new List<string>();
        if (cell.IsCompleted)
        {
            parts.Add(cell.CompletedOn.HasValue ? $"done {ShortDate(cell.CompletedOn.Value)}" : "done, date not recorded");
        }
        else
        {
            parts.Add(cell.IsCurrent ? "current stage" : "not yet");
        }

        if (cell.CostLakhs.HasValue)
        {
            parts.Add(Cost(cell.CostLakhs.Value));
        }

        return $"{label}: {string.Join(", ", parts)}";
    }

    public static string CellClass(ItProjectBoardStageCell cell) => cell.IsSkipped
        ? "itp-cell--skip"
        : cell.IsCompleted
            ? "itp-cell--done"
            : cell.IsCurrent
                ? "itp-cell--cur"
                : "itp-cell--pend";

    public sealed record Band(string Key, string Title, int Count, IReadOnlyList<ItProjectBoardRow> Rows);

    /// <summary>
    /// Splits board rows into the bands the page draws: by status (Active, Completed, Dropped),
    /// or by financial year (newest first) when the user groups by FY. Empty bands are omitted.
    /// Rows keep the service's order inside a band.
    /// </summary>
    public static IReadOnlyList<Band> Bands(IReadOnlyList<ItProjectBoardRow> rows, bool groupByFinancialYear)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (groupByFinancialYear)
        {
            return rows
                .GroupBy(row => row.FinancialYearStart)
                .OrderByDescending(group => group.Key)
                .Select(group => new Band($"fy-{group.Key}", FinancialYear(group.Key), group.Count(), group.ToList()))
                .ToList();
        }

        var bands = new List<Band>();
        AddBand(bands, "active", "Active", rows.Where(row => row.Status.Kind is ItProjectStatusKind.NotStarted or ItProjectStatusKind.InProgress));
        AddBand(bands, "completed", "Completed", rows.Where(row => row.Status.Kind == ItProjectStatusKind.Completed));
        AddBand(bands, "dropped", "Dropped", rows.Where(row => row.Status.Kind == ItProjectStatusKind.Dropped));
        return bands;
    }

    private static void AddBand(List<Band> bands, string key, string title, IEnumerable<ItProjectBoardRow> rows)
    {
        var list = rows.ToList();
        if (list.Count > 0)
        {
            bands.Add(new Band(key, title, list.Count, list));
        }
    }

    /// <summary>"3 projects" / "1 project".</summary>
    public static string Projects(int count) => count == 1 ? "1 project" : $"{count} projects";
}
