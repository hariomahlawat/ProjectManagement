using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Shared A4 dossier composition planner used by browser review and PDF planning. Phase 37 ranks
/// one-page candidates using page utilisation, image fidelity, Balanced side-flow utilisation and
/// narrative readability. Automatic layouts are print-protective; explicit publisher choices are
/// retained whenever geometrically possible.
/// </summary>
public static class CompendiumDossierPaginationPlanner
{
    private const float UsableContentHeightPoints = 700f;
    private const float StandardLineHeightPoints = 12.5f;
    private const int ContinuationNarrativeBudget = 3300;

    private static readonly Regex MarkdownNoiseRegex = new(
        @"[*_`#>]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Decision(
        CompendiumDossierLayout Layout,
        float PrimaryImageHeightPoints,
        float NarrativeFontScale,
        int FirstPageNarrativeBudget,
        int FirstPageSpecificationCount,
        int SpecificationColumns,
        int ProgrammeColumns,
        int EstimatedPageCount,
        bool UsesContinuation,
        string Reason,
        string PaginationNote);

    public static Decision Resolve(
        CompendiumDossierLayout requested,
        CompendiumDossierLayout initiallyResolved,
        int availablePhotoCount,
        string? narrative,
        IReadOnlyList<string>? technicalSpecifications,
        int programmeModuleCount,
        string? projectName,
        int? primaryImageEffectiveDpi = null,
        CompendiumBalancedTextFlowMode balancedTextFlowMode = CompendiumBalancedTextFlowMode.FlowBelowImage)
    {
        availablePhotoCount = Math.Clamp(availablePhotoCount, 0, 3);
        var cleanNarrative = CleanText(narrative);
        var specifications = (technicalSpecifications ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(6)
            .ToArray();
        programmeModuleCount = Math.Clamp(programmeModuleCount, 0, 4);

        var specificationColumns = ResolveTechnicalSpecificationColumns(specifications);
        var programmeColumns = ResolveProgrammeColumns(programmeModuleCount);
        var explicitLayout = requested != CompendiumDossierLayout.Automatic;

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
            foreach (var imageHeight in CandidateImageHeights(layout, availablePhotoCount, primaryImageEffectiveDpi, !explicitLayout))
            {
                foreach (var narrativeScale in CandidateNarrativeScales(cleanNarrative))
                {
                    var evaluated = Evaluate(
                        layout,
                        imageHeight,
                        narrativeScale,
                        availablePhotoCount,
                        cleanNarrative,
                        specifications,
                        programmeModuleCount,
                        projectName,
                        specificationColumns,
                        programmeColumns,
                        balancedTextFlowMode);

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

        if (validCandidates.Count > 0)
        {
            var best = validCandidates
                .OrderByDescending(candidate => candidate.CompositionScore)
                .ThenBy(candidate => Math.Abs(candidate.ResidualSpacePoints - ResolveIdealResidualSpace(specifications.Length, programmeModuleCount)))
                .ThenBy(candidate => candidate.SideRemainingHeightPoints)
                .ThenByDescending(candidate => candidate.ImageHeight)
                .First();

            var changedLayout = best.Layout != initiallyResolved;
            var preferredHeight = PreferredImageHeight(best.Layout, availablePhotoCount);
            var expandedPhotography = best.ImageHeight > preferredHeight + .1f;
            var reducedPhotography = best.ImageHeight + .1f < preferredHeight;
            var improvedTypography = best.NarrativeFontScale > 1.001f;
            var qualityProtected = !explicitLayout
                                   && primaryImageEffectiveDpi is > 0 and < CompendiumImageQualityPolicy.AcceptablePrintDpi;

            var reason = qualityProtected && changedLayout
                ? $"Automatic composition selected {Display(best.Layout)} to protect print fidelity at approximately {primaryImageEffectiveDpi} DPI"
                : changedLayout
                    ? $"Automatic composition selected {Display(best.Layout)} over {Display(initiallyResolved)} after comparing one-page candidates for readability, photography and whitespace balance"
                    : expandedPhotography && improvedTypography
                        ? $"{Display(best.Layout)} retained; available page space was invested in larger photography and more readable narrative typography"
                        : expandedPhotography
                            ? $"{Display(best.Layout)} retained; available page space was invested in larger photography"
                            : reducedPhotography && qualityProtected
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
                specifications.Length,
                specificationColumns,
                programmeColumns,
                1,
                false,
                explicitLayout && !changedLayout
                    ? $"Publisher-selected {Display(best.Layout)} retained; {reason.ToLowerInvariant()}"
                    : reason,
                $"1 dossier page · {Display(best.Layout)} · optimised");
        }

        var fallbackLayout = layouts[0];
        var compactHeight = CandidateImageHeights(
            fallbackLayout,
            availablePhotoCount,
            primaryImageEffectiveDpi,
            !explicitLayout).Last();
        var withoutSpecs = Evaluate(
            fallbackLayout,
            compactHeight,
            1f,
            availablePhotoCount,
            cleanNarrative,
            Array.Empty<string>(),
            programmeModuleCount,
            projectName,
            1,
            programmeColumns,
            balancedTextFlowMode);

        const int firstSpecificationCount = 0;
        var firstNarrativeBudget = withoutSpecs.FitsAllContent
            ? Math.Max(800, cleanNarrative.Length + 220)
            : Math.Max(760, withoutSpecs.NarrativeCapacityCharacters);

        var overflow = Math.Max(0, cleanNarrative.Length - firstNarrativeBudget);
        if (overflow > 0 && overflow <= 430 && withoutSpecs.TotalHeightPoints <= UsableContentHeightPoints + 24f)
        {
            firstNarrativeBudget = cleanNarrative.Length + 220;
            overflow = 0;
        }

        var narrativeContinuationPages = overflow <= 0
            ? 0
            : Math.Max(1, (int)Math.Ceiling((double)overflow / ContinuationNarrativeBudget));
        var specificationContinuationPages = specifications.Length == 0
            ? 0
            : EstimateSpecificationContinuationPages(specifications);
        if (narrativeContinuationPages > 0 && specificationContinuationPages > 0)
        {
            var lastNarrativeContinuationLength = overflow % ContinuationNarrativeBudget;
            if (lastNarrativeContinuationLength == 0) lastNarrativeContinuationLength = ContinuationNarrativeBudget;
            var specificationPressure = specifications.Sum(item => CleanText(item).Length);
            if (lastNarrativeContinuationLength <= 1800 && specificationPressure <= 1200)
                specificationContinuationPages--;
        }

        var estimatedPages = 1 + narrativeContinuationPages + specificationContinuationPages;
        var continuationReason = narrativeContinuationPages > 0 && specifications.Length > 0
            ? "narrative and technical reference continue"
            : narrativeContinuationPages > 0
                ? "project brief continues"
                : "technical reference continues";

        return new Decision(
            fallbackLayout,
            compactHeight,
            1f,
            firstNarrativeBudget,
            firstSpecificationCount,
            specificationColumns,
            programmeColumns,
            estimatedPages,
            true,
            explicitLayout
                ? $"Publisher-selected {Display(fallbackLayout)} requires controlled continuation at the current content volume"
                : $"Content exceeds the safe one-page envelope after photography is reduced; {continuationReason}",
            $"{estimatedPages} dossier pages · {continuationReason}");
    }

    public static int ResolveTechnicalSpecificationColumns(IReadOnlyList<string>? specifications)
    {
        var items = (specifications ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(6)
            .ToArray();
        if (items.Length == 0) return 1;

        static int EstimatedLines(string value, int charactersPerLine)
            => Math.Max(1, (int)Math.Ceiling((double)CleanText(value).Length / charactersPerLine));

        if (items.Length >= 3 && items.All(item => EstimatedLines(item, 24) <= 2)) return 3;
        if (items.Length >= 2 && items.All(item => EstimatedLines(item, 37) <= 4)) return 2;
        return 1;
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
        if (availablePhotoCount <= 0) return 519f;
        if (layout != CompendiumDossierLayout.Balanced) return 519f;
        const float gap = 13f;
        var usableWidth = 519f - gap;
        return usableWidth * 1.12f / 2f;
    }

    public static float ResolveBalancedSideWidthPoints(int availablePhotoCount)
    {
        if (availablePhotoCount <= 0) return 519f;
        const float gap = 13f;
        var usableWidth = 519f - gap;
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
        int specificationColumns,
        int programmeColumns,
        CompendiumBalancedTextFlowMode balancedTextFlowMode)
    {
        narrativeFontScale = Math.Clamp(narrativeFontScale, 1f, 1.10f);
        var titleHeight = EstimateTitleBlockHeight(projectName);
        var programmeHeight = EstimateProgrammeHeight(programmeModuleCount, programmeColumns);
        var specificationHeight = EstimateSpecificationHeight(specifications, specificationColumns);
        var hasPhoto = availablePhotoCount > 0 && imageHeight > 0f;
        var fixedGaps = 31f;
        float sideRemaining = 0f;

        float mainHeight;
        int capacity;
        if (!hasPhoto)
        {
            mainHeight = EstimateNarrativeHeight(narrative, 86, narrativeFontScale, includeHeading: true);
            var available = UsableContentHeightPoints - titleHeight - programmeHeight - specificationHeight - fixedGaps;
            capacity = EstimateNarrativeCapacityCharacters(available, 86, narrativeFontScale);
        }
        else if (layout == CompendiumDossierLayout.Balanced
                 && balancedTextFlowMode == CompendiumBalancedTextFlowMode.FlowBelowImage)
        {
            var side = CompendiumDossierNarrativeFlowPlanner.AssessSideFlow(narrative, imageHeight, narrativeFontScale);
            sideRemaining = side.RemainingHeightPoints;
            var belowHeight = string.IsNullOrWhiteSpace(side.BelowSegment)
                ? 0f
                : EstimateNarrativeHeight(side.BelowSegment, 86, narrativeFontScale, includeHeading: false);
            mainHeight = imageHeight + (belowHeight > 0f ? 8f + belowHeight : 0f);
            var available = UsableContentHeightPoints - titleHeight - programmeHeight - specificationHeight - fixedGaps;
            capacity = EstimateBalancedFlowCapacity(available, imageHeight, narrativeFontScale);
        }
        else if (layout == CompendiumDossierLayout.Balanced)
        {
            var narrativeHeight = EstimateNarrativeHeight(narrative, 42, narrativeFontScale, includeHeading: true);
            mainHeight = Math.Max(imageHeight, narrativeHeight);
            var available = UsableContentHeightPoints - titleHeight - programmeHeight - specificationHeight - fixedGaps;
            capacity = EstimateNarrativeCapacityCharacters(available, 42, narrativeFontScale);
        }
        else
        {
            var narrativeHeight = EstimateNarrativeHeight(narrative, 86, narrativeFontScale, includeHeading: true);
            mainHeight = imageHeight + 9f + narrativeHeight;
            var available = UsableContentHeightPoints - titleHeight - programmeHeight - specificationHeight - fixedGaps - imageHeight - 9f;
            capacity = EstimateNarrativeCapacityCharacters(available, 86, narrativeFontScale);
        }

        var totalHeight = titleHeight + mainHeight + programmeHeight + specificationHeight + fixedGaps;
        return new Candidate(
            layout,
            imageHeight,
            narrativeFontScale,
            totalHeight <= UsableContentHeightPoints,
            totalHeight,
            capacity)
        {
            SideRemainingHeightPoints = sideRemaining
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
        bool protectAutomatic)
    {
        if (availablePhotoCount <= 0) return new[] { 0f };
        IReadOnlyList<float> values = layout switch
        {
            CompendiumDossierLayout.VisualHero => new[] { 330f, 315f, 285f, 255f, 230f, 205f, 185f, 160f, 135f, 110f },
            CompendiumDossierLayout.MultiImageEditorial => new[] { 285f, 275f, 260f, 245f, 220f, 200f, 185f, 160f, 135f, 110f },
            CompendiumDossierLayout.Technical => new[] { 185f, 175f, 160f, 145f, 125f, 105f, 90f, 82f },
            _ => new[] { 300f, 285f, 270f, 255f, 246f, 225f, 205f, 185f, 165f, 145f, 125f, 105f, 96f }
        };

        if (!protectAutomatic || primaryImageEffectiveDpi is not > 0) return values;
        var safeMaximum = CompendiumImageQualityPolicy.MaximumAutomaticImageHeight(
            layout,
            PreferredImageHeight(layout, availablePhotoCount),
            primaryImageEffectiveDpi);
        var filtered = values.Where(value => value <= safeMaximum + .1f).ToArray();
        return filtered.Length > 0 ? filtered : new[] { values[^1] };
    }

    private static IReadOnlyList<float> CandidateNarrativeScales(string narrative)
    {
        if (string.IsNullOrWhiteSpace(narrative)) return new[] { 1f };
        return narrative.Length switch
        {
            <= 1500 => new[] { 1.10f, 1.08f, 1.05f, 1.025f, 1f },
            <= 2200 => new[] { 1.08f, 1.05f, 1.025f, 1f },
            <= 3000 => new[] { 1.05f, 1.025f, 1f },
            <= 3900 => new[] { 1.025f, 1f },
            _ => new[] { 1f }
        };
    }

    private static float EstimateTitleBlockHeight(string? projectName)
    {
        var length = projectName?.Trim().Length ?? 0;
        var extraLines = length switch { > 105 => 2, > 72 => 1, _ => 0 };
        return 62f + extraLines * 17f;
    }

    private static float EstimateProgrammeHeight(int moduleCount, int columns)
    {
        if (moduleCount <= 0) return 0f;
        var rows = (int)Math.Ceiling((double)moduleCount / Math.Max(1, columns));
        // Responsive particulars: one fact is a compact strip; larger sets earn additional rows.
        return moduleCount switch
        {
            1 => 52f,
            <= 3 => 57f,
            _ => 25f + rows * 32f
        };
    }

    private static float EstimateSpecificationHeight(IReadOnlyList<string> specifications, int columns)
    {
        if (specifications.Count == 0) return 0f;
        var charactersPerLine = columns switch { >= 3 => 24, 2 => 37, _ => 80 };
        var height = 22f;
        foreach (var row in specifications.Chunk(Math.Max(1, columns)))
        {
            var rowLines = row.Max(item => Math.Max(1, (int)Math.Ceiling((double)CleanText(item).Length / charactersPerLine)));
            height += 4.5f + rowLines * 10.8f;
        }
        return height;
    }

    private static float EstimateNarrativeHeight(
        string narrative,
        int charactersPerLine,
        float fontScale,
        bool includeHeading)
    {
        if (string.IsNullOrWhiteSpace(narrative)) return includeHeading ? 31f : 0f;
        var paragraphs = Regex.Split(narrative.Trim(), @"\n\s*\n")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        if (paragraphs.Length == 0) paragraphs = new[] { narrative };

        var lines = 0;
        foreach (var paragraph in paragraphs)
        {
            var clean = CleanText(paragraph);
            lines += Math.Max(1, (int)Math.Ceiling((double)Math.Max(1, clean.Length) / charactersPerLine));
        }
        return (includeHeading ? 19f : 0f)
               + lines * StandardLineHeightPoints * fontScale
               + Math.Max(0, paragraphs.Length - 1) * 5f;
    }

    private static int EstimateNarrativeCapacityCharacters(float availableHeight, int charactersPerLine, float fontScale)
    {
        if (availableHeight <= 32f) return 760;
        var lineHeight = StandardLineHeightPoints * Math.Max(1f, fontScale);
        var lines = Math.Max(1, (int)Math.Floor((availableHeight - 19f) / lineHeight));
        return Math.Max(760, (int)Math.Floor(lines * charactersPerLine * .94d));
    }

    private static int EstimateBalancedFlowCapacity(float availableHeight, float imageHeight, float fontScale)
    {
        var sideLines = Math.Max(0, (int)Math.Floor((Math.Max(120f, imageHeight) - 29f) / (StandardLineHeightPoints * fontScale)));
        var belowHeight = Math.Max(0f, availableHeight - imageHeight - 8f);
        var belowLines = Math.Max(0, (int)Math.Floor(belowHeight / (StandardLineHeightPoints * fontScale)));
        return Math.Max(760, (int)Math.Floor(sideLines * 39f * .965f + belowLines * 86f * .94f));
    }

    private static int EstimateSpecificationContinuationPages(IReadOnlyList<string> specifications)
    {
        if (specifications.Count == 0) return 0;
        var pressure = specifications.Sum(item => CleanText(item).Length + 70);
        return Math.Max(1, (int)Math.Ceiling(pressure / 2600d));
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
        float ImageHeight,
        float NarrativeFontScale,
        bool FitsAllContent,
        float TotalHeightPoints,
        int NarrativeCapacityCharacters)
    {
        public int CompositionScore { get; init; }
        public float SideRemainingHeightPoints { get; init; }
        public float ResidualSpacePoints => Math.Max(0f, UsableContentHeightPoints - TotalHeightPoints);
    }
}
