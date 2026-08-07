using System.Globalization;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

/// <summary>
/// Resolves the content, geometry and narrative pagination for formal Project Update Sheets.
/// The same plan is consumed by the PowerPoint composer and by preflight slide estimation.
/// </summary>
public static class ProjectBriefingUpdateSheetPlanner
{
    private const double ContentX = .50;
    private const double ContentY = 1.08;
    private const double ContentWidth = 12.33;
    private const double ContentBottom = 6.93;
    private const double Gap = .16;
    private const double BriefHeadingAndInsets = .62;

    private static readonly ProjectBriefingUpdateSheetBriefTypography PreferredBriefTypography =
        new(14.5, 18.0, 7.0);
    private static readonly ProjectBriefingUpdateSheetBriefTypography StandardBriefTypography =
        new(13.2, 16.2, 5.5);
    private static readonly ProjectBriefingUpdateSheetBriefTypography CompactBriefTypography =
        new(12.0, 14.4, 4.0);
    private static readonly ProjectBriefingUpdateSheetBriefTypography TightBriefTypography =
        new(12.0, 13.6, 2.0);

    private static readonly IReadOnlyList<ProjectBriefingUpdateSheetBriefTypography> BriefTypographyProfiles =
        new[]
        {
            PreferredBriefTypography,
            StandardBriefTypography,
            CompactBriefTypography,
            TightBriefTypography
        };

    public static IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> ResolveRows(
        ProjectBriefingPresentationProject project,
        ProjectBriefingUpdateSheetOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        return ResolveRows(
            new UpdateSheetRowSource(
                project.LifecycleStatus,
                project.PresentStageCode,
                project.CostRd,
                project.ExternalStatus,
                project.ArppReference,
                project.ArppPppNumberApplicable,
                project.Fund,
                project.DfpdsSchedule,
                project.Cfa,
                project.AonDate,
                project.SupplyOrderDate,
                project.DevelopmentPdcDate,
                project.CompletionStatusDisplay,
                project.JdpNames,
                project.ProjectOfficer,
                project.LineDirectorate),
            options);
    }

    public static IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> ResolveRows(
        ProjectBriefingProjectVm project,
        ProjectBriefingUpdateSheetOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        return ResolveRows(
            new UpdateSheetRowSource(
                project.LifecycleStatus,
                project.PresentStageCode,
                project.CostRd,
                project.ExternalStatus,
                project.ArppReference,
                project.ArppPppNumberApplicable,
                project.Fund,
                project.DfpdsSchedule,
                project.Cfa,
                project.AonDate,
                project.SupplyOrderDate,
                project.DevelopmentPdcDate,
                project.CompletionStatusDisplay,
                project.JdpNames,
                project.ProjectOfficer,
                project.LineDirectorate),
            options);
    }

    public static ProjectBriefingUpdateSheetPlan Plan(
        IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> rows,
        bool hasPhotograph,
        string? projectBrief)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var normalizedRows = rows.Count > 0
            ? rows
            : new[]
            {
                new ProjectBriefingUpdateSheetPlanningRow(
                    ProjectBriefingUpdateSheetRow.ProjectCost,
                    "Project Cost",
                    "Not recorded",
                    HasRecordedValue: false,
                    KeepWhenBlank: false,
                    FontSize: 10.5)
            };
        var normalizedBrief = NormalizeBrief(projectBrief);
        var missingBrief = IsMissingBrief(normalizedBrief);

        var layout = ResolveLayout(normalizedRows, hasPhotograph, normalizedBrief, missingBrief);
        var bodyWidth = Math.Max(.50, layout.BriefWidth - .38);
        var bodyHeight = Math.Max(.30, layout.BriefHeight - BriefHeadingAndInsets);
        var pages = PaginateBrief(normalizedBrief, bodyWidth, bodyHeight);

