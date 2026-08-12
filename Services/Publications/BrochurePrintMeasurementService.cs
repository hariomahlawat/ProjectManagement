using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using ProjectManagement.Utilities.Reporting;
using SkiaSharp;

namespace ProjectManagement.Services.Publications;

public interface IBrochurePrintMeasurementService
{
    BrochurePrintProjectMeasurement MeasureProject(
        BrochurePrintPlanningItem item,
        BrochurePrintLayoutVariant variant,
        float imageWidthAdjustmentPoints = 0f);

    BrochurePrintClosingMeasurement MeasureClosing(BrochurePrintMatter? matter, string? strapline);

    BrochurePrintFrontPagePlan MeasureFrontPage(
        BrochurePrintMatter? matter,
        BrochureCoverStyle coverStyle,
        string? strapline);
}

/// <summary>
/// Font-aware measurement for the narrow hard-copy brochure. It uses the same offline DM Sans
/// package as QuestPDF when available and falls back to the platform typeface only when the
/// publication font package is unavailable. The service intentionally measures text by actual
/// glyph widths rather than using word-count heuristics.
/// </summary>
public sealed class BrochurePrintMeasurementService : IBrochurePrintMeasurementService, IDisposable
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex WordToken = new(@"\S+", RegexOptions.Compiled);
    private static readonly Regex SentenceEnd = new(
        @"[.!?](?:[""')\]]+)?(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IWebHostEnvironment _environment;
    private readonly IPublicationFontService _fontService;
    private readonly ILogger<BrochurePrintMeasurementService> _logger;
    private readonly object _gate = new();

    private TypefaceSet? _typefaces;
    private bool _disposed;

    public BrochurePrintMeasurementService(
        IWebHostEnvironment environment,
        IPublicationFontService fontService,
        ILogger<BrochurePrintMeasurementService> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public BrochurePrintProjectMeasurement MeasureProject(
        BrochurePrintPlanningItem item,
        BrochurePrintLayoutVariant variant,
        float imageWidthAdjustmentPoints = 0f)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfDisposed();

        var wordCount = item.NarrativeWordCount;
        var spec = BrochurePrintLayoutMetrics.VariantSpec(variant, wordCount);
        var moduleWidth = BrochurePrintLayoutMetrics.ModuleWidthPoints;
        var titleWidth = moduleWidth
                         - (BrochurePrintLayoutMetrics.ModuleBorderPoints * 2f)
                         - (BrochurePrintLayoutMetrics.ModuleHorizontalPaddingPoints * 2f);

        var titleFontSize = spec.TitleFontSize;
        var titleLines = MeasureLineCount(
            item.ProjectName.ToUpperInvariant(),
            titleFontSize,
            titleWidth,
            FontWeight.SemiBold);

        // Reference-format behaviour: preserve readable heading typography and let the green band
        // grow to two (or exceptionally three) lines. Shrinking is bounded and is never used as
        // the primary mechanism for protecting a fixed-height title strip.
        while (titleLines > 2 && titleFontSize > BrochurePrintLayoutMetrics.ProjectTitleMinimumFontSize)
        {
            titleFontSize = Math.Max(
                BrochurePrintLayoutMetrics.ProjectTitleMinimumFontSize,
                titleFontSize - .25f);
            titleLines = MeasureLineCount(
                item.ProjectName.ToUpperInvariant(),
                titleFontSize,
                titleWidth,
                FontWeight.SemiBold);
        }

        var titleHeight = Math.Max(
            18f,
            (titleLines * titleFontSize * BrochurePrintLayoutMetrics.ProjectTitleLineHeight) + 5f);

        var hasPrimary = item.HasPrimaryPhoto;
        var hasSecond = item.HasSecondaryPhoto
                        && item.ImageMode != BrochureImageMode.Single;
        var imageWidth = hasPrimary
            ? Math.Clamp(
                spec.ImageWidthPoints + Math.Max(0f, imageWidthAdjustmentPoints),
                94f,
                BrochurePrintLayoutMetrics.ResidualMaximumImageWidthPoints)
            : 0f;

        var bodyInnerWidth = moduleWidth
                             - (BrochurePrintLayoutMetrics.ModuleBorderPoints * 2f)
                             - (spec.BodyPaddingPoints * 2f);
        var sideTextWidth = hasPrimary
            ? bodyInnerWidth - imageWidth - BrochurePrintLayoutMetrics.TextImageGapPoints
            : bodyInnerWidth;
        sideTextWidth = Math.Max(118f, sideTextWidth);
        var fullTextWidth = bodyInnerWidth;

        var primaryImageHeight = 0f;
        var secondaryImageHeight = 0f;
        var imageHeight = 0f;
        if (hasPrimary)
        {
            var imageAspect = hasSecond
                ? BrochurePrintLayoutMetrics.GalleryImageAspectRatio
                : BrochurePrintLayoutMetrics.SingleImageAspectRatio;
            primaryImageHeight = imageWidth / imageAspect;
            secondaryImageHeight = hasSecond ? imageWidth / imageAspect : 0f;
            imageHeight = primaryImageHeight
                          + secondaryImageHeight
                          + (hasSecond ? BrochurePrintLayoutMetrics.GalleryImageGapPoints : 0f);
        }

        string leadingNarrative;
        string continuationNarrative;
        string trailingNarrative;
        float leadingTextHeight;
        float continuationTextHeight;
        float trailingTextHeight;
        float bodyContentHeight;
        float remainderGapPoints;
        BrochureFloatSplitKind floatSplitKind;

        if (!hasPrimary)
        {
            leadingNarrative = string.Empty;
            continuationNarrative = string.Empty;
            trailingNarrative = item.Narrative ?? string.Empty;
            leadingTextHeight = 0f;
            continuationTextHeight = 0f;
            trailingTextHeight = MeasureTextHeight(
                trailingNarrative,
                spec.BodyFontSize,
                fullTextWidth,
                spec.BodyLineHeight,
                FontWeight.Regular,
                BrochurePrintLayoutMetrics.ProjectParagraphSpacingPoints);
            bodyContentHeight = trailingTextHeight;
            remainderGapPoints = 0f;
            floatSplitKind = BrochureFloatSplitKind.None;
        }
        else
        {
            var split = SplitNarrativeForFloat(
                item.Narrative,
                spec.BodyFontSize,
                sideTextWidth,
                fullTextWidth,
                imageHeight,
                spec.BodyLineHeight);

            leadingNarrative = split.Leading;
            continuationNarrative = split.Continuation;
            trailingNarrative = split.Trailing;
            leadingTextHeight = split.LeadingHeightPoints;
            continuationTextHeight = split.ContinuationHeightPoints;
            trailingTextHeight = split.TrailingHeightPoints;
            remainderGapPoints = split.RemainderGapPoints;
            floatSplitKind = split.Kind;

            bodyContentHeight = Math.Max(imageHeight, leadingTextHeight);
            if (!string.IsNullOrWhiteSpace(continuationNarrative) || !string.IsNullOrWhiteSpace(trailingNarrative))
            {
                bodyContentHeight += split.RemainderGapPoints
                                     + continuationTextHeight
                                     + trailingTextHeight;
            }
        }

        var totalTextHeight = leadingTextHeight
                              + continuationTextHeight
                              + trailingTextHeight
                              + (!string.IsNullOrWhiteSpace(leadingNarrative)
                                 && (!string.IsNullOrWhiteSpace(continuationNarrative) || !string.IsNullOrWhiteSpace(trailingNarrative))
                                  ? remainderGapPoints
                                  : 0f);
        var totalHeight = titleHeight
                          + (spec.BodyPaddingPoints * 2f)
                          + bodyContentHeight
                          + (BrochurePrintLayoutMetrics.ModuleBorderPoints * 2f)
                          + BrochurePrintLayoutMetrics.ProjectMeasurementSafetyPoints;

        return new BrochurePrintProjectMeasurement(
            item.ProjectId,
            variant,
            TotalHeightPoints: totalHeight,
            TitleHeightPoints: titleHeight,
            TitleFontSize: titleFontSize,
            BodyFontSize: spec.BodyFontSize,
            BodyLineHeight: spec.BodyLineHeight,
            ImageWidthPoints: imageWidth,
            BodyPaddingPoints: spec.BodyPaddingPoints,
            TextWidthPoints: sideTextWidth,
            TextHeightPoints: totalTextHeight,
            ImageHeightPoints: imageHeight,
            QualityRank: spec.QualityRank,
            LeadingNarrative: leadingNarrative,
            TrailingNarrative: trailingNarrative,
            LeadingTextHeightPoints: leadingTextHeight,
            TrailingTextHeightPoints: trailingTextHeight,
            FullTextWidthPoints: fullTextWidth,
            PrimaryImageHeightPoints: primaryImageHeight,
            SecondaryImageHeightPoints: secondaryImageHeight,
            UsesFloatLayout: hasPrimary,
            ContinuationNarrative: continuationNarrative,
            ContinuationTextHeightPoints: continuationTextHeight,
            FloatSplitKind: floatSplitKind,
            RemainderGapPoints: remainderGapPoints,
            ParagraphSpacingPoints: BrochurePrintLayoutMetrics.ProjectParagraphSpacingPoints);
    }

    public BrochurePrintClosingMeasurement MeasureClosing(
        BrochurePrintMatter? matter,
        string? strapline)
    {
        ThrowIfDisposed();
        matter ??= new BrochurePrintMatter(null, null, null, null, null, null, null, null);

        var outerWidth = BrochurePrintLayoutMetrics.ModuleWidthPoints;
        var visionInnerWidth = outerWidth - 8.4f - 18f; // 4.2 pt border each side + 9 pt padding each side.
        var visionHeadingHeight = Math.Max(
            14f,
            MeasureTextHeight(
                "Visionary Horizons & Strategic Objectives",
                BrochurePrintLayoutMetrics.ClosingVisionHeadingFontSize,
                visionInnerWidth - 16f,
                1.0f,
                FontWeight.SemiBold) + 4f);
        var visionBodyHeight = MeasureTextHeight(
            matter.VisionaryHorizons,
            BrochurePrintLayoutMetrics.ClosingVisionBodyFontSize,
            visionInnerWidth,
            BrochurePrintLayoutMetrics.ClosingVisionBodyLineHeight,
            FontWeight.Regular,
            BrochurePrintLayoutMetrics.ClosingVisionParagraphSpacingPoints);
        var visionPanelHeight = 8.4f + 14f + visionHeadingHeight + 4f + visionBodyHeight;

        var newSimulatorInnerWidth = outerWidth - 16f;
        var newSimulatorText = $"New Simulators. {matter.NewSimulatorsGuidance}".Trim();
        var newSimulatorHeight = MeasureTextHeight(
            newSimulatorText,
            BrochurePrintLayoutMetrics.ClosingNewSimulatorsFontSize,
            newSimulatorInnerWidth,
            BrochurePrintLayoutMetrics.ClosingNewSimulatorsLineHeight,
            FontWeight.SemiBold) + 14f;

        // The approved reference finishes on the New Simulators band. The institutional
        // strapline belongs to the first page only; omitting it here gives the closing panel
        // more visual authority and removes a redundant final-page element.
        const float straplineHeight = 0f;

        var total = 1f
                    + visionPanelHeight
                    + 5f
                    + newSimulatorHeight
                    + 2f;

        return new BrochurePrintClosingMeasurement(
            TotalHeightPoints: total,
            VisionPanelHeightPoints: visionPanelHeight,
            NewSimulatorsHeightPoints: newSimulatorHeight,
            StraplineHeightPoints: straplineHeight);
    }

    public BrochurePrintFrontPagePlan MeasureFrontPage(
        BrochurePrintMatter? matter,
        BrochureCoverStyle coverStyle,
        string? strapline)
    {
        ThrowIfDisposed();
        matter ??= new BrochurePrintMatter(null, null, null, null, null, null, null, null);

        const float horizontalPadding = 10f;
        var bodyWidth = BrochurePrintLayoutMetrics.ReferenceWidthPoints - (horizontalPadding * 2f);

        // Cover A carries the Centre of Expertise statement inside the artwork hero, like the
        // approved reference brochure. Cover B retains a separate statement band because its hero
        // is arbitrary project photography and cannot guarantee a safe text zone.
        var centreFont = BrochurePrintLayoutMetrics.FrontCentrePreferredFontSize;
        var centreTextHeight = MeasureTextHeight(
            matter.CentreStatement,
            centreFont,
            bodyWidth - 28f,
            BrochurePrintLayoutMetrics.FrontCentreLineHeight,
            FontWeight.SemiBold);
        var centreBlockHeight = coverStyle == BrochureCoverStyle.Institutional
            ? 0f
            : Math.Max(48f, centreTextHeight + 18f);

        // CONTACTS is a dedicated row above the agency headings. The agency body is asymmetric
        // because the Developing Agency carries materially more copy than Manufacturing Agency.
        var contactFont = BrochurePrintLayoutMetrics.FrontContactPreferredFontSize;
        var contactInnerWidth = BrochurePrintLayoutMetrics.ReferenceWidthPoints - 16f;
        var agencyGutter = 12f;
        var distributableWidth = contactInnerWidth - agencyGutter;
        var developingWidth = distributableWidth * BrochurePrintLayoutMetrics.FrontContactDevelopingFraction;
        var manufacturingWidth = distributableWidth * BrochurePrintLayoutMetrics.FrontContactManufacturingFraction;
        var contactBodyHeight = Math.Max(
            MeasureTextHeight(
                matter.DevelopingAgency,
                contactFont,
                developingWidth,
                BrochurePrintLayoutMetrics.FrontContactLineHeight,
                FontWeight.SemiBold),
            MeasureTextHeight(
                matter.ManufacturingAgency,
                contactFont,
                manufacturingWidth,
                BrochurePrintLayoutMetrics.FrontContactLineHeight,
                FontWeight.SemiBold));
        var contactBlockHeight = Math.Max(
            96f,
            contactBodyHeight
            + BrochurePrintLayoutMetrics.FrontContactBadgeHeightPoints
            + BrochurePrintLayoutMetrics.FrontContactAgencyHeadingHeightPoints
            + 15f);

        var straplineHeight = Math.Max(
            BrochurePrintLayoutMetrics.FrontStraplineHeightPoints,
            MeasureTextHeight(strapline, 8.5f, bodyWidth, 1.05f, FontWeight.SemiBold) + 8f);

        var selectedBodyFont = BrochurePrintLayoutMetrics.FrontBodyMinimumFontSize;
        var selectedBodyHeight = 0f;
        var selectedHeroHeight = 0f;
        var selectedSpacing = 5.5f;
        var fits = false;

        foreach (var bodyFont in new[] { 9.0f, 8.8f, 8.6f })
        {
            var spacing = bodyFont >= 8.8f ? 6f : 5f;
            var openingHeight = MeasureTextHeight(
                matter.OpeningNarrative,
                bodyFont,
                bodyWidth,
                BrochurePrintLayoutMetrics.FrontBodyLineHeight,
                FontWeight.Regular);
            var futureHeight = MeasureTextHeight(
                matter.FutureNarrative,
                bodyFont,
                bodyWidth,
                BrochurePrintLayoutMetrics.FrontBodyLineHeight,
                FontWeight.Regular);
            var procurementHeight = MeasureTextHeight(
                $"Procurement: {matter.ProcurementGuidance}".Trim(),
                Math.Max(BrochurePrintLayoutMetrics.FrontBodyMinimumFontSize, bodyFont - .1f),
                bodyWidth,
                1.06f,
                FontWeight.Regular);
            var bodyHeight = 14f + openingHeight + spacing + futureHeight + spacing + procurementHeight + 12f;
            var fixedHeight = centreBlockHeight + bodyHeight + contactBlockHeight + straplineHeight;
            var heroHeight = BrochurePrintLayoutMetrics.ReferenceHeightPoints - fixedHeight;

            if (heroHeight < BrochurePrintLayoutMetrics.FrontMinimumHeroHeightPoints)
            {
                continue;
            }

            selectedBodyFont = bodyFont;
            selectedBodyHeight = bodyHeight;
            selectedHeroHeight = Math.Min(heroHeight, BrochurePrintLayoutMetrics.FrontMaximumHeroHeightPoints);
            selectedSpacing = spacing;
            fits = true;

            if (heroHeight > BrochurePrintLayoutMetrics.FrontMaximumHeroHeightPoints)
            {
                selectedBodyHeight += heroHeight - BrochurePrintLayoutMetrics.FrontMaximumHeroHeightPoints;
            }
            break;
        }

        if (!fits)
        {
            var bodyFont = BrochurePrintLayoutMetrics.FrontBodyMinimumFontSize;
            var openingHeight = MeasureTextHeight(
                matter.OpeningNarrative,
                bodyFont,
                bodyWidth,
                BrochurePrintLayoutMetrics.FrontBodyLineHeight,
                FontWeight.Regular);
            var futureHeight = MeasureTextHeight(
                matter.FutureNarrative,
                bodyFont,
                bodyWidth,
                BrochurePrintLayoutMetrics.FrontBodyLineHeight,
                FontWeight.Regular);
            var procurementHeight = MeasureTextHeight(
                $"Procurement: {matter.ProcurementGuidance}".Trim(),
                bodyFont,
                bodyWidth,
                1.06f,
                FontWeight.Regular);
            selectedBodyHeight = 14f + openingHeight + 5f + futureHeight + 5f + procurementHeight + 12f;
            selectedHeroHeight = BrochurePrintLayoutMetrics.FrontMinimumHeroHeightPoints;
        }

        var totalUsed = selectedHeroHeight
                        + centreBlockHeight
                        + selectedBodyHeight
                        + contactBlockHeight
                        + straplineHeight;
        var utilization = (int)Math.Round(
            Math.Min(1d, totalUsed / BrochurePrintLayoutMetrics.ReferenceHeightPoints) * 100d);

        return new BrochurePrintFrontPagePlan(
            Fits: fits && totalUsed <= BrochurePrintLayoutMetrics.ReferenceHeightPoints + 1f,
            HeroHeightPoints: selectedHeroHeight,
            CentreBlockHeightPoints: centreBlockHeight,
            CentreFontSize: centreFont,
            BodyBlockHeightPoints: selectedBodyHeight,
            BodyFontSize: selectedBodyFont,
            BodyLineHeight: BrochurePrintLayoutMetrics.FrontBodyLineHeight,
            BodySpacingPoints: selectedSpacing,
            ContactBlockHeightPoints: contactBlockHeight,
            ContactFontSize: contactFont,
            StraplineHeightPoints: straplineHeight,
            TotalUsedHeightPoints: totalUsed,
            UtilizationPercent: Math.Clamp(utilization, 0, 100),
            UsesMinimumTypography: selectedBodyFont <= BrochurePrintLayoutMetrics.FrontBodyMinimumFontSize + .01f,
            CoverStyle: coverStyle);
    }

    private FloatNarrativeSplit SplitNarrativeForFloat(
        string? narrative,
        float fontSize,
        float sideWidth,
        float fullWidth,
        float imageHeight,
        float lineHeight)
    {
        if (string.IsNullOrWhiteSpace(narrative))
        {
            return new FloatNarrativeSplit(string.Empty, string.Empty, string.Empty, 0f, 0f, 0f, BrochureFloatSplitKind.None, 0f);
        }

        var normalized = narrative
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        var words = WordToken.Matches(normalized).Cast<Match>().ToArray();
        if (words.Length == 0)
        {
            return new FloatNarrativeSplit(string.Empty, string.Empty, string.Empty, 0f, 0f, 0f, BrochureFloatSplitKind.None, 0f);
        }

        // First identify the largest complete-word prefix that fits beside the image. This is the
        // geometric anchor; the actual split is then moved to a nearby editorial boundary so the
        // full-width continuation does not begin in the middle of a sentence unless unavoidable.
        var low = 1;
        var high = words.Length;
        var bestWordCount = 0;
        var bestWordHeight = 0f;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var end = words[mid - 1].Index + words[mid - 1].Length;
            var prefix = normalized[..end].TrimEnd();
            var height = MeasureTextHeight(
                prefix,
                fontSize,
                sideWidth,
                lineHeight,
                FontWeight.Regular,
                BrochurePrintLayoutMetrics.ProjectParagraphSpacingPoints);

            if (height <= imageHeight + .35f)
            {
                bestWordCount = mid;
                bestWordHeight = height;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (bestWordCount == 0)
        {
            var trailingHeight = MeasureTextHeight(
                normalized,
                fontSize,
                fullWidth,
                lineHeight,
                FontWeight.Regular,
                BrochurePrintLayoutMetrics.ProjectParagraphSpacingPoints);
            return new FloatNarrativeSplit(string.Empty, string.Empty, normalized, 0f, 0f, trailingHeight, BrochureFloatSplitKind.None, 0f);
        }

        var idealEnd = words[bestWordCount - 1].Index + words[bestWordCount - 1].Length;
        var linePoints = fontSize * lineHeight;
        var upperTolerance = imageHeight
                             + (linePoints * BrochurePrintLayoutMetrics.FloatBoundaryToleranceLines);
        var preferredLowerBound = Math.Max(
            linePoints,
            imageHeight - (linePoints * BrochurePrintLayoutMetrics.FloatPreferredBoundaryBandLines));

        var candidates = BuildEditorialBoundaries(normalized, idealEnd);
        BoundaryMeasurement? bestBoundary = null;

        foreach (var boundary in candidates)
        {
            if (boundary.EndIndex <= 0 || boundary.EndIndex >= normalized.Length)
            {
                continue;
            }

            var prefix = normalized[..boundary.EndIndex].TrimEnd();
            if (prefix.Length == 0)
            {
                continue;
            }

            var height = MeasureTextHeight(
                prefix,
                fontSize,
                sideWidth,
                lineHeight,
                FontWeight.Regular,
                BrochurePrintLayoutMetrics.ProjectParagraphSpacingPoints);
            if (height > upperTolerance)
            {
                continue;
            }

            // Avoid choosing a semantically clean boundary that leaves most of the image height
            // unused when a much closer sentence/word boundary is available.
            var outsidePreferredBand = height < preferredLowerBound;
            var distance = Math.Abs(imageHeight - height);
            var boundaryPenalty = boundary.Kind switch
            {
                EditorialBoundaryKind.Paragraph => 0f,
                EditorialBoundaryKind.Sentence => linePoints * .28f,
                _ => linePoints * 1.10f
            };
            var bandPenalty = outsidePreferredBand ? linePoints * 1.5f : 0f;
            var score = distance + boundaryPenalty + bandPenalty;

            if (bestBoundary is null || score < bestBoundary.Score)
            {
                bestBoundary = new BoundaryMeasurement(
                    boundary.EndIndex,
                    height,
                    score,
                    boundary.Kind);
            }
        }

        var selectedEnd = bestBoundary?.EndIndex ?? idealEnd;
        var selectedHeight = bestBoundary?.HeightPoints ?? bestWordHeight;
        var selectedKind = bestBoundary?.Kind ?? EditorialBoundaryKind.Word;

        var leading = normalized[..selectedEnd].Trim();
        var trailingStart = selectedEnd;
        while (trailingStart < normalized.Length && char.IsWhiteSpace(normalized[trailingStart]))
        {
            trailingStart++;
        }

        var remainder = trailingStart < normalized.Length
            ? normalized[trailingStart..].Trim()
            : string.Empty;

        // A forced geometric split can occur in the middle of a sentence. Rendering that remainder
        // as a freshly justified paragraph creates the conspicuous stretched first line seen in
        // earlier phases. Keep only the unfinished sentence as a left-aligned continuation, then
        // return to normal justified editorial copy at the next true sentence boundary.
        var continuation = string.Empty;
        var trailing = remainder;
        if (selectedKind == EditorialBoundaryKind.Word && !string.IsNullOrWhiteSpace(remainder))
        {
            var nextSentence = SentenceEnd.Match(remainder);
            if (nextSentence.Success)
            {
                var continuationEnd = nextSentence.Index + nextSentence.Length;
                continuation = remainder[..continuationEnd].Trim();
                trailing = continuationEnd < remainder.Length
                    ? remainder[continuationEnd..].Trim()
                    : string.Empty;
            }
            else
            {
                continuation = remainder;
                trailing = string.Empty;
            }
        }

        var continuationHeightPoints = string.IsNullOrWhiteSpace(continuation)
            ? 0f
            : MeasureTextHeight(
                continuation,
                fontSize,
                fullWidth,
                lineHeight,
                FontWeight.Regular,
                BrochurePrintLayoutMetrics.ProjectParagraphSpacingPoints);
        var trailingHeightPoints = string.IsNullOrWhiteSpace(trailing)
            ? 0f
            : MeasureTextHeight(
                trailing,
                fontSize,
                fullWidth,
                lineHeight,
                FontWeight.Regular,
                BrochurePrintLayoutMetrics.ProjectParagraphSpacingPoints);

        var splitKind = selectedKind switch
        {
            EditorialBoundaryKind.Paragraph => BrochureFloatSplitKind.Paragraph,
            EditorialBoundaryKind.Sentence => BrochureFloatSplitKind.Sentence,
            _ => BrochureFloatSplitKind.Word
        };
        var remainderGap = splitKind switch
        {
            BrochureFloatSplitKind.Paragraph => BrochurePrintLayoutMetrics.FloatParagraphContinuationGapPoints,
            BrochureFloatSplitKind.Sentence => BrochurePrintLayoutMetrics.FloatSentenceContinuationGapPoints,
            BrochureFloatSplitKind.Word => BrochurePrintLayoutMetrics.FloatWordContinuationGapPoints,
            _ => BrochurePrintLayoutMetrics.FloatRemainderGapPoints
        };

        return new FloatNarrativeSplit(
            leading,
            continuation,
            trailing,
            selectedHeight,
            continuationHeightPoints,
            trailingHeightPoints,
            splitKind,
            remainderGap);
    }

    private static IReadOnlyList<EditorialBoundary> BuildEditorialBoundaries(
        string normalized,
        int idealEnd)
    {
        var boundaries = new List<EditorialBoundary>();

        for (var index = 0; index < normalized.Length; index++)
        {
            if (normalized[index] == '\n')
            {
                boundaries.Add(new EditorialBoundary(index, EditorialBoundaryKind.Paragraph));
            }
        }

        foreach (Match sentenceEnd in SentenceEnd.Matches(normalized))
        {
            boundaries.Add(new EditorialBoundary(
                sentenceEnd.Index + sentenceEnd.Length,
                EditorialBoundaryKind.Sentence));
        }

        // Always retain the geometric complete-word split as a last-resort boundary.
        boundaries.Add(new EditorialBoundary(idealEnd, EditorialBoundaryKind.Word));

        // Bound the search to a useful neighbourhood. Distant paragraph boundaries should not win
        // merely because they have a stronger semantic rank.
        var window = Math.Max(90, normalized.Length / 4);
        return boundaries
            .Where(boundary => Math.Abs(boundary.EndIndex - idealEnd) <= window
                               || boundary.Kind == EditorialBoundaryKind.Word)
            .GroupBy(boundary => boundary.EndIndex)
            .Select(group => group.OrderBy(boundary => boundary.Kind).First())
            .OrderBy(boundary => boundary.EndIndex)
            .ToArray();
    }

    private int MeasureLineCount(
        string? text,
        float fontSize,
        float width,
        FontWeight weight)
        => MeasureWrapped(text, fontSize, width, weight).LineCount;

    private float MeasureTextHeight(
        string? text,
        float fontSize,
        float width,
        float lineHeight,
        FontWeight weight,
        float paragraphSpacingPoints = 0f)
    {
        var result = MeasureWrapped(text, fontSize, width, weight);
        var textHeight = Math.Max(fontSize * lineHeight, result.LineCount * fontSize * lineHeight);
        var paragraphGap = Math.Max(0, result.ParagraphCount - 1) * Math.Max(0f, paragraphSpacingPoints);
        return textHeight + paragraphGap;
    }

    private WrappedMeasurement MeasureWrapped(
        string? text,
        float fontSize,
        float width,
        FontWeight weight)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new WrappedMeasurement(1, 1);
        }

        var availableWidth = Math.Max(24f, width);
        using var paint = new SKPaint
        {
            Typeface = GetTypeface(weight),
            TextSize = fontSize,
            IsAntialias = true,
            SubpixelText = true
        };

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var paragraphs = normalized
            .Split('\n')
            .Select(raw => Whitespace.Replace(raw.Trim(), " "))
            .Where(paragraph => paragraph.Length > 0)
            .ToArray();
        if (paragraphs.Length == 0)
        {
            return new WrappedMeasurement(1, 1);
        }

        var lines = 0;

        foreach (var paragraph in paragraphs)
        {

            var currentWidth = 0f;
            var hasWord = false;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var wordWidth = paint.MeasureText(word);
                var spaceWidth = hasWord ? paint.MeasureText(" ") : 0f;

                if (hasWord && currentWidth + spaceWidth + wordWidth > availableWidth)
                {
                    lines++;
                    currentWidth = 0f;
                    hasWord = false;
                    spaceWidth = 0f;
                }

                if (wordWidth > availableWidth)
                {
                    if (hasWord)
                    {
                        lines++;
                        currentWidth = 0f;
                        hasWord = false;
                    }

                    var chunks = Math.Max(1, (int)Math.Ceiling(wordWidth / availableWidth));
                    lines += Math.Max(0, chunks - 1);
                    currentWidth = Math.Min(availableWidth, wordWidth / chunks);
                    hasWord = true;
                    continue;
                }

                currentWidth += spaceWidth + wordWidth;
                hasWord = true;
            }

            if (hasWord)
            {
                lines++;
            }
        }

        return new WrappedMeasurement(Math.Max(1, lines), paragraphs.Length);
    }

    private SKTypeface GetTypeface(FontWeight weight)
    {
        var set = EnsureTypefaces();
        return weight switch
        {
            FontWeight.SemiBold => set.SemiBold,
            _ => set.Regular
        };
    }

    private TypefaceSet EnsureTypefaces()
    {
        if (_typefaces is not null)
        {
            return _typefaces;
        }

        lock (_gate)
        {
            if (_typefaces is not null)
            {
                return _typefaces;
            }

            _ = _fontService.EnsureRegistered();
            var roots = CandidateRoots().ToArray();
            var regularPath = roots
                .Select(root => Path.Combine(root, "dm-sans", "DMSans-Regular.ttf"))
                .FirstOrDefault(File.Exists);
            var semiboldPath = roots
                .Select(root => Path.Combine(root, "dm-sans", "DMSans-SemiBold.ttf"))
                .FirstOrDefault(File.Exists);

            var regular = regularPath is not null
                ? SKTypeface.FromFile(regularPath)
                : SKTypeface.Default;
            var semibold = semiboldPath is not null
                ? SKTypeface.FromFile(semiboldPath)
                : regular;

            if (regularPath is null)
            {
                _logger.LogWarning(
                    "DM Sans was not available to the brochure measurement service. Print measurements will use the platform fallback typeface until the offline font package is installed.");
            }

            _typefaces = new TypefaceSet(
                regular ?? SKTypeface.Default,
                semibold ?? regular ?? SKTypeface.Default,
                OwnsRegular: regularPath is not null,
                OwnsSemiBold: semiboldPath is not null && !string.Equals(regularPath, semiboldPath, StringComparison.OrdinalIgnoreCase));
            return _typefaces;
        }
    }

    private IEnumerable<string> CandidateRoots()
    {
        if (!string.IsNullOrWhiteSpace(_environment.ContentRootPath))
        {
            yield return Path.Combine(
                _environment.ContentRootPath,
                "Resources",
                "Publications",
                "Fonts");
        }

        if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
        {
            yield return Path.Combine(
                _environment.WebRootPath,
                "fonts",
                "publications");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_typefaces is null)
        {
            return;
        }

        if (_typefaces.OwnsSemiBold)
        {
            _typefaces.SemiBold.Dispose();
        }
        if (_typefaces.OwnsRegular)
        {
            _typefaces.Regular.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BrochurePrintMeasurementService));
        }
    }

    private enum FontWeight
    {
        Regular,
        SemiBold
    }

    private sealed record WrappedMeasurement(int LineCount, int ParagraphCount);

    private sealed record FloatNarrativeSplit(
        string Leading,
        string Continuation,
        string Trailing,
        float LeadingHeightPoints,
        float ContinuationHeightPoints,
        float TrailingHeightPoints,
        BrochureFloatSplitKind Kind,
        float RemainderGapPoints);

    private enum EditorialBoundaryKind
    {
        Paragraph = 0,
        Sentence = 1,
        Word = 2
    }

    private sealed record EditorialBoundary(
        int EndIndex,
        EditorialBoundaryKind Kind);

    private sealed record BoundaryMeasurement(
        int EndIndex,
        float HeightPoints,
        float Score,
        EditorialBoundaryKind Kind);

    private sealed record TypefaceSet(
        SKTypeface Regular,
        SKTypeface SemiBold,
        bool OwnsRegular,
        bool OwnsSemiBold);
}
