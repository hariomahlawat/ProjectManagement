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

    /// <summary>A case card in the "Where the cases are now" pipeline.</summary>
    public sealed record PipelineCase(int Id, string Title, string TimeText, string? TimeTitle, string Tone, string? SoCost);

    /// <summary>One stage column of the pipeline: the live cases at that stage, longest wait first.</summary>
    public sealed record PipelineColumn(
        string StageCode,
        string Label,
        string ShortLabel,
        bool IsOptional,
        int Count,
        IReadOnlyList<PipelineCase> Shown,
        int Hidden);

    /// <summary>How many cases a column shows before "+ n more".</summary>
    public const int PipelineCasesShown = 4;

    /// <summary>
    /// The nine stage columns with the cases that are live and at that stage now (not started cases
    /// sit at the first stage). Order in a column: the longest wait by stage dates first, then the
    /// cases without a stage date, then by title. The facts (current stage, timing) come from the
    /// read service; this only groups and words them.
    /// </summary>
    public static IReadOnlyList<PipelineColumn> Pipeline(IEnumerable<ItProjectBoardRow> rows)
    {
        var live = rows
            .Where(row => row.Status.Kind is ItProjectStatusKind.NotStarted or ItProjectStatusKind.InProgress && row.Status.CurrentStageCode is not null)
            .ToList();

        return ItProjectStages.All
            .Select(stage =>
            {
                var here = live
                    .Where(row => row.Status.CurrentStageCode == stage.Code)
                    .OrderBy(row => row.Timing.Basis == ItProjectTimeBasis.StageDate ? 0 : 1)
                    .ThenByDescending(row => row.Timing.Basis == ItProjectTimeBasis.StageDate ? row.Timing.DaysAtStage ?? 0 : 0)
                    .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new PipelineColumn(
                    stage.Code,
                    stage.Label,
                    ShortStageLabel(stage.Code),
                    stage.IsOptional,
                    here.Count,
                    here.Take(PipelineCasesShown).Select(ToPipelineCase).ToList(),
                    Math.Max(0, here.Count - PipelineCasesShown));
            })
            .ToList();
    }

    /// <summary>The column heading when space is short: "Delivery and ATP" becomes "Delivery &amp; ATP", Benchmarking "BM".</summary>
    public static string ShortStageLabel(string code) => code switch
    {
        ItProjectStages.Ppp => "IT PPP / IPA",
        ItProjectStages.Benchmarking => "BM",
        ItProjectStages.Delivery => "Delivery & ATP",
        _ => ItProjectStages.LabelOf(code)
    };

    /// <summary>
    /// The time line of a case card, following the redesign's rule: days from the previous stage's
    /// date (coloured by age); else when the move was recorded (never coloured); else not recorded.
    /// </summary>
    public static (string Text, string? Title, string Tone) TimeText(ItProjectTiming timing) => timing.Basis switch
    {
        ItProjectTimeBasis.StageDate => (
            Days(timing.DaysAtStage ?? 0),
            timing.Since is { } since ? $"since the previous stage closed on {since.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}" : null,
            timing.Band switch
            {
                ItProjectAgeBand.Red => "late",
                ItProjectAgeBand.Amber => "warn",
                _ => "calm"
            }),
        ItProjectTimeBasis.RecordedMove => (
            timing.Since is { } moved ? $"recorded {moved.ToString("dd MMM", CultureInfo.InvariantCulture)}" : "date not recorded",
            "The previous stage has no date; this is when the move was recorded.",
            "nodate"),
        ItProjectTimeBasis.SinceAdded => (
            timing.DaysAtStage is 0 or null ? "added today" : $"added {Days(timing.DaysAtStage.Value)} ago",
            null,
            "calm"),
        _ => ("date not recorded", "The previous stage has no date.", "nodate")
    };

    private static string Days(int days) => days == 1 ? "1 d" : $"{days} d";

    private static PipelineCase ToPipelineCase(ItProjectBoardRow row)
    {
        var (text, title, tone) = TimeText(row.Timing);
        var so = row.Status.CurrentStageCode == ItProjectStages.Delivery
            ? row.Stages.FirstOrDefault(cell => cell.StageCode == ItProjectStages.So && cell.IsCompleted)?.CostLakhs
            : null;
        return new PipelineCase(row.Id, row.Title, text, title, tone, so is { } cost ? Cost(cost) : null);
    }

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

    /// <summary>The status line of the project page: "In progress · COB", "Delivered", "Dropped".</summary>
    public static string StatusLine(ItProjectStatus status)
        => status.Kind == ItProjectStatusKind.Completed ? "Delivered" : status.Label;

    public static string StatusChip(ItProjectStatus status) => status.Kind switch
    {
        ItProjectStatusKind.NotStarted => "Not started",
        ItProjectStatusKind.InProgress => $"At {status.CurrentStageLabel}",
        ItProjectStatusKind.Completed => "Delivered",
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
    /// Splits board rows into the bands the page draws: by status (Active, Delivered, Dropped),
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
        AddBand(bands, "completed", "Delivered", rows.Where(row => row.Status.Kind == ItProjectStatusKind.Completed));
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
