using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using ProjectManagement.Utilities.Reporting;
using SkiaSharp;

namespace ProjectManagement.Services.Publications;

public interface IBrochurePrintMeasurementService
{
    BrochurePrintProjectMeasurement MeasureProject(
        BrochurePrintPlanningItem item,
        BrochurePrintLayoutVariant variant);

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
        BrochurePrintLayoutVariant variant)
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

        // Long project titles are allowed to use a slightly smaller title treatment, but the
        // adjustment is bounded and measured rather than inferred from character count.
        while (titleLines > 2 && titleFontSize > 7.2f)
        {
            titleFontSize -= .25f;
            titleLines = MeasureLineCount(
                item.ProjectName.ToUpperInvariant(),
                titleFontSize,
                titleWidth,
                FontWeight.SemiBold);
        }

        var titleHeight = Math.Max(
            18f,
            (titleLines * titleFontSize * 1.02f) + 5.5f);

        var hasPrimary = item.HasPrimaryPhoto;
        var hasSecond = item.HasSecondaryPhoto
                        && item.ImageMode != BrochureImageMode.Single;
        var imageWidth = hasPrimary ? Math.Max(92f, spec.ImageWidthPoints) : 0f;

        var bodyInnerWidth = moduleWidth
                             - (BrochurePrintLayoutMetrics.ModuleBorderPoints * 2f)
                             - (spec.BodyPaddingPoints * 2f);
        var textWidth = hasPrimary
            ? bodyInnerWidth - imageWidth - BrochurePrintLayoutMetrics.TextImageGapPoints
            : bodyInnerWidth;
        textWidth = Math.Max(120f, textWidth);

        var textHeight = MeasureTextHeight(
            item.Narrative,
            spec.BodyFontSize,
            textWidth,
            spec.BodyLineHeight,
            FontWeight.Regular);

        var imageHeight = 0f;
        if (hasPrimary)
        {
            var singleHeight = imageWidth * 9f / 16f;
            imageHeight = hasSecond
                ? (singleHeight * 2f) + 4f
                : singleHeight;
        }

        var rowHeight = Math.Max(textHeight, imageHeight);
        var totalHeight = titleHeight
                          + (spec.BodyPaddingPoints * 2f)
                          + rowHeight
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
            TextWidthPoints: textWidth,
            TextHeightPoints: textHeight,
            ImageHeightPoints: imageHeight,
            QualityRank: spec.QualityRank);
    }

    public BrochurePrintClosingMeasurement MeasureClosing(
        BrochurePrintMatter? matter,
        string? strapline)
    {
        ThrowIfDisposed();
        matter ??= new BrochurePrintMatter(null, null, null, null, null, null, null, null);

        var outerWidth = BrochurePrintLayoutMetrics.ModuleWidthPoints;
        var visionInnerWidth = outerWidth - 8f - 14f; // 4 pt border each side + 7 pt padding each side.
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
            FontWeight.Regular);
        var visionPanelHeight = 8f + 14f + visionHeadingHeight + 4f + visionBodyHeight;

        var newSimulatorInnerWidth = outerWidth - 14f;
        var newSimulatorText = $"New Simulators. {matter.NewSimulatorsGuidance}".Trim();
        var newSimulatorHeight = MeasureTextHeight(
            newSimulatorText,
            BrochurePrintLayoutMetrics.ClosingNewSimulatorsFontSize,
            newSimulatorInnerWidth,
            BrochurePrintLayoutMetrics.ClosingNewSimulatorsLineHeight,
            FontWeight.SemiBold) + 14f;

        var straplineHeight = Math.Max(
            9f,
            MeasureTextHeight(
                strapline,
                BrochurePrintLayoutMetrics.ClosingStraplineFontSize,
                outerWidth - 16f,
                1.05f,
                FontWeight.SemiBold) + 2f);

        var total = 1f
                    + visionPanelHeight
                    + 5f
                    + newSimulatorHeight
                    + 5f
                    + straplineHeight
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
        var contactWidth = (BrochurePrintLayoutMetrics.ReferenceWidthPoints - 26f) / 2f;

        // Centre statement is intentionally larger than Phase 8 and is measured independently.
        var centreFont = BrochurePrintLayoutMetrics.FrontCentrePreferredFontSize;
        var centreTextHeight = MeasureTextHeight(
            matter.CentreStatement,
            centreFont,
            bodyWidth - 20f,
            BrochurePrintLayoutMetrics.FrontCentreLineHeight,
            FontWeight.SemiBold);
        var centreBlockHeight = Math.Max(48f, centreTextHeight + 18f);

        // Contact details use explicit line breaks from the approved reference copy. Their block
        // height therefore reacts to real line wrapping rather than a fixed 98 pt footer.
        var contactFont = BrochurePrintLayoutMetrics.FrontContactPreferredFontSize;
        var contactBodyHeight = Math.Max(
            MeasureTextHeight(
                matter.DevelopingAgency,
                contactFont,
                contactWidth,
                BrochurePrintLayoutMetrics.FrontContactLineHeight,
                FontWeight.SemiBold),
            MeasureTextHeight(
                matter.ManufacturingAgency,
                contactFont,
                contactWidth,
                BrochurePrintLayoutMetrics.FrontContactLineHeight,
                FontWeight.SemiBold));
        var contactBlockHeight = Math.Max(86f, contactBodyHeight + 27f);

        var straplineHeight = Math.Max(
            BrochurePrintLayoutMetrics.FrontStraplineHeightPoints,
            MeasureTextHeight(strapline, 8.5f, bodyWidth, 1.05f, FontWeight.SemiBold) + 8f);

        var selectedBodyFont = BrochurePrintLayoutMetrics.FrontBodyMinimumFontSize;
        var selectedBodyHeight = 0f;
        var selectedHeroHeight = 0f;
        var selectedSpacing = 5.5f;
        var fits = false;

        foreach (var bodyFont in new[] { 9.0f, 8.8f, 8.6f, 8.4f })
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

            // If a particularly short user edit would leave more than the maximum hero allowance,
            // keep the hero within the approved visual band and use the residual space as deliberate
            // breathing inside the body rather than a blank void above the contact strip.
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
        FontWeight weight)
    {
        var result = MeasureWrapped(text, fontSize, width, weight);
        return Math.Max(fontSize * lineHeight, result.LineCount * fontSize * lineHeight);
    }

    private WrappedMeasurement MeasureWrapped(
        string? text,
        float fontSize,
        float width,
        FontWeight weight)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new WrappedMeasurement(1);
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
        var paragraphs = normalized.Split('\n');
        var lines = 0;

        foreach (var rawParagraph in paragraphs)
        {
            var paragraph = Whitespace.Replace(rawParagraph.Trim(), " ");
            if (paragraph.Length == 0)
            {
                lines++;
                continue;
            }

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

        return new WrappedMeasurement(Math.Max(1, lines));
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

    private sealed record WrappedMeasurement(int LineCount);

    private sealed record TypefaceSet(
        SKTypeface Regular,
        SKTypeface SemiBold,
        bool OwnsRegular,
        bool OwnsSemiBold);
}
