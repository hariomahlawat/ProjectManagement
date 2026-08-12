namespace ProjectManagement.Services.Publications;

/// <summary>
/// Deterministic screen-first publication plan for Digital / Comfortable.
/// The policy is intentionally different from Print / Compact: it limits project density,
/// inserts dedicated institutional opening/closing pages when authoritative matter exists,
/// and retains a minimal back cover as a separate final page.
/// </summary>
public static class BrochureDigitalPublicationPolicy
{
    public const int AdditionalIntroductionMaximumWordsPerPage = 330;
    public const int InstitutionalOpeningMaximumWords = 430;
    public const int InstitutionalClosingMaximumWords = 420;

    public static BrochureDigitalPlan Plan(
        IReadOnlyList<BrochurePublicationProject> projects,
        BrochurePrintMatter? institutionalMatter,
        string? additionalIntroduction,
        bool includeBackCover)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var projectPages = BrochureLayoutPlanner.PlanDigitalComfortable(projects);
        var introductionPages = SplitAdditionalIntroduction(additionalIntroduction);
        var includeOpening = HasOpeningMatter(institutionalMatter);
        var includeClosing = HasClosingMatter(institutionalMatter);

        var pageNumber = 1; // cover
        var summaries = new List<BrochureDigitalPageSummary>
        {
            new(pageNumber, "cover", "Cover", 0, null)
        };

        int? openingPageNumber = null;
        if (includeOpening)
        {
            openingPageNumber = ++pageNumber;
            summaries.Add(new BrochureDigitalPageSummary(
                pageNumber,
                "institutional-opening",
                "About SDD",
                0,
                null));
        }

        var additionalIntroductionPageNumbers = new List<int>(introductionPages.Count);
        for (var index = 0; index < introductionPages.Count; index++)
        {
            additionalIntroductionPageNumbers.Add(++pageNumber);
            summaries.Add(new BrochureDigitalPageSummary(
                pageNumber,
                "additional-introduction",
                introductionPages.Count == 1 ? "Additional introduction" : $"Additional introduction {index + 1}",
                0,
                null));
        }

        var projectPageNumbers = new List<int>(projectPages.Count);
        for (var index = 0; index < projectPages.Count; index++)
        {
            var plan = projectPages[index];
            projectPageNumbers.Add(++pageNumber);
            summaries.Add(new BrochureDigitalPageSummary(
                pageNumber,
                "projects",
                ProjectPageLabel(plan),
                plan.Items.Count,
                plan.Layout.ToString()));
        }

        int? closingPageNumber = null;
        if (includeClosing)
        {
            closingPageNumber = ++pageNumber;
            summaries.Add(new BrochureDigitalPageSummary(
                pageNumber,
                "institutional-closing",
                "Future capability & engagement",
                0,
                null));
        }

        int? backCoverPageNumber = null;
        if (includeBackCover)
        {
            backCoverPageNumber = ++pageNumber;
            summaries.Add(new BrochureDigitalPageSummary(
                pageNumber,
                "back-cover",
                "Back cover",
                0,
                null));
        }