        return layout with { BriefPages = pages };
    }

    public static int EstimateSlideCount(
        ProjectBriefingProjectVm project,
        ProjectBriefingUpdateSheetOptions options)
    {
        var rows = ResolveRows(project, options);
        return Plan(rows, project.HasCoverPhoto, project.ProjectBrief).BriefPages.Count;
    }

    public static string NormalizeBrief(string? projectBrief)
        => string.IsNullOrWhiteSpace(projectBrief)
            ? "Project brief not recorded."
            : projectBrief
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();

    public static bool IsMissingBrief(string value)
        => string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), "Project brief not recorded.", StringComparison.OrdinalIgnoreCase);

    private static ProjectBriefingUpdateSheetPlan ResolveLayout(
        IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> rows,
        bool hasPhotograph,
        string normalizedBrief,
        bool missingBrief)
    {
        if (!hasPhotograph)
        {
            return ResolveNoPhotographLayout(rows);
        }

        if (rows.Count <= 2)
        {
            return ResolveFactsFirstLayout(
                rows,
                normalizedBrief,
                missingBrief,
                ProjectBriefingUpdateSheetLayoutVariant.Compact,
                compactPhoto: true);
        }

        var variant = rows.Count >= 6
            ? ProjectBriefingUpdateSheetLayoutVariant.Detailed
            : ProjectBriefingUpdateSheetLayoutVariant.Standard;
        var factsWidth = variant == ProjectBriefingUpdateSheetLayoutVariant.Detailed ? 7.18 : 6.27;
        var photoX = variant == ProjectBriefingUpdateSheetLayoutVariant.Detailed ? 7.93 : 7.02;
        var photoWidth = variant == ProjectBriefingUpdateSheetLayoutVariant.Detailed ? 4.90 : 5.81;
        var columns = variant == ProjectBriefingUpdateSheetLayoutVariant.Detailed
            ? new[] { .34, 2.15, 4.69 }
            : new[] { .34, 1.88, 4.05 };
        var rowHeights = BuildContentAwareRowHeights(rows, columns);
        var factsHeight = rowHeights.Sum();
        var minimumPhotoHeight = variant == ProjectBriefingUpdateSheetLayoutVariant.Detailed ? 2.65 : 2.35;
        var minimumBriefPanel = missingBrief ? .86 : 2.10;
        var maximumUpperHeight = ContentBottom - ContentY - Gap - minimumBriefPanel;
        var upperHeight = Math.Max(factsHeight, minimumPhotoHeight);

        // When a narrow facts column causes excessive wrapping, a full-width facts band
        // gives the table more room and preserves a usable first-slide narrative area.
        if (upperHeight > maximumUpperHeight)
        {
            var factsFirst = ResolveFactsFirstLayout(
                rows,
                normalizedBrief,
                missingBrief,
                ProjectBriefingUpdateSheetLayoutVariant.FactsFirst,
                compactPhoto: false);
            if (factsFirst.BriefHeight >= minimumBriefPanel
                || factsFirst.FactsHeight + .08 < factsHeight)
            {
                return factsFirst;
            }
        }

        var photoHeight = Math.Max(factsHeight, Math.Min(minimumPhotoHeight, maximumUpperHeight));
        var briefY = ContentY + Math.Max(factsHeight, photoHeight) + Gap;
        var briefHeight = Math.Max(.74, ContentBottom - briefY);

        return new ProjectBriefingUpdateSheetPlan(
            variant,
            ContentX,
            ContentY,
            factsWidth,
            factsHeight,
            columns,
            rowHeights,
            RenderPhotograph: true,
            photoX,
            ContentY,
            photoWidth,
            photoHeight,
            ContentX,
            briefY,
            ContentWidth,
            briefHeight,
            Array.Empty<ProjectBriefingUpdateSheetBriefPage>());
    }

    private static ProjectBriefingUpdateSheetPlan ResolveNoPhotographLayout(
        IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> rows)
    {
        var columns = FullWidthColumns();
        var rowHeights = BuildContentAwareRowHeights(rows, columns);
        var factsHeight = rowHeights.Sum();
        var briefY = ContentY + factsHeight + Gap;
        var briefHeight = Math.Max(.64, ContentBottom - briefY);

        return new ProjectBriefingUpdateSheetPlan(
            ProjectBriefingUpdateSheetLayoutVariant.NoPhotograph,
            ContentX,
            ContentY,
            ContentWidth,
            factsHeight,
            columns,
            rowHeights,
            RenderPhotograph: false,
            0,
            0,
            0,
            0,
            ContentX,
            briefY,
            ContentWidth,
            briefHeight,
            Array.Empty<ProjectBriefingUpdateSheetBriefPage>());
    }

    private static ProjectBriefingUpdateSheetPlan ResolveFactsFirstLayout(
        IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> rows,
        string normalizedBrief,
        bool missingBrief,
        ProjectBriefingUpdateSheetLayoutVariant variant,
        bool compactPhoto)
    {
        var columns = FullWidthColumns();
        var rowHeights = BuildContentAwareRowHeights(rows, columns);
        var factsHeight = rowHeights.Sum();
        var lowerY = ContentY + factsHeight + Gap;
        var lowerHeight = Math.Max(.64, ContentBottom - lowerY);
        var photoWidth = ResolveFactsFirstPhotoWidth(normalizedBrief, missingBrief, compactPhoto);
        var briefX = ContentX + photoWidth + Gap;
        var briefWidth = ContentWidth - photoWidth - Gap;

        return new ProjectBriefingUpdateSheetPlan(
            variant,
            ContentX,
            ContentY,
            ContentWidth,
            factsHeight,
            columns,
            rowHeights,
            RenderPhotograph: true,
            ContentX,
            lowerY,
            photoWidth,
            lowerHeight,
            briefX,
            lowerY,
            briefWidth,
            lowerHeight,
            Array.Empty<ProjectBriefingUpdateSheetBriefPage>());
    }

    private static double[] FullWidthColumns()
        => new[] { .38, 2.45, 9.50 };

    private static double ResolveFactsFirstPhotoWidth(
        string normalizedBrief,
        bool missingBrief,
        bool compactPhoto)
    {
        if (missingBrief)
        {
            return compactPhoto ? 6.30 : 5.45;
        }

        var length = normalizedBrief.Length;
        if (length > 1_400) return 4.25;
        if (length > 900) return 4.65;
        if (length > 560) return 5.05;
        return compactPhoto ? 5.55 : 5.30;
    }

    private static double[] BuildContentAwareRowHeights(
        IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> rows,
        IReadOnlyList<double> columns)
    {
        var labelWidth = Math.Max(.55, columns[1] - .12);
        var valueWidth = Math.Max(.80, columns[2] - .12);
        var heights = rows
            .Select(row => EstimateRowHeight(row, labelWidth, valueWidth))
            .ToArray();
        return FitRowHeightsToMaximum(heights, maximumTotal: 4.84);
    }


    private static double[] FitRowHeightsToMaximum(double[] heights, double maximumTotal)
    {
        var total = heights.Sum();
        if (total <= maximumTotal + .001)
        {
            return heights;
        }

        const double minimumRowHeight = .28;
        var reducible = heights.Sum(height => Math.Max(0, height - minimumRowHeight));
        if (reducible <= .001)
        {
            return heights;
        }

        var reductionRequired = total - maximumTotal;
        var ratio = Math.Min(1d, reductionRequired / reducible);
        return heights
            .Select(height => Math.Max(minimumRowHeight, height - ((height - minimumRowHeight) * ratio)))
            .ToArray();
    }

    private static double EstimateRowHeight(
        ProjectBriefingUpdateSheetPlanningRow row,
        double labelWidth,
        double valueWidth)
    {
        const double labelFontSize = 10.3;
        var labelLines = EstimateLines(row.Label, labelWidth, labelFontSize);
        var valueLines = EstimateLines(row.Value, valueWidth, row.FontSize);
        var labelHeight = (labelLines * labelFontSize * 1.18d) / 72d;
        var valueHeight = (valueLines * row.FontSize * 1.18d) / 72d;
        var required = Math.Max(labelHeight, valueHeight) + .065;

        return Math.Clamp(required, .30, 1.05);
    }

    private static IReadOnlyList<ProjectBriefingUpdateSheetBriefPage> PaginateBrief(
        string normalized,
        double firstSlideWidth,
        double firstSlideHeight)
    {
        if (IsMissingBrief(normalized))
        {
            return new[]
            {
                new ProjectBriefingUpdateSheetBriefPage(
                    normalized,
                    new ProjectBriefingUpdateSheetBriefTypography(12.0, 14.8, 0),
                    IsContinuation: false,
                    IsMissing: true)
            };
        }

        foreach (var profile in BriefTypographyProfiles)
        {
            if (Fits(normalized, firstSlideWidth, firstSlideHeight, profile))
            {
                return new[]
                {
                    new ProjectBriefingUpdateSheetBriefPage(
                        normalized,
                        profile,
                        IsContinuation: false,
                        IsMissing: false)
                };
            }
        }

        var pages = new List<ProjectBriefingUpdateSheetBriefPage>();
        var firstSplit = SplitToFit(normalized, firstSlideWidth, firstSlideHeight, TightBriefTypography);
        pages.Add(new ProjectBriefingUpdateSheetBriefPage(
            firstSplit.First,
            TightBriefTypography,
            IsContinuation: false,
            IsMissing: false));

        var remaining = firstSplit.Remainder;
        const double continuationWidth = 11.81;
        const double continuationHeight = 5.07;
        while (!string.IsNullOrWhiteSpace(remaining))
        {
            var fitted = false;
            foreach (var profile in BriefTypographyProfiles)
            {
                if (!Fits(remaining, continuationWidth, continuationHeight, profile))
                {
                    continue;
                }

                pages.Add(new ProjectBriefingUpdateSheetBriefPage(
                    remaining.Trim(),
                    profile,
                    IsContinuation: true,
                    IsMissing: false));
                remaining = string.Empty;
                fitted = true;
                break;
            }

            if (fitted)
            {
                continue;
            }

            var split = SplitToFit(remaining, continuationWidth, continuationHeight, TightBriefTypography);
            pages.Add(new ProjectBriefingUpdateSheetBriefPage(
                split.First,
                TightBriefTypography,
                IsContinuation: true,
                IsMissing: false));
            remaining = split.Remainder;
        }

        return pages;
    }

    private static (string First, string Remainder) SplitToFit(
        string value,
        double width,
        double height,
        ProjectBriefingUpdateSheetBriefTypography typography)
    {
        var maximumPrefix = FindMaximumPrefixLength(value, width, height, typography);
        var splitAt = FindSemanticSplit(value, maximumPrefix);
        if (splitAt <= 0 || splitAt >= value.Length)
        {
            splitAt = Math.Clamp(maximumPrefix, 1, value.Length - 1);
        }

        splitAt = RebalanceSmallRemainder(value, splitAt, width, typography);
        var first = value[..splitAt].Trim();
        var remainder = value[splitAt..].TrimStart();
        return (first, remainder);
    }

    private static int RebalanceSmallRemainder(
        string value,
        int splitAt,
        double width,
        ProjectBriefingUpdateSheetBriefTypography typography)
    {
        var remainder = value[splitAt..].TrimStart();
        if (EstimateLines(remainder, width, typography.BodyFontSize) >= 5)
        {
            return splitAt;
        }

        var searchLimit = Math.Max(1, splitAt - 1);
        while (searchLimit > Math.Max(24, splitAt / 2))
        {
            var earlier = FindSemanticSplit(value, searchLimit);
            if (earlier <= 0 || earlier >= splitAt)
            {
                break;
            }

            remainder = value[earlier..].TrimStart();
            if (EstimateLines(remainder, width, typography.BodyFontSize) >= 5)
            {
                return earlier;
            }

            searchLimit = earlier - 1;
        }

        return splitAt;
    }

    private static int FindMaximumPrefixLength(
        string value,
        double width,
        double height,
        ProjectBriefingUpdateSheetBriefTypography typography)
    {
        var low = 1;
        var high = Math.Max(1, value.Length - 1);
        var best = 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (Fits(value[..middle], width, height, typography))
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Math.Clamp(best, 1, Math.Max(1, value.Length - 1));
    }

    private static int FindSemanticSplit(string value, int maximumCharacters)
    {
        var limit = Math.Min(value.Length - 1, Math.Max(1, maximumCharacters));
        var minimum = Math.Max(20, (int)Math.Floor(limit * .55d));

        var paragraphBreak = value.LastIndexOf("\n\n", limit, StringComparison.Ordinal);
        if (paragraphBreak >= minimum)
        {
            return paragraphBreak + 2;
        }

        for (var index = limit; index >= minimum; index--)
        {
            if (index + 1 >= value.Length || !char.IsWhiteSpace(value[index + 1]))
            {
                continue;
            }

            if (value[index] is '.' or '?' or '!' or ';' or ':')
            {
                return index + 1;
            }
        }

        var wordBreak = value.LastIndexOf(' ', limit);
        return wordBreak >= minimum ? wordBreak + 1 : limit;
    }

    private static bool Fits(
        string value,
        double width,
        double height,
        ProjectBriefingUpdateSheetBriefTypography typography)
        => EstimateTextHeight(value, width, typography) <= height + .002;

    private static double EstimateTextHeight(
        string value,
        double width,
        ProjectBriefingUpdateSheetBriefTypography typography)
    {
        var paragraphs = SplitParagraphs(value);
        var total = 0d;
        for (var index = 0; index < paragraphs.Length; index++)
        {
            var lines = EstimateLines(paragraphs[index], width, typography.BodyFontSize);
            total += (lines * typography.LineSpacingPoints) / 72d;
            if (index < paragraphs.Length - 1)
            {
                total += typography.SpaceAfterPoints / 72d;
            }
        }

        return total + .035;
    }

    private static int EstimateLines(string value, double width, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }

        var usablePoints = Math.Max(24d, width * 72d);
        var weightedCharactersPerLine = Math.Max(12d, usablePoints / Math.Max(4d, fontSize * .52d));
        return value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Sum(line => Math.Max(
                1,
                (int)Math.Ceiling(WeightedLength(line) / weightedCharactersPerLine)));
    }

    private static double WeightedLength(string value)
    {
        var total = 0d;
        foreach (var character in value)
        {
            total += character switch
            {
                'W' or 'M' or 'w' or 'm' => 1.22d,
                'I' or 'i' or 'l' or '1' or '|' => .62d,
                ' ' => .52d,
                _ => 1d
            };
        }

        return Math.Max(1d, total);
    }

    private static string[] SplitParagraphs(string value)
        => value
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragraph => paragraph.Replace("\n", " ", StringComparison.Ordinal))
            .DefaultIfEmpty(value)
            .ToArray();

    private static IReadOnlyList<ProjectBriefingUpdateSheetPlanningRow> ResolveRows(
        UpdateSheetRowSource source,
        ProjectBriefingUpdateSheetOptions options)
    {
        var status = NormalizeStatus(source.ExternalStatus);
        var arppDetails = BuildArppDetails(source);
        var supplyOrder = BuildSupplyOrderDisplay(source.SupplyOrderDate, source.JdpNames);
        var milestone = ResolveMilestone(source);

        var all = options.Rows
            .Select(row => row switch
            {
                ProjectBriefingUpdateSheetRow.ProjectCost => new ProjectBriefingUpdateSheetPlanningRow(
                    row,
                    "Project Cost",
                    source.CostRd.IsAvailable ? source.CostRd.DisplayValue : "Not recorded",
                    source.CostRd.IsAvailable,
                    KeepWhenBlank: false,
                    FontSize: 10.8),
                ProjectBriefingUpdateSheetRow.ArppPppNumber => new ProjectBriefingUpdateSheetPlanningRow(
                    row,
                    "ARPP/PPP Number",
                    source.ArppPppNumberApplicable ? DisplayOrNotRecorded(source.ArppReference) : string.Empty,
                    source.ArppPppNumberApplicable && IsRecorded(source.ArppReference),
                    KeepWhenBlank: false,
                    FontSize: 10.2),
                ProjectBriefingUpdateSheetRow.FundingAuthority => new ProjectBriefingUpdateSheetPlanningRow(
                    row,
                    "Fund, DFPDS Sch and CFA",
                    arppDetails,
                    HasAnyArppDetail(source),
                    KeepWhenBlank: false,
                    FontSize: 9.4),
                ProjectBriefingUpdateSheetRow.AonDate => new ProjectBriefingUpdateSheetPlanningRow(
                    row,
                    "AoN Date",
                    FormatDate(source.AonDate),
                    source.AonDate.HasValue,
                    KeepWhenBlank: false,
                    FontSize: 10.5),
                ProjectBriefingUpdateSheetRow.SupplyOrder => new ProjectBriefingUpdateSheetPlanningRow(
                    row,
                    "SO Date and Name of Firm",
                    supplyOrder,
                    HasAnySupplyOrderDetail(source.SupplyOrderDate, source.JdpNames),
                    KeepWhenBlank: false,
                    FontSize: 9.4),
                ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus => new ProjectBriefingUpdateSheetPlanningRow(
                    row,
                    milestone.Label,
                    milestone.Value,
                    milestone.HasRecordedValue,
                    KeepWhenBlank: true,
                    FontSize: 10.5),
                ProjectBriefingUpdateSheetRow.PresentStatus => new ProjectBriefingUpdateSheetPlanningRow(
                    row,
                    "Present Status",
                    status,
                    !string.Equals(status, "Not recorded", StringComparison.Ordinal),
                    KeepWhenBlank: false,
                    FontSize: ResolveStatusFontSize(status)),
                ProjectBriefingUpdateSheetRow.ProjectOfficer => new ProjectBriefingUpdateSheetPlanningRow(
                    row,
                    "Project Officer",
                    DisplayOrNotRecorded(source.ProjectOfficer),
                    IsRecorded(source.ProjectOfficer),
                    KeepWhenBlank: false,
                    FontSize: 10.2),
                ProjectBriefingUpdateSheetRow.LineDirectorate => new ProjectBriefingUpdateSheetPlanningRow(
                    row,
                    "Line Directorate",
                    DisplayOrNotRecorded(source.LineDirectorate),
                    IsRecorded(source.LineDirectorate),
                    KeepWhenBlank: false,
                    FontSize: 10.2),
                _ => null
            })
            .Where(row => row is not null)
            .Select(row => row!)
            .ToArray();

        if (!options.HideEmptyValues)
        {
            return all;
        }

        var filtered = all.Where(row => row.HasRecordedValue || row.KeepWhenBlank).ToArray();
        return filtered.Length > 0 ? filtered : all.Take(1).ToArray();
    }

    private static UpdateSheetMilestone ResolveMilestone(UpdateSheetRowSource source)
    {
        if (source.LifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return new UpdateSheetMilestone(
                "Completion Status",
                string.IsNullOrWhiteSpace(source.CompletionStatusDisplay)
                    ? "Project completed"
                    : source.CompletionStatusDisplay,
                HasRecordedValue: true);
        }

        if (source.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
        {
            return new UpdateSheetMilestone("Project Status", "Project cancelled", HasRecordedValue: true);
        }

        var pdc = string.Equals(source.PresentStageCode, StageCodes.DEVP, StringComparison.OrdinalIgnoreCase)
            && source.DevelopmentPdcDate.HasValue
                ? source.DevelopmentPdcDate.Value.ToString("dd MMM yy", CultureInfo.InvariantCulture)
                : string.Empty;
        return new UpdateSheetMilestone("PDC Date", pdc, !string.IsNullOrWhiteSpace(pdc));
    }

    private static string BuildArppDetails(UpdateSheetRowSource source)
        => string.Join("\n", new[]
        {
            FieldValue("Fund", source.Fund),
            FieldValue("DFPDS", source.DfpdsSchedule),
            FieldValue("CFA", source.Cfa)
        });

    private static string BuildSupplyOrderDisplay(DateOnly? supplyOrderDate, IReadOnlyList<string> jdpNames)
    {
        var firms = jdpNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!supplyOrderDate.HasValue && firms.Length == 0)
        {
            return "Not recorded";
        }

        return string.Join("\n", new[]
        {
            $"SO Date: {(supplyOrderDate.HasValue ? supplyOrderDate.Value.ToString("dd MMM yy", CultureInfo.InvariantCulture) : "Not recorded")}",
            $"Firm: {(firms.Length > 0 ? string.Join("; ", firms) : "Not recorded")}"
        });
    }

    private static string NormalizeStatus(string? value)
        => string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "No external status recorded", StringComparison.OrdinalIgnoreCase)
                ? "Not recorded"
                : value.Trim();

    private static double ResolveStatusFontSize(string status)
        => status.Length switch
        {
            <= 95 => 10.4,
            <= 165 => 10.0,
            <= 250 => 9.6,
            _ => 9.2
        };

    private static bool HasAnyArppDetail(UpdateSheetRowSource source)
        => IsRecorded(source.Fund) || IsRecorded(source.DfpdsSchedule) || IsRecorded(source.Cfa);

    private static bool HasAnySupplyOrderDetail(DateOnly? supplyOrderDate, IReadOnlyList<string> jdpNames)
        => supplyOrderDate.HasValue || jdpNames.Any(IsRecorded);

    private static bool IsRecorded(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static string FieldValue(string label, string? value)
        => $"{label}: {DisplayOrNotRecorded(value)}";

    private static string DisplayOrNotRecorded(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value.Trim();

    private static string FormatDate(DateOnly? value)
        => value.HasValue ? value.Value.ToString("dd MMM yy", CultureInfo.InvariantCulture) : "Not recorded";

    private sealed record UpdateSheetRowSource(
        ProjectLifecycleStatus LifecycleStatus,
        string PresentStageCode,
        ProjectBriefingCostValue CostRd,
        string? ExternalStatus,
        string? ArppReference,
        bool ArppPppNumberApplicable,
        string? Fund,
        string? DfpdsSchedule,
        string? Cfa,
        DateOnly? AonDate,
        DateOnly? SupplyOrderDate,
        DateOnly? DevelopmentPdcDate,
        string? CompletionStatusDisplay,
        IReadOnlyList<string> JdpNames,
        string? ProjectOfficer,
        string? LineDirectorate);

    private sealed record UpdateSheetMilestone(string Label, string Value, bool HasRecordedValue);
}

public enum ProjectBriefingUpdateSheetLayoutVariant
{
    Compact = 1,
    Standard = 2,
    Detailed = 3,
    FactsFirst = 4,
    NoPhotograph = 5
}

public sealed record ProjectBriefingUpdateSheetPlanningRow(
    ProjectBriefingUpdateSheetRow Key,
    string Label,
    string Value,
    bool HasRecordedValue,
    bool KeepWhenBlank,
    double FontSize);

public sealed record ProjectBriefingUpdateSheetBriefTypography(
    double BodyFontSize,
    double LineSpacingPoints,
    double SpaceAfterPoints);

public sealed record ProjectBriefingUpdateSheetBriefPage(
    string Text,
    ProjectBriefingUpdateSheetBriefTypography Typography,
    bool IsContinuation,
    bool IsMissing);

public sealed record ProjectBriefingUpdateSheetPlan(
    ProjectBriefingUpdateSheetLayoutVariant Variant,
    double FactsX,
    double FactsY,
    double FactsWidth,
    double FactsHeight,
    IReadOnlyList<double> TableColumnWidths,
    IReadOnlyList<double> RowHeights,
    bool RenderPhotograph,
    double PhotoX,
    double PhotoY,
    double PhotoWidth,
    double PhotoHeight,
    double BriefX,
    double BriefY,
    double BriefWidth,
    double BriefHeight,
    IReadOnlyList<ProjectBriefingUpdateSheetBriefPage> BriefPages);
