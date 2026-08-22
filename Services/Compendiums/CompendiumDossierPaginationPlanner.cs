using System.Text.RegularExpressions;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Shared A4 dossier composition planner used by browser review and PDF planning. Phase 40 makes
/// physical feasibility authoritative: semantic narrative, DM Sans title wrapping and fixed page
/// chrome are all measured against the same geometry later used by QuestPDF. Editorial scoring is
/// applied only after a candidate is physically valid.
/// </summary>
public static class CompendiumDossierPaginationPlanner
{
    private const float PhysicalContentHeightPoints = CompendiumLayoutMetrics.ProjectContentHeightPoints;
    private const float FullNarrativeWidthPoints = CompendiumLayoutMetrics.ContentWidthPoints;

    private static readonly Regex MarkdownNoiseRegex = new(
        @"[*_`#>]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Decision(
        CompendiumDossierLayout Layout,
        float PrimaryImageHeightPoints,
        float NarrativeFontScale,
        int FirstPageNarrativeBudget,
        float FirstPageNarrativeHeightPoints,
        int FirstPageSpecificationCount,
        int SpecificationColumns,
        int ProgrammeColumns,
        int EstimatedPageCount,
        bool UsesContinuation,
        string Reason,
        string PaginationNote,
        bool HasEditorialWarning = false,
        string? EditorialWarning = null);

