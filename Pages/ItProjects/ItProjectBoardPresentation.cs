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

    /// <summary>A lakh figure for a summary tile: "₹148.40".</summary>
    public static string Lakhs(decimal lakhs) => $"₹{lakhs.ToString("#,##0.00", Culture)}";

    /// <summary>The second line of the In progress tile.</summary>
    public static string InProgressNote(ItProjectBoardFigures figures)
    {
        var parts = new List<string>();
        if (figures.NotStartedCount > 0)
        {
            parts.Add($"{figures.NotStartedCount} not started yet");
        }

        if (figures.DatesMissingCount > 0)
        {
            parts.Add(figures.DatesMissingCount == 1 ? "1 missing a stage date" : $"{figures.DatesMissingCount} missing a stage date");
        }

        return parts.Count == 0 ? "All have their stage dates" : string.Join(" · ", parts);
    }

    /// <summary>The note after "Waiting longest" when some cases have no stage date to count from.</summary>
    public static string LeftOutNote(int casesWithoutStageDate) => casesWithoutStageDate switch
    {
        0 => string.Empty,
        1 => " · 1 case without a stage date left out",
        _ => $" · {casesWithoutStageDate} cases without stage dates left out"
    };

    /// <summary>The second line of the Delivered tile: deliveries in the board's year and the last delivery.</summary>
    public static string DeliveredNote(ItProjectBoardFigures figures)
    {
        var year = $"FY {FinancialYearHelper.Format(figures.FinancialYearStart)}";
        var inYear = figures.DeliveredInFinancialYear == 0 ? $"none yet in {year}" : $"{figures.DeliveredInFinancialYear} in {year}";
        return figures.LastDeliveredOn is { } last ? $"{inYear} · last on {last.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}" : inYear;
    }

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

    /// <summary>
    /// One sentence for the stage editor's note and confirm dialog: what a save will also do
    /// beyond the stages the user touched. Null when nothing else changes.
    /// </summary>
    public static string? CascadeSummary(IReadOnlyList<string> completedCodes, IReadOnlyList<string> reopenedCodes)
    {
        var parts = new List<string>();
        if (completedCodes.Count > 0)
        {
            parts.Add("also complete " + JoinLabels(completedCodes));
        }

        if (reopenedCodes.Count > 0)
        {
            parts.Add("also reopen " + JoinLabels(reopenedCodes));
        }

        return parts.Count == 0 ? null : $"Saving will {string.Join(" and ", parts)}.";
    }

    private static string JoinLabels(IReadOnlyList<string> codes)
    {
        var labels = codes.Select(ItProjectStages.LabelOf).ToList();
        return labels.Count <= 1
            ? string.Concat(labels)
            : $"{string.Join(", ", labels.Take(labels.Count - 1))} and {labels[^1]}";
    }

    /// <summary>Up to two initials for a remark avatar: "Maj Rathi" gives "MR", "ito-1" gives "I".</summary>
    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty)
            .Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => char.IsLetterOrDigit(part[0]))
            .ToList();
        if (parts.Count == 0)
        {
            return "?";
        }

        var letters = parts.Count == 1
            ? parts[0].Substring(0, 1)
            : string.Concat(parts[0][0], parts[^1][0]);
        return letters.ToUpperInvariant();
    }

    /// <summary>The CSS modifier of a remark role chip.</summary>
    public static string RoleClass(string role) => role switch
    {
        Configuration.RoleNames.Comdt => "itp-role--comdt",
        Configuration.RoleNames.HoD => "itp-role--hod",
        Configuration.RoleNames.Mco => "itp-role--mco",
        Configuration.RoleNames.Ito => "itp-role--ito",
        _ => "itp-role--other"
    };

    /// <summary>"842 KB", "1.2 MB": the size of an attachment.</summary>
    public static string FileSize(long bytes)
    {
        const long kb = 1024, mb = kb * 1024, gb = mb * 1024;
        return bytes switch
        {
            < kb => $"{bytes} B",
            < mb => $"{bytes / (double)kb:0} KB",
            < gb => $"{bytes / (double)mb:0.#} MB",
            _ => $"{bytes / (double)gb:0.#} GB"
        };
    }

    /// <summary>The Bootstrap icon of an attachment by its content type.</summary>
    public static string FileIcon(string? contentType) => (contentType ?? string.Empty).ToLowerInvariant() switch
    {
        "application/pdf" => "bi-file-earmark-pdf",
        "application/msword" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "bi-file-earmark-word",
        "application/vnd.ms-excel" or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "bi-file-earmark-excel",
        "application/vnd.ms-powerpoint" or "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "bi-file-earmark-slides",
        _ => "bi-file-earmark"
    };

    /// <summary>"3 projects" / "1 project".</summary>
    public static string Projects(int count) => count == 1 ? "1 project" : $"{count} projects";
}
