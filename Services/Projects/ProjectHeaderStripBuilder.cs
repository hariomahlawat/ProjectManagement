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
    LastUpdate,
    RecordHealth,
    Jdp,
    Proliferation,
    Tot,
    Cancellation,
    Status
}

public sealed record ProjectHeaderStageTile(string Title, string? Schedule, string ScheduleTone, string Detail);

public sealed record ProjectHeaderCostTile(string Value, string? Basis, string Detail);

public sealed record ProjectHeaderUpdateTile(bool HasRemark, string Preview, string Meta, bool IsStale);

/// <summary>
/// The header strip for one project: which four tiles show, in order, and which tracked
/// tiles move down into the Project overview card instead. Each tile is rendered once.
/// </summary>
public sealed record ProjectHeaderStripVm(
    IReadOnlyList<ProjectHeaderTile> Tiles,
    IReadOnlyList<ProjectHeaderTile> InOverviewCard,
    ProjectHeaderStageTile Stage,
    ProjectHeaderCostTile Cost,
    ProjectHeaderUpdateTile LastUpdate)
{
    public static ProjectHeaderStripVm Empty { get; } = new(
        Array.Empty<ProjectHeaderTile>(),
        Array.Empty<ProjectHeaderTile>(),
        new ProjectHeaderStageTile("Not started", null, string.Empty, string.Empty),
        new ProjectHeaderCostTile("Not recorded", null, "No cost recorded yet"),
        new ProjectHeaderUpdateTile(false, "No remarks yet", string.Empty, false));

    public bool Shows(ProjectHeaderTile tile) => Tiles.Contains(tile);

    public bool InCard(ProjectHeaderTile tile) => InOverviewCard.Contains(tile);
}

/// <summary>
/// Rules for the Project Overview header strip. The tiles follow the project's phase:
/// before the supply order the strip tracks schedule, cost, the last update and record
/// health; from the supply order the JDP partner takes record health's place; a completed
/// project leads with its Transfer of Technology.
/// </summary>
public static class ProjectHeaderStripBuilder
{
    public const int StaleUpdateDays = 30;
    public const int PdcSoonDays = 30;

    private static readonly ProjectHeaderTile[] Tracked =
    {
        ProjectHeaderTile.Jdp, ProjectHeaderTile.Proliferation, ProjectHeaderTile.RecordHealth
    };

    public static ProjectHeaderStripVm Build(
        ProjectLifecycleStatus lifecycle,
        ProjectPortfolioPresentationVm portfolio,
        ProcurementAtAGlanceVm procurement,
        ProjectRemarkSummaryViewModel remarks,
        DateOnly today,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        procurement ??= ProcurementAtAGlanceVm.Empty;
        remarks ??= ProjectRemarkSummaryViewModel.Empty;

        var tiles = lifecycle switch
        {
            ProjectLifecycleStatus.Active => IsSupplyOrderPlaced(portfolio, procurement)
                ? new[] { ProjectHeaderTile.Stage, ProjectHeaderTile.Cost, ProjectHeaderTile.Jdp, ProjectHeaderTile.LastUpdate }
                : new[] { ProjectHeaderTile.Stage, ProjectHeaderTile.Cost, ProjectHeaderTile.LastUpdate, ProjectHeaderTile.RecordHealth },
            ProjectLifecycleStatus.Completed => new[] { ProjectHeaderTile.Tot, ProjectHeaderTile.Cost, ProjectHeaderTile.Proliferation, ProjectHeaderTile.RecordHealth },
            ProjectLifecycleStatus.Cancelled => new[] { ProjectHeaderTile.Cancellation, ProjectHeaderTile.Cost, ProjectHeaderTile.LastUpdate, ProjectHeaderTile.RecordHealth },
            _ => new[] { ProjectHeaderTile.Status, ProjectHeaderTile.Cost, ProjectHeaderTile.LastUpdate, ProjectHeaderTile.RecordHealth }
        };

        return new ProjectHeaderStripVm(
            tiles,
            Tracked.Where(tile => !tiles.Contains(tile)).ToArray(),
            BuildStage(portfolio, today),
            BuildCost(procurement),
            BuildLastUpdate(remarks, nowUtc));
    }

    /// <summary>The supply order is placed once its date is recorded or development has begun.</summary>
    public static bool IsSupplyOrderPlaced(ProjectPortfolioPresentationVm portfolio, ProcurementAtAGlanceVm procurement)
        => procurement.SupplyOrderDate.HasValue
            || (portfolio.CurrentStage is { } current && StageBuckets.Of(current.Code) == StageBucket.Development);

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

    // SECTION: Latest cost, with the supply order once it is placed
    public static ProjectHeaderCostTile BuildCost(ProcurementAtAGlanceVm procurement)
    {
        var known = new (string Basis, decimal? Value)[]
            {
                ("PNC", procurement.PncCost),
                ("L1", procurement.L1Cost),
                ("Benchmark", procurement.BenchmarkCost),
                ("AoN", procurement.AonCost),
                ("IPA", procurement.IpaCost)
            }
            .Where(cost => cost.Value.HasValue)
            .ToArray();

        var supplyOrder = procurement.SupplyOrderDate is { } soDate
            ? $"Supply order · {soDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
            : null;

        if (known.Length == 0)
        {
            return new ProjectHeaderCostTile("Not recorded", null, supplyOrder ?? "No cost recorded yet");
        }

        var latest = known[0];
        var detail = supplyOrder
            ?? (known.Length > 1 ? $"{known[1].Basis} {IndianCurrencyFormatter.FormatCompact(known[1].Value!.Value)}" : $"{latest.Basis} is the only cost recorded");

        return new ProjectHeaderCostTile(IndianCurrencyFormatter.FormatCompact(latest.Value!.Value), latest.Basis, detail);
    }

    // SECTION: Last update
    public static ProjectHeaderUpdateTile BuildLastUpdate(ProjectRemarkSummaryViewModel remarks, DateTime nowUtc)
    {
        if (!remarks.HasRemarks || remarks.LastActivityUtc is not { } at)
        {
            return new ProjectHeaderUpdateTile(false, "No remarks yet", "Add one when something moves", false);
        }

        var utc = DateTime.SpecifyKind(at, DateTimeKind.Utc);
        var days = Math.Max(0, (int)(nowUtc - utc).TotalDays);
        var date = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneHelper.GetIst()).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        var author = string.IsNullOrWhiteSpace(remarks.LastRemarkAuthorDisplayName) ? "Unknown" : remarks.LastRemarkAuthorDisplayName.Trim();
        var preview = PlainText(remarks.LastRemarkPreview);
        if (preview.Length == 0) preview = "Remark recorded";

        return new ProjectHeaderUpdateTile(true, preview, $"{author} · {date} · {AgeLabel(days)}", days > StaleUpdateDays);
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

    public const int RemarkPreviewLength = 140;

    /// <summary>
    /// A plain-text preview of a remark body. Markup is removed from the whole body before
    /// it is shortened, so the cut never lands inside a mention tag.
    /// </summary>
    public static string? RemarkPreview(string? body)
    {
        var text = PlainText(body);
        if (text.Length == 0) return null;
        return text.Length <= RemarkPreviewLength
            ? text
            : string.Concat(text.AsSpan(0, RemarkPreviewLength).TrimEnd(), "…");
    }

    // Remark bodies can hold mention markup; previews show plain text.
    public static string PlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " "));
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string Days(int count) => count == 1 ? "day" : "days";
}