    public static Decision Resolve(
        CompendiumDossierLayout requested,
        CompendiumDossierLayout initiallyResolved,
        int availablePhotoCount,
        string? narrative,
        IReadOnlyList<string>? technicalSpecifications,
        int programmeModuleCount,
        string? projectName,
        int? primaryImageEffectiveDpi = null,
        CompendiumBalancedTextFlowMode balancedTextFlowMode = CompendiumBalancedTextFlowMode.FlowBelowImage,
        int? primaryImageSourceWidth = null,
        int? primaryImageSourceHeight = null,
        CompendiumImageFitMode primaryImageFitMode = CompendiumImageFitMode.Fill,
        string? additionalNote = null,
        IReadOnlyList<CompendiumProgrammeModuleDto>? programmeModules = null,
        CompendiumProjectParticularsStyle projectParticularsStyle = CompendiumProjectParticularsStyle.Panel,
        string? projectKicker = null)
    {
        availablePhotoCount = Math.Clamp(availablePhotoCount, 0, 3);
        var narrativeMarkdown = CompendiumNarrativeParser.Normalize(narrative);
        var cleanNarrative = CleanText(narrativeMarkdown);
        var cleanAdditionalNote = CompendiumPublicationNotePolicy.Normalize(additionalNote);
        var specifications = (technicalSpecifications ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(6)
            .ToArray();
        programmeModuleCount = Math.Clamp(programmeModuleCount, 0, 4);
        projectParticularsStyle = CompendiumProjectParticularsLayoutPolicy.Normalize(projectParticularsStyle);

        var specificationColumns = ResolveTechnicalSpecificationColumns(specifications);
        var explicitLayout = requested != CompendiumDossierLayout.Automatic;
        var measurementSession = new CompendiumDossierTextMeasurementService.Session();
        var particularsLayout = programmeModules is { Count: > 0 }
            ? CompendiumProjectParticularsLayoutPolicy.Resolve(projectParticularsStyle, programmeModules, measurementSession)
            : ResolveLegacyParticularsLayout(projectParticularsStyle, programmeModuleCount);
        var programmeColumns = particularsLayout.Columns > 0
            ? particularsLayout.Columns
            : ResolveProgrammeColumns(programmeModuleCount);
        var programmeHeight = particularsLayout.HeightPoints;

        if (!explicitLayout
            && !CompendiumImageQualityPolicy.IsAutomaticLayoutAllowed(initiallyResolved, primaryImageEffectiveDpi))
        {
            initiallyResolved = specifications.Length >= 4
                ? CompendiumDossierLayout.Technical
                : CompendiumDossierLayout.Balanced;
        }

        var layouts = BuildCandidateLayouts(initiallyResolved, explicitLayout, availablePhotoCount, primaryImageEffectiveDpi);
        var validCandidates = new List<Candidate>();

        foreach (var layout in layouts)
        {
            foreach (var imageHeight in CandidateImageHeights(layout, availablePhotoCount, primaryImageEffectiveDpi, protectPrintFidelity: !explicitLayout))
            {
                foreach (var narrativeScale in CandidateNarrativeScales(cleanNarrative))
                {
                    var evaluated = Evaluate(
                        layout,
                        imageHeight,
                        narrativeScale,
                        availablePhotoCount,
                        narrativeMarkdown,
                        specifications,
                        programmeModuleCount,
                        projectName,
                        projectKicker,
                        specificationColumns,
                        programmeColumns,
                        programmeHeight,
                        balancedTextFlowMode,
                        primaryImageSourceWidth,
                        primaryImageSourceHeight,
                        primaryImageFitMode,
                        cleanAdditionalNote,
                        measurementSession);

                    if (!evaluated.FitsAllContent) continue;

                    validCandidates.Add(evaluated with
                    {
                        CompositionScore = ScoreCandidate(
                            evaluated,
                            initiallyResolved,
                            explicitLayout,
                            availablePhotoCount,
                            cleanNarrative,
                            specifications,
                            programmeModuleCount,
                            primaryImageEffectiveDpi,
                            balancedTextFlowMode)
                    });
                }
            }
        }

        var editorialCandidates = validCandidates
            .Where(candidate => candidate.IsEditoriallyValid)
            .ToArray();
        var selectableCandidates = editorialCandidates.Length > 0
            ? editorialCandidates
            : explicitLayout
                ? validCandidates.ToArray()
                : Array.Empty<Candidate>();

        if (selectableCandidates.Length > 0)
        {
            var best = selectableCandidates
                .OrderByDescending(candidate => candidate.CompositionScore)
                .ThenBy(candidate => Math.Abs(candidate.ResidualSpacePoints - ResolveIdealResidualSpace(specifications.Length, programmeModuleCount)))
                .ThenBy(candidate => candidate.SideOverflowHeightPoints + candidate.SideRemainingHeightPoints)
                .ThenByDescending(candidate => candidate.ImageHeight)
                .First();

            var changedLayout = best.Layout != initiallyResolved;
            var preferredHeight = PreferredImageHeight(best.Layout, availablePhotoCount);
            var expandedPhotography = best.RequestedImageHeight > preferredHeight + .1f;
            var reducedPhotography = best.RequestedImageHeight + .1f < preferredHeight;
            var aspectRatioFit = primaryImageFitMode == CompendiumImageFitMode.Fit
                                 && primaryImageSourceWidth is > 0
                                 && primaryImageSourceHeight is > 0
                                 && best.ImageHeight + .1f < best.RequestedImageHeight;
            var improvedTypography = best.NarrativeFontScale > 1.001f;
            var qualityConstrained = primaryImageEffectiveDpi is > 0 and < CompendiumImageQualityPolicy.AcceptablePrintDpi;
            var qualityProtected = !explicitLayout && qualityConstrained;

            var reason = qualityProtected && changedLayout
                ? $"Automatic composition selected {Display(best.Layout)} to protect print fidelity at approximately {primaryImageEffectiveDpi} DPI"
                : changedLayout
                    ? $"Automatic composition selected {Display(best.Layout)} over {Display(initiallyResolved)} after comparing one-page candidates for readability, photography and whitespace balance"
                    : aspectRatioFit && improvedTypography
                        ? $"{Display(best.Layout)} retained; Fit preserved the complete image at its natural printed aspect ratio and spare space was invested in narrative readability"
                    : aspectRatioFit
                        ? $"{Display(best.Layout)} retained; Fit preserved the complete image at its natural printed aspect ratio"
                    : expandedPhotography && improvedTypography
                        ? $"{Display(best.Layout)} retained; available page space was invested in larger photography and more readable narrative typography"
                        : expandedPhotography
                            ? $"{Display(best.Layout)} retained; available page space was invested in larger photography"
                            : reducedPhotography && qualityConstrained && !explicitLayout
                                ? $"{Display(best.Layout)} retained; photography was reduced to protect print fidelity"
                                : reducedPhotography
                                    ? $"{Display(best.Layout)} retained; photography was reduced to preserve readable one-page content"
                                    : improvedTypography
                                        ? $"{Display(best.Layout)} retained; available page space was used to improve narrative readability"
                                        : $"{Display(best.Layout)} provides the strongest one-page balance for the current content";

            return new Decision(
                best.Layout,
                best.ImageHeight,
                best.NarrativeFontScale,
                Math.Max(800, cleanNarrative.Length + 220),
                best.NarrativeHeightCapacityPoints,
                specifications.Length,
                specificationColumns,
                programmeColumns,
                1,
                false,
                explicitLayout && !changedLayout
                    ? $"Publisher-selected {Display(best.Layout)} retained; {reason.ToLowerInvariant()}"
                    : reason,
                best.IsEditoriallyValid
                    ? $"1 dossier page · {Display(best.Layout)} · optimised"
                    : $"1 dossier page · {Display(best.Layout)} · publisher review required",
                HasEditorialWarning: !best.IsEditoriallyValid || !string.IsNullOrWhiteSpace(best.EditorialWarning),
                EditorialWarning: best.EditorialWarning);
        }

        var fallbackLayout = layouts[0];
        IReadOnlyList<float> fallbackHeights = Array.Empty<float>();
        Candidate? fallbackEvaluation = null;

        foreach (var candidateLayout in layouts)
        {
            var candidateHeights = CandidateImageHeights(
                candidateLayout,
                availablePhotoCount,
                primaryImageEffectiveDpi,
                protectPrintFidelity: !explicitLayout);
            if (candidateHeights.Count == 0) continue;

            var candidateCompactHeight = candidateHeights[^1];
            var candidateEvaluation = Evaluate(
                candidateLayout,
                candidateCompactHeight,
                1f,
                availablePhotoCount,
                narrativeMarkdown,
                Array.Empty<string>(),
                programmeModuleCount,
                projectName,
                projectKicker,
                1,
                programmeColumns,
                programmeHeight,
                balancedTextFlowMode,
                primaryImageSourceWidth,
                primaryImageSourceHeight,
                primaryImageFitMode,
                string.Empty,
                measurementSession);

            if (fallbackEvaluation is null)
            {
                fallbackLayout = candidateLayout;
                fallbackHeights = candidateHeights;
                fallbackEvaluation = candidateEvaluation;
            }

            if (explicitLayout || candidateEvaluation.IsEditoriallyValid)
            {
                fallbackLayout = candidateLayout;
                fallbackHeights = candidateHeights;
                fallbackEvaluation = candidateEvaluation;
                break;
            }
        }

        if (fallbackHeights.Count == 0)
        {
            fallbackHeights = new[] { availablePhotoCount > 0
                ? CompendiumDossierEditorialPolicy.MinimumEditorialFillHeightPoints(fallbackLayout)
                : 0f };
        }

        var compactHeight = fallbackHeights[^1];
        var withoutSpecs = fallbackEvaluation ?? Evaluate(
            fallbackLayout,
            compactHeight,
            1f,
            availablePhotoCount,
            narrativeMarkdown,
            Array.Empty<string>(),
            programmeModuleCount,
            projectName,
            projectKicker,
            1,
            programmeColumns,
            programmeHeight,
            balancedTextFlowMode,
            primaryImageSourceWidth,
            primaryImageSourceHeight,
            primaryImageFitMode,
            string.Empty,
            measurementSession);

        var firstSpecificationCount = 0;
        var firstPageEvaluation = withoutSpecs;

        // If the Project Brief itself fits, retain as much technical reference as can physically
        // remain on page one. An Additional Note that causes continuation must not unnecessarily
        // evict specifications that otherwise fit safely.
        if (withoutSpecs.FitsAllContent && specifications.Length > 0)
        {
            for (var count = 1; count <= specifications.Length; count++)
            {
                var candidateWithSpecs = Evaluate(
                    fallbackLayout,
                    compactHeight,
                    1f,
                    availablePhotoCount,
                    narrativeMarkdown,
                    specifications.Take(count).ToArray(),
                    programmeModuleCount,
                    projectName,
                    projectKicker,
                    specificationColumns,
                    programmeColumns,
                    programmeHeight,
                    balancedTextFlowMode,
                    primaryImageSourceWidth,
                    primaryImageSourceHeight,
                    primaryImageFitMode,
                    string.Empty,
                    measurementSession);
                if (!candidateWithSpecs.FitsAllContent) break;
                if (!explicitLayout && !candidateWithSpecs.IsEditoriallyValid) break;
                firstSpecificationCount = count;
                firstPageEvaluation = candidateWithSpecs;
            }
        }

        var firstNarrativeBudget = firstPageEvaluation.FitsAllContent
            ? Math.Max(800, cleanNarrative.Length + 220)
            : Math.Max(760, firstPageEvaluation.NarrativeCapacityCharacters);
        var firstNarrativeHeight = Math.Max(36f, firstPageEvaluation.NarrativeHeightCapacityPoints);

        var firstPageFlow = CompendiumDossierNarrativeFlowPlanner.Resolve(
            narrativeMarkdown,
            balancedTextFlowMode,
            fallbackLayout,
            availablePhotoCount > 0,
            firstPageEvaluation.ImageHeight,
            1f,
            firstNarrativeBudget,
            CompendiumNarrativeAlignment.Left,
            ResolveBalancedSideWidthPoints(availablePhotoCount),
            firstNarrativeHeight);
        var narrativeContinuationPages = firstPageFlow.ContinuationSegments.Count;

        var continuationBodyHeight = ResolveContinuationBodyHeightPoints(projectName, measurementSession);
        var remainingSpecifications = specifications.Skip(firstSpecificationCount).ToArray();
        var specificationChunks = SplitTechnicalSpecificationsForPhysicalPages(
            remainingSpecifications,
            specificationColumns,
            continuationBodyHeight);
        var specificationContinuationPages = specificationChunks.Count;

        // A short final Project Brief continuation and a compact technical chunk may share the same
        // continuation page. The decision is made from the same physical font measurements used by
        // the renderer, not from character counts.
        var firstSpecificationChunkShared = narrativeContinuationPages > 0
            && specificationChunks.Count > 0
            && CanShareContinuationPage(
                firstPageFlow.ContinuationSegments[^1],
                specificationChunks[0],
                specificationColumns,
                1f,
                availableHeightPoints: continuationBodyHeight);
        if (firstSpecificationChunkShared)
            specificationContinuationPages--;

        var lastContinuationNarrative = narrativeContinuationPages > 0
            ? firstPageFlow.ContinuationSegments[^1]
            : string.Empty;
        IReadOnlyList<string> lastContinuationSpecifications = Array.Empty<string>();
        if (specificationChunks.Count > (firstSpecificationChunkShared ? 1 : 0))
        {
            lastContinuationNarrative = string.Empty;
            lastContinuationSpecifications = specificationChunks[^1];
        }
        else if (firstSpecificationChunkShared)
        {
            lastContinuationSpecifications = specificationChunks[0];
        }

        var noteContinuationPages = 0;
        if (!string.IsNullOrWhiteSpace(cleanAdditionalNote))
        {
            var noteChunks = CompendiumDossierNarrativeFlowPlanner.SplitForPhysicalPages(
                cleanAdditionalNote,
                FullNarrativeWidthPoints,
                continuationBodyHeight,
                1f,
                includeHeading: false,
                allowMinorHeadings: false);
            noteContinuationPages = Math.Max(1, noteChunks.Count);

            var hasExistingContinuation = narrativeContinuationPages + specificationContinuationPages > 0;
            if (hasExistingContinuation
                && noteChunks.Count > 0
                && CanShareContinuationPage(
                    lastContinuationNarrative,
                    lastContinuationSpecifications,
                    specificationColumns,
                    1f,
                    noteChunks[0],
                    continuationBodyHeight))
            {
                noteContinuationPages--;
            }
        }

        var estimatedPages = 1 + narrativeContinuationPages + specificationContinuationPages + noteContinuationPages;
        var continuationParts = new List<string>(3);
        if (narrativeContinuationPages > 0) continuationParts.Add("project brief");
        if (specificationContinuationPages > 0) continuationParts.Add("technical reference");
        if (!string.IsNullOrWhiteSpace(cleanAdditionalNote)) continuationParts.Add("additional note");
        var continuationReason = continuationParts.Count switch
        {
            0 => "project dossier continues",
            1 => $"{continuationParts[0]} continues",
            2 => $"{continuationParts[0]} and {continuationParts[1]} continue",
            _ => $"{string.Join(", ", continuationParts.Take(continuationParts.Count - 1))} and {continuationParts[^1]} continue"
        };

        return new Decision(
            fallbackLayout,
            withoutSpecs.ImageHeight,
            1f,
            firstNarrativeBudget,
            firstNarrativeHeight,
            firstSpecificationCount,
            specificationColumns,
            programmeColumns,
            estimatedPages,
            true,
            explicitLayout
                ? $"Publisher-selected {Display(fallbackLayout)} requires controlled continuation at the current content volume"
                : $"Content exceeds the safe one-page editorial envelope; {continuationReason}",
            $"{estimatedPages} dossier pages · {continuationReason}",
            HasEditorialWarning: !firstPageEvaluation.IsEditoriallyValid || !string.IsNullOrWhiteSpace(firstPageEvaluation.EditorialWarning),
            EditorialWarning: firstPageEvaluation.EditorialWarning);
    }

    public static int ResolveTechnicalSpecificationColumns(IReadOnlyList<string>? specifications)
    {
        var items = (specifications ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(6)
            .ToArray();
        if (items.Length == 0) return 1;

        var measurement = new CompendiumDossierTextMeasurementService.Session();
        static float TextWidthFor(int columns)
            => Math.Max(60f, FullNarrativeWidthPoints / Math.Max(1, columns) - 23f);

        bool FitsColumns(int columns, int maximumLines)
        {
            var width = TextWidthFor(columns);
            return items.All(item => measurement.MeasureAtFontSize(
                CleanText(item),
                width,
                fontSizePoints: 8.75f,
                lineHeightMultiplier: 1.22f).LineCount <= maximumLines);
        }

        if (items.Length >= 3 && FitsColumns(3, 2)) return 3;
        if (items.Length >= 2 && FitsColumns(2, 4)) return 2;
        return 1;
    }

    public static IReadOnlyList<IReadOnlyList<string>> SplitTechnicalSpecificationsForPhysicalPages(
        IReadOnlyList<string>? specifications,
        int columns,
        float availableHeightPoints)
    {
        var items = (specifications ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(6)
            .ToArray();
        if (items.Length == 0) return Array.Empty<IReadOnlyList<string>>();

        columns = Math.Clamp(columns, 1, 3);
        availableHeightPoints = Math.Max(80f, availableHeightPoints);
        var measurement = new CompendiumDossierTextMeasurementService.Session();
        var chunks = new List<IReadOnlyList<string>>();
        var current = new List<string>();

        foreach (var item in items)
        {
            var candidate = current.Append(item).ToArray();
            var height = EstimateSpecificationHeight(candidate, columns, measurement);
            if (current.Count > 0 && height > availableHeightPoints)
            {
                chunks.Add(current.ToArray());
                current.Clear();
            }
            current.Add(item);
        }

        if (current.Count > 0) chunks.Add(current.ToArray());
        return chunks;
    }

    public static float MeasureTechnicalSpecificationsHeight(
        IReadOnlyList<string>? specifications,
        int columns)
    {
        var items = (specifications ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(6)
            .ToArray();
        if (items.Length == 0) return 0f;
        return EstimateSpecificationHeight(
            items,
            Math.Clamp(columns, 1, 3),
            new CompendiumDossierTextMeasurementService.Session());
    }

    public static float MeasureAdditionalNoteHeightPoints(string? additionalNote, float narrativeFontScale)
        => EstimateAdditionalNoteHeight(
            CompendiumPublicationNotePolicy.Normalize(additionalNote),
            narrativeFontScale,
            new CompendiumDossierTextMeasurementService.Session());

    public static bool CanShareContinuationPage(
        string? narrative,
        IReadOnlyList<string>? specifications,
        int specificationColumns,
        float narrativeFontScale,
        string? additionalNote = null,
        float? availableHeightPoints = null)
    {
        var measurement = new CompendiumDossierTextMeasurementService.Session();
        var narrativeHeight = string.IsNullOrWhiteSpace(narrative)
            ? 0f
            : measurement.Measure(
                narrative,
                FullNarrativeWidthPoints,
                narrativeFontScale,
                includeHeading: false).HeightPoints;
        var specificationHeight = EstimateSpecificationHeight(
            (specifications ?? Array.Empty<string>()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            Math.Clamp(specificationColumns, 1, 3),
            measurement);
        var noteHeight = EstimateAdditionalNoteHeight(
            CompendiumPublicationNotePolicy.Normalize(additionalNote),
            narrativeFontScale,
            measurement);
        var interBlockSpacing = (narrativeHeight > 0f && specificationHeight > 0f
                ? CompendiumLayoutMetrics.ContinuationColumnSpacingPoints
                : 0f)
            + ((narrativeHeight > 0f || specificationHeight > 0f) && noteHeight > 0f
                ? CompendiumLayoutMetrics.ContinuationColumnSpacingPoints
                : 0f);
        var capacity = Math.Max(80f, availableHeightPoints ?? CompendiumPublicationNotePolicy.ContinuationBodyHeightPoints);
        return narrativeHeight + specificationHeight + noteHeight + interBlockSpacing <= capacity;
    }

    public static int ResolveProgrammeColumns(int moduleCount)
        => moduleCount switch
        {
            <= 1 => 1,
            2 => 2,
            3 => 3,
            _ => 2
        };

    public static float ResolvePrimaryFrameWidthPoints(CompendiumDossierLayout layout, int availablePhotoCount)
    {
        if (availablePhotoCount <= 0) return FullNarrativeWidthPoints;
        if (layout == CompendiumDossierLayout.MultiImageEditorial)
        {
            const float mosaicGap = 7f;
            var usableMosaicWidth = FullNarrativeWidthPoints - mosaicGap;
            return usableMosaicWidth * 1.55f / 2.55f;
        }

        if (layout != CompendiumDossierLayout.Balanced) return FullNarrativeWidthPoints;
        const float gap = 13f;
        var usableWidth = FullNarrativeWidthPoints - gap;
        return usableWidth * 1.12f / 2f;
    }

    public static float ResolveBalancedSideWidthPoints(int availablePhotoCount)
    {
        if (availablePhotoCount <= 0) return FullNarrativeWidthPoints;
        const float gap = 13f;
        var usableWidth = FullNarrativeWidthPoints - gap;
        return usableWidth * .88f / 2f;
    }

    public static float PreferredImageHeight(CompendiumDossierLayout layout, int availablePhotoCount)
    {
        if (availablePhotoCount <= 0) return 0f;
        return layout switch
        {
            CompendiumDossierLayout.VisualHero => 255f,
            CompendiumDossierLayout.MultiImageEditorial => 245f,
            CompendiumDossierLayout.Technical => 145f,
            _ => 246f
        };
    }

    public static float MaximumImageHeight(CompendiumDossierLayout layout, int availablePhotoCount)
    {
        if (availablePhotoCount <= 0) return 0f;
        return layout switch
        {
            CompendiumDossierLayout.VisualHero => 330f,
            CompendiumDossierLayout.MultiImageEditorial => 285f,
            CompendiumDossierLayout.Technical => 185f,
            _ => 300f
        };
    }

    private static Candidate Evaluate(
        CompendiumDossierLayout layout,
        float imageHeight,
        float narrativeFontScale,
        int availablePhotoCount,
        string narrative,
        IReadOnlyList<string> specifications,
        int programmeModuleCount,
        string? projectName,
        string? projectKicker,
        int specificationColumns,
        int programmeColumns,
        float programmeHeight,
        CompendiumBalancedTextFlowMode balancedTextFlowMode,
        int? primaryImageSourceWidth,
        int? primaryImageSourceHeight,
        CompendiumImageFitMode primaryImageFitMode,
        string additionalNote,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        narrativeFontScale = CompendiumNarrativeTypographyPolicy.NormalizeScale(narrativeFontScale);
        var titleHeight = EstimateTitleBlockHeight(projectName, projectKicker, measurementSession);
        programmeHeight = Math.Max(0f, programmeHeight);
        var specificationHeight = EstimateSpecificationHeight(specifications, specificationColumns, measurementSession);
        var additionalNoteHeight = EstimateAdditionalNoteHeight(additionalNote, narrativeFontScale, measurementSession);
        var hasPhoto = availablePhotoCount > 0 && imageHeight > 0f;
        var trailingBlockCount = (programmeHeight > 0f ? 1 : 0)
                                 + (specificationHeight > 0f ? 1 : 0)
                                 + (additionalNoteHeight > 0f ? 1 : 0);
        var fixedGaps = trailingBlockCount * CompendiumLayoutMetrics.ProjectColumnSpacingPoints;
        float sideRemaining = 0f;
        float sideOverflow = 0f;
        float sideBalanceRatio = 1f;
        var sideColumnBalanced = true;
        var flowBelowBalanced = true;
        string? sideColumnWarning = null;
        string? flowBelowWarning = null;

        var frameWidth = ResolvePrimaryFrameWidthPoints(layout, availablePhotoCount);
        var geometryFitMode = layout == CompendiumDossierLayout.MultiImageEditorial
            ? CompendiumImageFitMode.Fill
            : primaryImageFitMode;
        var geometry = hasPhoto
            ? CompendiumDossierImageGeometryPolicy.Resolve(
                frameWidth, imageHeight, primaryImageSourceWidth, primaryImageSourceHeight, geometryFitMode)
            : new CompendiumDossierImageGeometryPolicy.Geometry(frameWidth, 0f, frameWidth, 0f, geometryFitMode);
        var renderedImageHeight = hasPhoto ? geometry.RenderedHeightPoints : 0f;

        float mainHeight;
        int capacity;
        float narrativeHeightCapacity;
        if (!hasPhoto)
        {
            mainHeight = measurementSession.Measure(
                narrative, FullNarrativeWidthPoints, narrativeFontScale, includeHeading: true).HeightPoints;
            var available = Math.Max(0f, PhysicalContentHeightPoints - titleHeight - programmeHeight - specificationHeight - additionalNoteHeight - fixedGaps);
            narrativeHeightCapacity = available;
            capacity = EstimateNarrativeCapacityCharacters(available, 86, narrativeFontScale);
        }
        else if (layout == CompendiumDossierLayout.Balanced
                 && balancedTextFlowMode == CompendiumBalancedTextFlowMode.FlowBelowImage)
        {
            var sideWidth = ResolveBalancedSideWidthPoints(availablePhotoCount);
            var side = CompendiumDossierNarrativeFlowPlanner.AssessSideFlow(
                narrative, renderedImageHeight, narrativeFontScale, sideWidth, measurementSession);
            sideRemaining = side.RemainingHeightPoints;
            flowBelowBalanced = !side.HasExcessiveGap;
            if (!flowBelowBalanced)
                flowBelowWarning = "Text beside the image leaves excessive unused vertical space before the full-width continuation. PRISM should use another measured image height or text split for a better balance.";
            var belowHeight = string.IsNullOrWhiteSpace(side.BelowSegment)
                ? 0f
                : measurementSession.Measure(
                    side.BelowSegment, FullNarrativeWidthPoints, narrativeFontScale, includeHeading: false).HeightPoints;
            mainHeight = renderedImageHeight + (belowHeight > 0f ? 8f + belowHeight : 0f);
            var available = Math.Max(0f, PhysicalContentHeightPoints - titleHeight - programmeHeight - specificationHeight - additionalNoteHeight - fixedGaps);
            narrativeHeightCapacity = Math.Max(0f, available - renderedImageHeight - 8f);
            capacity = EstimateBalancedFlowCapacity(available, renderedImageHeight, narrativeFontScale);
        }
        else if (layout == CompendiumDossierLayout.Balanced)
        {
            var sideWidth = ResolveBalancedSideWidthPoints(availablePhotoCount);
            var narrativeHeight = measurementSession.Measure(
                narrative, sideWidth, narrativeFontScale, includeHeading: true).HeightPoints;
            var balance = CompendiumDossierEditorialPolicy.AssessSideColumn(renderedImageHeight, narrativeHeight);
            sideRemaining = balance.UnderfillHeightPoints;
            sideOverflow = balance.OverflowHeightPoints;
            sideBalanceRatio = balance.BalanceRatio;
            sideColumnBalanced = balance.IsEditoriallyBalanced;
            sideColumnWarning = balance.Warning;
            mainHeight = Math.Max(renderedImageHeight, narrativeHeight);
            var available = Math.Max(0f, PhysicalContentHeightPoints - titleHeight - programmeHeight - specificationHeight - additionalNoteHeight - fixedGaps);
            narrativeHeightCapacity = available;
            capacity = EstimateNarrativeCapacityCharacters(available, 42, narrativeFontScale);
        }
        else
        {
            var narrativeHeight = measurementSession.Measure(
                narrative, FullNarrativeWidthPoints, narrativeFontScale, includeHeading: true).HeightPoints;
            mainHeight = renderedImageHeight + 9f + narrativeHeight;
            var available = Math.Max(0f, PhysicalContentHeightPoints - titleHeight - programmeHeight - specificationHeight - additionalNoteHeight - fixedGaps - renderedImageHeight - 9f);
            narrativeHeightCapacity = available;
            capacity = EstimateNarrativeCapacityCharacters(available, 86, narrativeFontScale);
        }

        var totalHeight = titleHeight + mainHeight + programmeHeight + specificationHeight + additionalNoteHeight + fixedGaps;
        var imageGeometryValid = CompendiumDossierEditorialPolicy.IsImageGeometryEditoriallyValid(
            layout, geometryFitMode, hasPhoto, renderedImageHeight);
        var shallowFitWarning = CompendiumDossierEditorialPolicy.ShallowFitWarning(geometryFitMode, hasPhoto, renderedImageHeight);
        var editorialWarning = !imageGeometryValid
            ? $"The {Display(layout)} Fill treatment would reduce the publication image below the editorial minimum. Use a larger image treatment, Fit, or controlled continuation."
            : !sideColumnBalanced
                ? sideColumnWarning
                : !flowBelowBalanced
                    ? flowBelowWarning
                    : shallowFitWarning;

        return new Candidate(
            layout,
            imageHeight,
            renderedImageHeight,
            narrativeFontScale,
            totalHeight <= PhysicalContentHeightPoints,
            totalHeight,
            capacity)
        {
            SideRemainingHeightPoints = sideRemaining,
            SideOverflowHeightPoints = sideOverflow,
            SideBalanceRatio = sideBalanceRatio,
            NarrativeHeightCapacityPoints = narrativeHeightCapacity,
            IsEditoriallyValid = imageGeometryValid && sideColumnBalanced && flowBelowBalanced,
            EditorialWarning = editorialWarning
        };
    }

    private static int ScoreCandidate(
        Candidate candidate,
        CompendiumDossierLayout initiallyResolved,
        bool explicitLayout,
        int availablePhotoCount,
        string narrative,
        IReadOnlyList<string> specifications,
        int programmeModuleCount,
        int? primaryImageEffectiveDpi,
        CompendiumBalancedTextFlowMode balancedTextFlowMode)
    {
        var idealResidual = ResolveIdealResidualSpace(specifications.Count, programmeModuleCount);
        var residual = candidate.ResidualSpacePoints;
        var score = 1000f;

        score -= Math.Abs(residual - idealResidual) * 1.25f;
        if (residual > 80f) score -= (residual - 80f) * 1.75f;
        if (residual > 110f) score -= (residual - 110f) * 1.6f;
        if (residual < 45f) score -= (45f - residual) * 3.2f;

        if (candidate.Layout == initiallyResolved) score += explicitLayout ? 120f : 44f;
        else if (!explicitLayout) score -= 5f;

        if (availablePhotoCount > 0 && candidate.ImageHeight > 0f)
        {
            var maximum = Math.Max(1f, MaximumImageHeight(candidate.Layout, availablePhotoCount));
            var imageRatio = Math.Clamp(candidate.ImageHeight / maximum, 0f, 1f);
            score += imageRatio * (candidate.Layout switch
            {
                CompendiumDossierLayout.VisualHero => 38f,
                CompendiumDossierLayout.MultiImageEditorial => 32f,
                CompendiumDossierLayout.Balanced => 25f,
                CompendiumDossierLayout.Technical => 16f,
                _ => 18f
            });
        }

        score += (candidate.NarrativeFontScale - 1f) * 330f;

        if (!explicitLayout)
            score -= CompendiumImageQualityPolicy.AutomaticLayoutPenalty(candidate.Layout, primaryImageEffectiveDpi);

        if (candidate.Layout == CompendiumDossierLayout.Balanced
            && balancedTextFlowMode == CompendiumBalancedTextFlowMode.FlowBelowImage
            && candidate.SideRemainingHeightPoints > 0f)
        {
            if (candidate.SideRemainingHeightPoints <= 18f) score += 24f;
            else if (candidate.SideRemainingHeightPoints <= 30f) score += 10f;
            else if (candidate.SideRemainingHeightPoints > 40f) score -= (candidate.SideRemainingHeightPoints - 40f) * 1.8f;
        }

        if (candidate.Layout == CompendiumDossierLayout.Balanced
            && balancedTextFlowMode == CompendiumBalancedTextFlowMode.SideColumn)
        {
            if (candidate.SideOverflowHeightPoints <= CompendiumDossierEditorialPolicy.PreferredSideBalanceTolerancePoints
                && candidate.SideRemainingHeightPoints <= CompendiumDossierEditorialPolicy.PreferredSideBalanceTolerancePoints)
            {
                score += 28f;
            }

            score -= candidate.SideOverflowHeightPoints * 2.6f;
            score -= candidate.SideRemainingHeightPoints * 1.7f;
            if (!candidate.IsEditoriallyValid) score -= 420f;
        }

        var narrativeLength = narrative.Length;
        if (candidate.Layout == CompendiumDossierLayout.VisualHero && narrativeLength <= 1750 && availablePhotoCount > 0) score += 13f;
        if (candidate.Layout == CompendiumDossierLayout.VisualHero && narrativeLength > 2500) score -= 24f;
        if (candidate.Layout == CompendiumDossierLayout.Balanced && narrativeLength is >= 1000 and <= 2600) score += 10f;
        if (candidate.Layout == CompendiumDossierLayout.Balanced && narrativeLength > 3300) score -= 28f;
        if (candidate.Layout == CompendiumDossierLayout.MultiImageEditorial && availablePhotoCount >= 2) score += 12f;
        if (candidate.Layout == CompendiumDossierLayout.MultiImageEditorial && narrativeLength > 2300) score -= 18f;
        if (candidate.Layout == CompendiumDossierLayout.Technical && specifications.Count > 0) score += 11f;
        if (candidate.Layout == CompendiumDossierLayout.Technical && specifications.Count == 0 && narrativeLength < 1800) score -= 10f;

        return (int)Math.Round(score, MidpointRounding.AwayFromZero);
    }

    private static float ResolveIdealResidualSpace(int specificationCount, int programmeModuleCount)
        => specificationCount > 0 ? 54f : programmeModuleCount > 0 ? 62f : 68f;

    private static IReadOnlyList<CompendiumDossierLayout> BuildCandidateLayouts(
        CompendiumDossierLayout initiallyResolved,
        bool explicitLayout,
        int availablePhotoCount,
        int? primaryImageEffectiveDpi)
    {
        if (explicitLayout) return new[] { initiallyResolved };

        var result = new List<CompendiumDossierLayout>();
        void Add(CompendiumDossierLayout value)
        {
            if (value == CompendiumDossierLayout.MultiImageEditorial && availablePhotoCount < 2) return;
            if (!CompendiumImageQualityPolicy.IsAutomaticLayoutAllowed(value, primaryImageEffectiveDpi)) return;
            if (!result.Contains(value)) result.Add(value);
        }

        Add(initiallyResolved);
        Add(CompendiumDossierLayout.Balanced);
        Add(CompendiumDossierLayout.Technical);
        Add(CompendiumDossierLayout.VisualHero);
        Add(CompendiumDossierLayout.MultiImageEditorial);
        if (result.Count == 0) result.Add(CompendiumDossierLayout.Technical);
        return result;
    }

    private static IReadOnlyList<float> CandidateImageHeights(
        CompendiumDossierLayout layout,
        int availablePhotoCount,
        int? primaryImageEffectiveDpi,
        bool protectPrintFidelity)
    {
        if (availablePhotoCount <= 0) return new[] { 0f };
        IReadOnlyList<float> values = layout switch
        {
            CompendiumDossierLayout.VisualHero => new[] { 330f, 315f, 285f, 255f, 230f, 205f, 185f },
            CompendiumDossierLayout.MultiImageEditorial => new[] { 285f, 275f, 260f, 245f, 220f, 200f, 185f },
            CompendiumDossierLayout.Technical => new[] { 185f, 175f, 160f, 145f, 125f, 105f, 90f, 82f },
            _ => new[] { 300f, 285f, 270f, 255f, 246f, 225f, 205f, 185f, 165f }
        };

        if (!protectPrintFidelity || primaryImageEffectiveDpi is not > 0) return values;
        var safeMaximum = CompendiumImageQualityPolicy.MaximumAutomaticImageHeight(
            layout,
            PreferredImageHeight(layout, availablePhotoCount),
            primaryImageEffectiveDpi);
        var filtered = values.Where(value => value <= safeMaximum + .1f).ToArray();
        return filtered;
    }

    private static IReadOnlyList<float> CandidateNarrativeScales(string narrative)
    {
        if (string.IsNullOrWhiteSpace(narrative)) return new[] { 1f };
        return narrative.Length switch
        {
            <= 1500 => new[] { CompendiumNarrativeTypographyPolicy.MaximumScale, 1.075f, 1.05f, 1.025f, 1f },
            <= 2200 => new[] { 1.075f, 1.05f, 1.025f, 1f },
            <= 3000 => new[] { 1.05f, 1.025f, 1f },
            <= 3900 => new[] { 1.025f, 1f },
            _ => new[] { 1f }
        };
    }

    private static float EstimateAdditionalNoteHeight(
        string additionalNote,
        float narrativeFontScale,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        if (string.IsNullOrWhiteSpace(additionalNote)) return 0f;
        var scale = CompendiumNarrativeTypographyPolicy.NormalizeScale(narrativeFontScale);
        // Phase 37.5 removes the redundant closing/top separator around the note heading.
        // The former 7-point top treatment is deliberately retained as editorial breathing reserve
        // in measurement so a purely decorative refinement does not cause the planner to enlarge
        // imagery or choose a different dossier layout.
        const float noteHeadingGeometryPoints = 23f;
        const float preservedEditorialBreathingPoints = 7f;
        return preservedEditorialBreathingPoints + measurementSession.MeasureAdditionalNote(
            additionalNote,
            FullNarrativeWidthPoints,
            scale,
            leadingReservePoints: noteHeadingGeometryPoints).HeightPoints;
    }

    private static float EstimateTitleBlockHeight(
        string? projectName,
        string? projectKicker,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        var fontSize = CompendiumLayoutMetrics.ResolveProjectTitleFontSize(projectName);
        var titleHeight = measurementSession.MeasureAtFontSize(
            projectName,
            FullNarrativeWidthPoints,
            fontSize,
            CompendiumLayoutMetrics.ProjectTitleLineHeightMultiplier,
            semiBold: true).HeightPoints;
        var kickerText = string.IsNullOrWhiteSpace(projectKicker) ? "Project dossier" : projectKicker.Trim();
        var kickerHeight = measurementSession.MeasureAtFontSize(
            kickerText.ToUpperInvariant(),
            FullNarrativeWidthPoints,
            CompendiumLayoutMetrics.ProjectKickerFontSize,
            CompendiumLayoutMetrics.ProjectKickerLineHeightMultiplier,
            semiBold: true,
            letterSpacingPoints: CompendiumLayoutMetrics.ProjectKickerLetterSpacingPoints).HeightPoints;
        return kickerHeight
               + CompendiumLayoutMetrics.ProjectHeadingRuleHeightPoints
               + (3f * CompendiumLayoutMetrics.ProjectColumnSpacingPoints)
               + titleHeight;
    }

    public static float ResolveContinuationBodyHeightPoints(string? projectName)
        => ResolveContinuationBodyHeightPoints(
            projectName,
            new CompendiumDossierTextMeasurementService.Session());

    private static float ResolveContinuationBodyHeightPoints(
        string? projectName,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        var titleHeight = measurementSession.MeasureAtFontSize(
            projectName,
            FullNarrativeWidthPoints,
            CompendiumLayoutMetrics.ContinuationTitleFontSize,
            CompendiumLayoutMetrics.ContinuationTitleLineHeightMultiplier,
            semiBold: true).HeightPoints;
        var fixedGeometry = titleHeight
                            + (3f * CompendiumLayoutMetrics.ContinuationColumnSpacingPoints)
                            + CompendiumLayoutMetrics.ContinuationLabelLineHeightPoints
                            + CompendiumLayoutMetrics.ContinuationHeadingRuleHeightPoints;
        return Math.Max(120f, CompendiumLayoutMetrics.SecondaryContentHeightPoints - fixedGeometry);
    }

    private static CompendiumProjectParticularsLayoutPolicy.Layout ResolveLegacyParticularsLayout(
        CompendiumProjectParticularsStyle style,
        int moduleCount)
    {
        if (moduleCount <= 0)
            return new CompendiumProjectParticularsLayoutPolicy.Layout(style, 0, 0, 0f, false);
        var columns = ResolveProgrammeColumns(moduleCount);
        var rows = (int)Math.Ceiling((double)moduleCount / Math.Max(1, columns));
        var height = EstimateProgrammeHeight(moduleCount, columns, style);
        return new CompendiumProjectParticularsLayoutPolicy.Layout(style, columns, rows, height, moduleCount == 1);
    }

    private static float EstimateProgrammeHeight(
        int moduleCount,
        int columns,
        CompendiumProjectParticularsStyle style = CompendiumProjectParticularsStyle.Panel)
    {
        if (moduleCount <= 0) return 0f;
        var rows = (int)Math.Ceiling((double)moduleCount / Math.Max(1, columns));
        if (CompendiumProjectParticularsLayoutPolicy.Normalize(style) == CompendiumProjectParticularsStyle.Minimal)
        {
            return 16f + rows * 24f + Math.Max(0, rows - 1) * 8f;
        }

        // Backward-compatible count-only estimate for legacy callers. Production authored dossiers
        // pass the real module set and therefore use the physical shared layout policy above.
        return moduleCount switch
        {
            1 => 52f,
            <= 3 => 57f,
            _ => 25f + rows * 32f
        };
    }

    private static float EstimateSpecificationHeight(
        IReadOnlyList<string> specifications,
        int columns,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        if (specifications.Count == 0) return 0f;
        columns = Math.Clamp(columns, 1, 3);
        var columnWidth = FullNarrativeWidthPoints / columns;
        var textWidth = Math.Max(60f, columnWidth - 23f); // bullet column + right breathing room
        // Phase 37.5 renders one heading rule instead of a separate top separator. The visible
        // heading geometry is lighter, while six points are intentionally retained as breathing
        // reserve so this visual polish does not trigger unrelated page recomposition.
        const float headingGeometryPoints = 15.5f;
        const float preservedEditorialBreathingPoints = 6f;
        var height = headingGeometryPoints + preservedEditorialBreathingPoints;
        foreach (var row in specifications.Chunk(columns))
        {
            var rowHeight = row.Max(item => measurementSession.MeasureAtFontSize(
                CleanText(item),
                textWidth,
                fontSizePoints: 8.75f,
                lineHeightMultiplier: 1.22f).HeightPoints);
            height += 6f + Math.Max(10.7f, rowHeight);
        }
        return height;
    }

    private static int EstimateNarrativeCapacityCharacters(float availableHeight, int charactersPerLine, float fontScale)
    {
        if (availableHeight <= 32f) return 760;
        var lineHeight = CompendiumNarrativeTypographyPolicy.BodyFontSizePoints * CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier * CompendiumNarrativeTypographyPolicy.NormalizeScale(fontScale);
        var lines = Math.Max(1, (int)Math.Floor((availableHeight - CompendiumNarrativeTypographyPolicy.NarrativeHeadingReservePoints) / lineHeight));
        return Math.Max(760, (int)Math.Floor(lines * charactersPerLine * .94d));
    }

    private static int EstimateBalancedFlowCapacity(float availableHeight, float imageHeight, float fontScale)
    {
        var sideLines = Math.Max(0, (int)Math.Floor((Math.Max(1f, imageHeight) - CompendiumNarrativeTypographyPolicy.NarrativeHeadingReservePoints) / (CompendiumNarrativeTypographyPolicy.BodyFontSizePoints * CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier * CompendiumNarrativeTypographyPolicy.NormalizeScale(fontScale))));
        var belowHeight = Math.Max(0f, availableHeight - imageHeight - 8f);
        var belowLines = Math.Max(0, (int)Math.Floor(belowHeight / (CompendiumNarrativeTypographyPolicy.BodyFontSizePoints * CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier * CompendiumNarrativeTypographyPolicy.NormalizeScale(fontScale))));
        return Math.Max(760, (int)Math.Floor(sideLines * 39f * .965f + belowLines * 86f * .94f));
    }


    private static string CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return MarkdownNoiseRegex.Replace(value.Replace("\r\n", "\n", StringComparison.Ordinal), string.Empty).Trim();
    }

    private static string Display(CompendiumDossierLayout layout)
        => layout switch
        {
            CompendiumDossierLayout.VisualHero => "Visual Hero",
            CompendiumDossierLayout.MultiImageEditorial => "Multi-image Editorial",
            CompendiumDossierLayout.Technical => "Technical",
            CompendiumDossierLayout.Balanced => "Balanced Dossier",
            _ => "Automatic"
        };

    private sealed record Candidate(
        CompendiumDossierLayout Layout,
        float RequestedImageHeight,
        float ImageHeight,
        float NarrativeFontScale,
        bool FitsAllContent,
        float TotalHeightPoints,
        int NarrativeCapacityCharacters)
    {
        public int CompositionScore { get; init; }
        public float SideRemainingHeightPoints { get; init; }
        public float SideOverflowHeightPoints { get; init; }
        public float SideBalanceRatio { get; init; } = 1f;
        public float NarrativeHeightCapacityPoints { get; init; }
        public bool IsEditoriallyValid { get; init; } = true;
        public string? EditorialWarning { get; init; }
        public float ResidualSpacePoints => Math.Max(0f, PhysicalContentHeightPoints - TotalHeightPoints);
    }
}
