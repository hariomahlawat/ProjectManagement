using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

internal sealed record ProjectBriefingTableRowMeasure(
    string ProjectName,
    string PresentStage,
    string ExternalStatus,
    bool HasCostRdBasis,
    bool HasProliferationCostBasis);

internal sealed record ProjectBriefingTablePage<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<double> RowHeights);

internal static class ProjectBriefingTablePagination
{
    public const double AvailableBodyHeight = 5.28;
    public const int PreferredRowsPerSlide = 7;
    private const int MinimumBalancedFinalRows = 4;

    public static IReadOnlyList<ProjectBriefingTablePage<T>> Paginate<T>(
        IReadOnlyList<T> items,
        ProjectBriefingCostMode costMode,
        Func<T, ProjectBriefingTableRowMeasure> measure)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(measure);

        if (items.Count == 0)
        {
            return Array.Empty<ProjectBriefingTablePage<T>>();
        }

        var measured = items
            .Select(item => new MeasuredItem<T>(item, EstimateRowHeight(measure(item), costMode)))
            .ToList();

        var pages = new List<List<MeasuredItem<T>>>();
        var current = new List<MeasuredItem<T>>();
        var currentHeight = 0d;

        foreach (var item in measured)
        {
            var exceedsPreferredCount = current.Count >= PreferredRowsPerSlide;
            var exceedsAvailableHeight = current.Count > 0
                && currentHeight + item.Height > AvailableBodyHeight;

            if (exceedsPreferredCount || exceedsAvailableHeight)
            {
                pages.Add(current);
                current = new List<MeasuredItem<T>>();
                currentHeight = 0d;
            }

            current.Add(item);
            currentHeight += item.Height;
        }

        if (current.Count > 0)
        {
            pages.Add(current);
        }

        RebalanceFinalPage(pages);

        return pages
            .Select(page => new ProjectBriefingTablePage<T>(
                page.Select(row => row.Item).ToArray(),
                page.Select(row => row.Height).ToArray()))
            .ToArray();
    }

    public static ProjectBriefingTableRowMeasure Measure(
        string? projectName,
        string? presentStage,
        string? externalStatus,
        bool hasCostRdBasis,
        bool hasProliferationCostBasis)
        => new(
            Normalize(projectName, "Project"),
            Normalize(presentStage, "Not recorded"),
            Normalize(externalStatus, "No external status recorded"),
            hasCostRdBasis,
            hasProliferationCostBasis);

    private static double EstimateRowHeight(
        ProjectBriefingTableRowMeasure row,
        ProjectBriefingCostMode costMode)
    {
        var projectCharactersPerLine = costMode == ProjectBriefingCostMode.Both ? 28 : 34;
        var statusCharactersPerLine = costMode switch
        {
            ProjectBriefingCostMode.Both => 79,
            ProjectBriefingCostMode.None => 104,
            _ => 90
        };
        var stageCharactersPerLine = costMode == ProjectBriefingCostMode.None ? 25 : 22;

        var projectLines = EstimateLines(row.ProjectName, projectCharactersPerLine, maximum: 3);
        var statusLines = EstimateLines(row.ExternalStatus, statusCharactersPerLine, maximum: 4);
        var stageLines = EstimateLines(row.PresentStage, stageCharactersPerLine, maximum: 2);
        var costLines = costMode switch
        {
            ProjectBriefingCostMode.Both when row.HasCostRdBasis || row.HasProliferationCostBasis => 2,
            ProjectBriefingCostMode.CostRdOnly when row.HasCostRdBasis => 2,
            ProjectBriefingCostMode.ProliferationOnly when row.HasProliferationCostBasis => 2,
            _ => 1
        };

        var lines = Math.Max(Math.Max(projectLines, statusLines), Math.Max(stageLines, costLines));
        return Math.Clamp(.60 + ((lines - 2) * .09), .60, .87);
    }

    private static int EstimateLines(string value, int charactersPerLine, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = 1;
        var currentLength = 0;

        foreach (var word in words)
        {
            var wordLength = Math.Min(word.Length, charactersPerLine);
            var proposed = currentLength == 0 ? wordLength : currentLength + 1 + wordLength;
            if (proposed <= charactersPerLine)
            {
                currentLength = proposed;
                continue;
            }

            lines++;
            currentLength = wordLength;
            if (lines >= maximum)
            {
                return maximum;
            }
        }

        return Math.Clamp(lines, 1, maximum);
    }

    private static void RebalanceFinalPage<T>(List<List<MeasuredItem<T>>> pages)
    {
        if (pages.Count < 2)
        {
            return;
        }

        var last = pages[^1];
        var previous = pages[^2];

        while (last.Count < MinimumBalancedFinalRows && previous.Count > MinimumBalancedFinalRows)
        {
            var candidate = previous[^1];
            if (last.Sum(row => row.Height) + candidate.Height > AvailableBodyHeight)
            {
                break;
            }

            previous.RemoveAt(previous.Count - 1);
            last.Insert(0, candidate);
        }
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record MeasuredItem<T>(T Item, double Height);
}