        return new BrochureDigitalPlan(
            projectPages,
            introductionPages,
            summaries,
            pageNumber,
            projectPages.Count,
            projectPages.Count(page => page.Layout == BrochurePageLayoutKind.SingleFeature),
            projectPages.Count(page => page.Layout == BrochurePageLayoutKind.TwoFeature),
            1 + (includeOpening ? 1 : 0) + (includeClosing ? 1 : 0) + introductionPages.Count + (includeBackCover ? 1 : 0),
            includeOpening,
            includeClosing,
            includeBackCover,
            openingPageNumber,
            additionalIntroductionPageNumbers,
            projectPageNumbers,
            closingPageNumber,
            backCoverPageNumber);
    }

    public static IReadOnlyList<BrochurePreflightIssue> ValidateInstitutionalMatter(BrochurePrintMatter? matter)
    {
        if (matter is null)
        {
            return Array.Empty<BrochurePreflightIssue>();
        }

        var issues = new List<BrochurePreflightIssue>();
        var openingWords = WordCount(matter.CentreStatement)
                           + WordCount(matter.OpeningNarrative)
                           + WordCount(matter.FutureNarrative);
        if (openingWords > InstitutionalOpeningMaximumWords)
        {
            issues.Add(new BrochurePreflightIssue(
                BrochurePreflightIssueCode.DigitalInstitutionalOpeningTooLong,
                PublicationIssueSeverity.Blocker,
                null,
                null,
                $"Digital About SDD content is {openingWords} words. Keep the combined Centre of Expertise, simulator-role and future-readiness content within {InstitutionalOpeningMaximumWords} words so the dedicated opening page remains comfortably readable."));
        }

        var closingWords = WordCount(matter.VisionaryHorizons)
                           + WordCount(matter.NewSimulatorsGuidance)
                           + WordCount(matter.ProcurementGuidance)
                           + WordCount(matter.DevelopingAgency)
                           + WordCount(matter.ManufacturingAgency);
        if (closingWords > InstitutionalClosingMaximumWords)
        {
            issues.Add(new BrochurePreflightIssue(
                BrochurePreflightIssueCode.DigitalInstitutionalClosingTooLong,
                PublicationIssueSeverity.Blocker,
                null,
                null,
                $"Digital closing content is {closingWords} words. Keep the combined Visionary Horizons, engagement, procurement and contact content within {InstitutionalClosingMaximumWords} words so the closing page remains comfortably readable."));
        }

        return issues;
    }

    public static bool HasOpeningMatter(BrochurePrintMatter? matter)
        => matter is not null
           && HasAny(
               matter.CentreStatement,
               matter.OpeningNarrative,
               matter.FutureNarrative);

    public static bool HasClosingMatter(BrochurePrintMatter? matter)
        => matter is not null
           && HasAny(
               matter.VisionaryHorizons,
               matter.NewSimulatorsGuidance,
               matter.ProcurementGuidance,
               matter.DevelopingAgency,
               matter.ManufacturingAgency);

    public static IReadOnlyList<string> SplitAdditionalIntroduction(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        const int maximumWords = AdditionalIntroductionMaximumWordsPerPage;
        if (BrochureLayoutPlanner.CountWords(text) <= maximumWords)
        {
            return new[] { text.Trim() };
        }

        var paragraphs = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pieces = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            if (BrochureLayoutPlanner.CountWords(paragraph) <= maximumWords)
            {
                pieces.Add(paragraph.Trim());
                continue;
            }

            var words = paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (var offset = 0; offset < words.Length; offset += maximumWords)
            {
                pieces.Add(string.Join(" ", words.Skip(offset).Take(maximumWords)));
            }
        }

        var pages = new List<string>();
        var current = new List<string>();
        var currentWords = 0;
        foreach (var piece in pieces)
        {
            var pieceWords = BrochureLayoutPlanner.CountWords(piece);
            if (current.Count > 0 && currentWords + pieceWords > maximumWords)
            {
                pages.Add(string.Join("\n\n", current));
                current.Clear();
                currentWords = 0;
            }

            current.Add(piece);
            currentWords += pieceWords;
        }

        if (current.Count > 0)
        {
            pages.Add(string.Join("\n\n", current));
        }

        return pages;
    }

    private static string ProjectPageLabel(BrochurePagePlan page)
        => page.Layout switch
        {
            BrochurePageLayoutKind.SingleFeature => "1 project · feature",
            BrochurePageLayoutKind.TwoFeature => "2 projects · editorial split",
            _ => $"{page.Items.Count} projects"
        };

    private static int WordCount(string? value) => BrochureLayoutPlanner.CountWords(value);

    private static bool HasAny(params string?[] values)
        => values.Any(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record BrochureDigitalPageSummary(
    int PageNumber,
    string Kind,
    string Label,
    int ProjectCount,
    string? Layout);

public sealed record BrochureDigitalPlan(
    IReadOnlyList<BrochurePagePlan> ProjectPages,
    IReadOnlyList<string> AdditionalIntroductionPages,
    IReadOnlyList<BrochureDigitalPageSummary> PagePlan,
    int EstimatedTotalPageCount,
    int ProjectPageCount,
    int SingleFeaturePageCount,
    int TwoFeaturePageCount,
    int EditorialPageCount,
    bool IncludesInstitutionalOpening,
    bool IncludesInstitutionalClosing,
    bool IncludesBackCover,
    int? InstitutionalOpeningPageNumber,
    IReadOnlyList<int> AdditionalIntroductionPageNumbers,
    IReadOnlyList<int> ProjectPageNumbers,
    int? InstitutionalClosingPageNumber,
    int? BackCoverPageNumber);
