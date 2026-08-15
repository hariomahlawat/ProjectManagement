using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Shared A4 dossier fit planner used by browser review and the PDF page planner.
/// It estimates rendered height rather than applying a fixed character budget, and it allows
/// Automatic layout to yield photography before creating a continuation page.
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
        string? projectName)
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
        var layouts = BuildCandidateLayouts(initiallyResolved, explicitLayout, availablePhotoCount);

        foreach (var layout in layouts)
        {
            foreach (var imageHeight in CandidateImageHeights(layout, availablePhotoCount))
            {
                var candidate = Evaluate(
                    layout,
                    imageHeight,
                    availablePhotoCount,
                    cleanNarrative,
                    specifications,
                    programmeModuleCount,
                    projectName,
                    specificationColumns,
                    programmeColumns);

                if (!candidate.FitsAllContent)
                {
                    continue;
                }

                var changedLayout = layout != initiallyResolved;
                var reducedPhotography = imageHeight + .1f < PreferredImageHeight(layout, availablePhotoCount);
                var reason = changedLayout
                    ? $"Automatic fit changed {Display(initiallyResolved)} to {Display(layout)} to keep the dossier on one page"
                    : reducedPhotography
                        ? $"{Display(layout)} retained; photography was reduced to preserve readable one-page content"
                        : $"{Display(layout)} fits the current content on one dossier page";

                return new Decision(
                    layout,
                    imageHeight,
                    Math.Max(800, cleanNarrative.Length + 220),
                    specifications.Length,
                    specificationColumns,
                    programmeColumns,
                    1,
                    false,
                    reason,
                    $"1 dossier page · {Display(layout)}");
            }
        }

        // No candidate can contain everything on one page. Preserve the resolved/explicit layout,
        // use its most compact image treatment, keep the narrative together where possible, then
        // move technical specifications before splitting narrative into a tiny orphan page.
        var fallbackLayout = layouts[0];
        var compactHeight = CandidateImageHeights(fallbackLayout, availablePhotoCount).Last();
        var withoutSpecs = Evaluate(
            fallbackLayout,
            compactHeight,
            availablePhotoCount,
            cleanNarrative,
            Array.Empty<string>(),
            programmeModuleCount,
            projectName,
            1,
            programmeColumns);

        const int firstSpecificationCount = 0;
        var firstNarrativeBudget = withoutSpecs.FitsAllContent
            ? Math.Max(800, cleanNarrative.Length + 220)
            : Math.Max(760, withoutSpecs.NarrativeCapacityCharacters);

        // Orphan suppression: if only a small narrative tail would spill and the geometric
        // estimate is close to the boundary, retain it on page one rather than generating an
        // almost-empty continuation. The 24pt tolerance is smaller than the image-height step
        // already yielded above and remains below the standard page safety reserve.
        var overflow = Math.Max(0, cleanNarrative.Length - firstNarrativeBudget);
        if (overflow > 0 && overflow <= 430 && withoutSpecs.TotalHeightPoints <= UsableContentHeightPoints + 24f)
        {
            firstNarrativeBudget = cleanNarrative.Length + 220;
            overflow = 0;
        }

        var narrativeContinuationPages = overflow <= 0
            ? 0
            : Math.Max(1, (int)Math.Ceiling((double)overflow / ContinuationNarrativeBudget));
        var specificationContinuationPages = specifications.Length == 0 || firstSpecificationCount == specifications.Length
            ? 0
            : EstimateSpecificationContinuationPages(specifications);
        if (narrativeContinuationPages > 0 && specificationContinuationPages > 0)
        {
            var lastNarrativeContinuationLength = overflow % ContinuationNarrativeBudget;
            if (lastNarrativeContinuationLength == 0) lastNarrativeContinuationLength = ContinuationNarrativeBudget;
            var specificationPressure = specifications.Sum(item => CleanText(item).Length);
            if (lastNarrativeContinuationLength <= 1800 && specificationPressure <= 1200)
            {
                specificationContinuationPages--;
            }
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

        var total = items.Sum(item => item.Length);
        var longest = items.Max(item => item.Length);
        if (items.Length >= 4 && total <= 390 && longest <= 125)
        {
            return 3;
        }
        if (items.Length >= 3 && total <= 920 && longest <= 285)
        {
            return 2;
        }
        if (items.Length == 2 && total <= 340 && longest <= 190)
        {
            return 2;
        }
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

    public static float ResolvePrimaryFrameWidthPoints(
        CompendiumDossierLayout layout,
        int availablePhotoCount)
    {
        if (availablePhotoCount <= 0) return 519f;
        if (layout != CompendiumDossierLayout.Balanced) return 519f;
        const float gap = 13f;
        var usableWidth = 519f - gap;
        return usableWidth * 1.12f / 2f;
    }

    public static float PreferredImageHeight(
        CompendiumDossierLayout layout,
        int availablePhotoCount)
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

    private static Candidate Evaluate(
        CompendiumDossierLayout layout,
        float imageHeight,
        int availablePhotoCount,
        string narrative,
        IReadOnlyList<string> specifications,
        int programmeModuleCount,
        string? projectName,
        int specificationColumns,
        int programmeColumns)
    {
        var titleHeight = EstimateTitleBlockHeight(projectName);
        var programmeHeight = EstimateProgrammeHeight(programmeModuleCount, programmeColumns);
        var specificationHeight = EstimateSpecificationHeight(specifications, specificationColumns);
        var hasPhoto = availablePhotoCount > 0 && imageHeight > 0f;
        var narrativeWidthCharacters = layout == CompendiumDossierLayout.Balanced && hasPhoto ? 42 : 86;
        var narrativeHeight = EstimateNarrativeHeight(narrative, narrativeWidthCharacters);
        var fixedGaps = 31f;

        float mainHeight;
        if (!hasPhoto)
        {
            mainHeight = narrativeHeight;
        }
        else if (layout == CompendiumDossierLayout.Balanced)
        {
            mainHeight = Math.Max(imageHeight, narrativeHeight);
        }
        else
        {
            mainHeight = imageHeight + 9f + narrativeHeight;
        }

        var totalHeight = titleHeight + mainHeight + programmeHeight + specificationHeight + fixedGaps;
        var fits = totalHeight <= UsableContentHeightPoints;

        var availableNarrativeHeight = layout == CompendiumDossierLayout.Balanced && hasPhoto
            ? UsableContentHeightPoints - titleHeight - programmeHeight - specificationHeight - fixedGaps
            : UsableContentHeightPoints - titleHeight - programmeHeight - specificationHeight - fixedGaps - (hasPhoto ? imageHeight + 9f : 0f);
        var capacity = EstimateNarrativeCapacityCharacters(availableNarrativeHeight, narrativeWidthCharacters);

        return new Candidate(layout, imageHeight, fits, totalHeight, capacity);
    }

    private static IReadOnlyList<CompendiumDossierLayout> BuildCandidateLayouts(
        CompendiumDossierLayout initiallyResolved,
        bool explicitLayout,
        int availablePhotoCount)
    {
        if (explicitLayout)
        {
            return new[] { initiallyResolved };
        }

        var result = new List<CompendiumDossierLayout> { initiallyResolved };
        void Add(CompendiumDossierLayout value)
        {
            if (value == CompendiumDossierLayout.MultiImageEditorial && availablePhotoCount < 2) return;
            if (!result.Contains(value)) result.Add(value);
        }

        Add(CompendiumDossierLayout.Balanced);
        Add(CompendiumDossierLayout.Technical);
        Add(CompendiumDossierLayout.VisualHero);
        Add(CompendiumDossierLayout.MultiImageEditorial);
        return result;
    }

    private static IReadOnlyList<float> CandidateImageHeights(
        CompendiumDossierLayout layout,
        int availablePhotoCount)
    {
        if (availablePhotoCount <= 0) return new[] { 0f };
        return layout switch
        {
            CompendiumDossierLayout.VisualHero => new[] { 255f, 230f, 205f, 185f },
            CompendiumDossierLayout.MultiImageEditorial => new[] { 245f, 220f, 200f, 185f },
            CompendiumDossierLayout.Technical => new[] { 145f, 125f, 105f },
            _ => new[] { 246f, 225f, 205f, 185f }
        };
    }

    private static float EstimateTitleBlockHeight(string? projectName)
    {
        var length = projectName?.Trim().Length ?? 0;
        var extraLines = length switch
        {
            > 105 => 2,
            > 72 => 1,
            _ => 0
        };
        return 62f + (extraLines * 17f);
    }

    private static float EstimateProgrammeHeight(int moduleCount, int columns)
    {
        if (moduleCount <= 0) return 0f;
        var rows = (int)Math.Ceiling((double)moduleCount / Math.Max(1, columns));
        return 31f + rows * 34f;
    }

    private static float EstimateSpecificationHeight(IReadOnlyList<string> specifications, int columns)
    {
        if (specifications.Count == 0) return 0f;
        var charactersPerLine = columns switch { >= 3 => 25, 2 => 39, _ => 84 };
        var height = 20f;
        foreach (var row in specifications.Chunk(Math.Max(1, columns)))
        {
            var rowLines = row.Max(item => Math.Max(1, (int)Math.Ceiling((double)CleanText(item).Length / charactersPerLine)));
            height += 4f + rowLines * 10.1f;
        }
        return height;
    }

    private static float EstimateNarrativeHeight(string narrative, int charactersPerLine)
    {
        if (string.IsNullOrWhiteSpace(narrative)) return 31f;
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
        return 19f + lines * StandardLineHeightPoints + Math.Max(0, paragraphs.Length - 1) * 5f;
    }

    private static int EstimateNarrativeCapacityCharacters(float availableHeight, int charactersPerLine)
    {
        if (availableHeight <= 32f) return 760;
        var lines = Math.Max(1, (int)Math.Floor((availableHeight - 19f) / StandardLineHeightPoints));
        return Math.Max(760, (int)Math.Floor(lines * charactersPerLine * .94d));
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
        bool FitsAllContent,
        float TotalHeightPoints,
        int NarrativeCapacityCharacters);
}
