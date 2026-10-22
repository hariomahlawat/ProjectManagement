using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Utilities;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

/// <summary>The tiles that can sit in the Project Overview header strip.</summary>
public enum ProjectHeaderTile
{
    Stage,
    Cost,
    RecordHealth,
    Jdp,
    Proliferation,
    Tot,
    Cancellation,
    Status
}

public sealed record ProjectHeaderStageTile(string Title, string? Schedule, string ScheduleTone, string Detail);

public sealed record ProjectHeaderCostTile(string Value, string? Basis, string Detail);

/// <summary>The latest remark, shown in full in a band under the strip.</summary>
public sealed record ProjectHeaderUpdate(bool HasRemark, string Text, string Meta, bool IsStale);

/// <summary>
/// The header strip for one project: the four tiles it shows, in order. The latest update is
/// not a tile: a remark is prose, so it gets a full-width band under the strip.
/// </summary>
public sealed record ProjectHeaderStripVm(
    IReadOnlyList<ProjectHeaderTile> Tiles,
    ProjectHeaderStageTile Stage,
    ProjectHeaderCostTile Cost,
    bool ShowsLastUpdate,
    ProjectHeaderUpdate LastUpdate)
{
    public static ProjectHeaderStripVm Empty { get; } = new(
        Array.Empty<ProjectHeaderTile>(),
        new ProjectHeaderStageTile("Not started", null, string.Empty, string.Empty),
        new ProjectHeaderCostTile("Not recorded", null, "No cost recorded yet"),
        false,
        new ProjectHeaderUpdate(false, "No remarks yet", string.Empty, false));

    public bool Shows(ProjectHeaderTile tile) => Tiles.Contains(tile);
}

/// <summary>
/// Rules for the Project Overview header strip: four tiles in every phase. A project in
/// progress shows its stage, its cost, the JDP partner and record health; a completed project
/// shows its Transfer of Technology and proliferation cost in place of stage and cost. Every
/// phase but completion also shows the latest update.
/// </summary>
public static class ProjectHeaderStripBuilder
{
    public const int StaleUpdateDays = 30;
    public const int PdcSoonDays = 30;

    public static ProjectHeaderStripVm Build(
        ProjectLifecycleStatus lifecycle,
        ProjectPortfolioPresentationVm portfolio,
        ProcurementAtAGlanceVm procurement,
        ProjectCostResolution? projectCost,
        ProjectRemarkSummaryViewModel remarks,
        DateOnly today,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        procurement ??= ProcurementAtAGlanceVm.Empty;
        remarks ??= ProjectRemarkSummaryViewModel.Empty;

        var tiles = lifecycle switch
        {
            ProjectLifecycleStatus.Active => new[] { ProjectHeaderTile.Stage, ProjectHeaderTile.Cost, ProjectHeaderTile.Jdp, ProjectHeaderTile.RecordHealth },
            ProjectLifecycleStatus.Completed => new[] { ProjectHeaderTile.Tot, ProjectHeaderTile.Proliferation, ProjectHeaderTile.Jdp, ProjectHeaderTile.RecordHealth },
            ProjectLifecycleStatus.Cancelled => new[] { ProjectHeaderTile.Cancellation, ProjectHeaderTile.Cost, ProjectHeaderTile.Jdp, ProjectHeaderTile.RecordHealth },
            _ => new[] { ProjectHeaderTile.Status, ProjectHeaderTile.Cost, ProjectHeaderTile.Jdp, ProjectHeaderTile.RecordHealth }
        };

        return new ProjectHeaderStripVm(
            tiles,
            BuildStage(portfolio, today),
            BuildCost(projectCost, procurement.SupplyOrderDate),
            lifecycle != ProjectLifecycleStatus.Completed,
            BuildLastUpdate(remarks, nowUtc));
    }


    // SECTION: Stage & schedule
    public static ProjectHeaderStageTile BuildStage(ProjectPortfolioPresentationVm portfolio, DateOnly today)
    {
        var current = portfolio.CurrentStage;
        string? schedule = null;
        var tone = string.Empty;

        if (!portfolio.IsWorkflowConcluded && current is not null)
        {
            var pdcText = current.PlannedEnd is { } pdc
                ? $"PDC {pdc.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
                : null;

            // The timeline's own schedule facts decide: only an in-progress stage can be
            // overdue, due or missing its PDC. A not-started or blocked stage shows its PDC plainly.
            if (current.NeedsPlannedCompletion)
            {
                (schedule, tone) = ("PDC not set", "warn");
            }
            else if (current.DaysRemaining is int days)
            {
                (schedule, tone) = days switch
                {
                    // The status leads, so it survives when a narrow tile wraps.
                    < 0 => ($"Overdue {-days} {Days(-days)} · {pdcText}", "danger"),
                    0 => ($"Due today · {pdcText}", "warn"),
                    <= PdcSoonDays => ($"Due in {days} {Days(days)} · {pdcText}", "warn"),
                    _ => (pdcText, string.Empty)
                };
            }
            else
            {
                schedule = pdcText;
            }
        }

        return new ProjectHeaderStageTile(portfolio.CurrentStageDisplay, schedule, tone, portfolio.CurrentStageDetail);
    }

    // SECTION: Project cost (the one shared rule, ProjectCostResolver), with the supply order
    public static ProjectHeaderCostTile BuildCost(ProjectCostResolution? projectCost, DateOnly? supplyOrderDate)
    {
        var detail = supplyOrderDate is { } soDate
            ? $"Supply order · {soDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
            : "Supply order not placed";

        return projectCost?.AmountInRupees is decimal amount
            ? new ProjectHeaderCostTile(IndianCurrencyFormatter.FormatCompact(amount), projectCost.SourceLabel, detail)
            : new ProjectHeaderCostTile("Not recorded", null, supplyOrderDate.HasValue ? detail : "No cost recorded yet");
    }

    // SECTION: Last update
    public static ProjectHeaderUpdate BuildLastUpdate(ProjectRemarkSummaryViewModel remarks, DateTime nowUtc)
    {
        if (!remarks.HasRemarks || remarks.LastActivityUtc is not { } at)
        {
            return new ProjectHeaderUpdate(false, "No remarks yet", "Add one when something moves", false);
        }

        var utc = DateTime.SpecifyKind(at, DateTimeKind.Utc);
        var days = Math.Max(0, (int)(nowUtc - utc).TotalDays);
        var date = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneHelper.GetIst()).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        var author = string.IsNullOrWhiteSpace(remarks.LastRemarkAuthorDisplayName) ? "Unknown" : remarks.LastRemarkAuthorDisplayName.Trim();
        var text = RemarkText(remarks.LastRemarkText);
        if (text.Length == 0) text = "Remark recorded";

        return new ProjectHeaderUpdate(true, text, $"{author} · {date} · {AgeLabel(days)}", days > StaleUpdateDays);
    }

    public static string AgeLabel(int days)
    {
        var months = Math.Max(1, (int)Math.Round(days / 30.4));
        var years = Math.Max(1, (int)Math.Round(days / 365.25));
        return days switch
        {
            0 => "today",
            1 => "yesterday",
            < 31 => $"{days} days ago",
            < 365 => months == 1 ? "1 month ago" : $"{months} months ago",
            _ => years == 1 ? "1 year ago" : $"{years} years ago"
        };
    }

    /// <summary>
    /// The whole remark as plain text. Markup such as mention tags is removed, and line and
    /// paragraph breaks are kept so a long remark still reads as the author wrote it.
    /// </summary>
    public static string RemarkText(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var text = Regex.Replace(body, @"<br\s*/?>|</(p|div|li)>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", " "));
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim());
        // At most one blank line between paragraphs.
        return Regex.Replace(string.Join("\n", lines), @"\n{3,}", "\n\n").Trim();
    }

    private static string Days(int count) => count == 1 ? "day" : "days";
}
