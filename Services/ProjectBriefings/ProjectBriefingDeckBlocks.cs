using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

// A deck is an ordered list of blocks (docs/ux/briefing-decks.md, "Plan: Blocks, Project Officer
// decks and focus view"). Each block draws one kind of slide, or a run of them: the project slides
// block draws every project's slides. The closing slide always ends the deck and is not a block.
// Values are stored names in a later PR: append new ones, never renumber.
public enum ProjectBriefingDeckBlockType
{
    Cover = 1,
    InstitutionalProfile = 2,
    RoleAndCharter = 3,
    PortfolioSummary = 4,
    StageSummary = 5,
    ProjectCategorySummary = 6,
    TechnicalCategorySummary = 7,
    StatusTable = 8,
    ProjectSlides = 9,
    FfcGlobalFootprint = 10
}

// Where a block sits. Blocks move only within their zone, so a deck always opens, then gives its
// overview, then its projects, then closes.
public enum ProjectBriefingDeckZone
{
    Opening = 1,
    Overview = 2,
    Projects = 3,
    Closing = 4
}

public sealed record ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType Type, bool Enabled = true);

public static class ProjectBriefingDeckBlocks
{
    public static ProjectBriefingDeckZone ZoneOf(ProjectBriefingDeckBlockType type) => type switch
    {
        ProjectBriefingDeckBlockType.Cover
            or ProjectBriefingDeckBlockType.InstitutionalProfile
            or ProjectBriefingDeckBlockType.RoleAndCharter => ProjectBriefingDeckZone.Opening,
        ProjectBriefingDeckBlockType.PortfolioSummary
            or ProjectBriefingDeckBlockType.StageSummary
            or ProjectBriefingDeckBlockType.ProjectCategorySummary
            or ProjectBriefingDeckBlockType.TechnicalCategorySummary
            or ProjectBriefingDeckBlockType.StatusTable => ProjectBriefingDeckZone.Overview,
        ProjectBriefingDeckBlockType.ProjectSlides => ProjectBriefingDeckZone.Projects,
        ProjectBriefingDeckBlockType.FfcGlobalFootprint => ProjectBriefingDeckZone.Closing,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown deck block.")
    };

    public static ProjectBriefingDeckBlockType ForAdditionalSlide(ProjectBriefingAdditionalSlideType type) => type switch
    {
        ProjectBriefingAdditionalSlideType.InstitutionalProfile => ProjectBriefingDeckBlockType.InstitutionalProfile,
        ProjectBriefingAdditionalSlideType.RoleAndCharter => ProjectBriefingDeckBlockType.RoleAndCharter,
        ProjectBriefingAdditionalSlideType.FfcGlobalFootprint => ProjectBriefingDeckBlockType.FfcGlobalFootprint,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown additional slide.")
    };

    // The blocks a deck's present settings give, in the order the composer has always drawn them:
    // cover, the after-cover institution slides in their own order, portfolio summary; for the
    // standard layout the stage, project-category and technical-category summaries, the status
    // table (table or both) and the project slides (project slides or both); for update sheets the
    // sheets; then the before-closing institution slides. A deck saved before blocks existed is
    // read through this, so it composes exactly as it did (ProjectBriefingBlockEquivalenceTests).
    public static IReadOnlyList<ProjectBriefingDeckBlock> FromSettings(
        ProjectBriefingLayout layout,
        ProjectBriefingPresentationMode presentationMode,
        bool includeCoverSlide,
        bool includePortfolioSummarySlide,
        bool includeStageSummary,
        bool includeProjectCategorySummary,
        bool includeTechnicalCategorySummary,
        IReadOnlyList<ProjectBriefingAdditionalSlideType> additionalSlideOrder)
    {
        ArgumentNullException.ThrowIfNull(additionalSlideOrder);
        var blocks = new List<ProjectBriefingDeckBlock>();
        if (includeCoverSlide)
        {
            blocks.Add(new(ProjectBriefingDeckBlockType.Cover));
        }

        blocks.AddRange(AdditionalBlocks(additionalSlideOrder, ProjectBriefingAdditionalSlidePlacement.AfterCover));

        if (includePortfolioSummarySlide)
        {
            blocks.Add(new(ProjectBriefingDeckBlockType.PortfolioSummary));
        }

        if (layout == ProjectBriefingLayout.ProjectUpdateSheet)
        {
            blocks.Add(new(ProjectBriefingDeckBlockType.ProjectSlides));
        }
        else
        {
            if (includeStageSummary)
            {
                blocks.Add(new(ProjectBriefingDeckBlockType.StageSummary));
            }

            if (includeProjectCategorySummary)
            {
                blocks.Add(new(ProjectBriefingDeckBlockType.ProjectCategorySummary));
            }

            if (includeTechnicalCategorySummary)
            {
                blocks.Add(new(ProjectBriefingDeckBlockType.TechnicalCategorySummary));
            }

            if (presentationMode is ProjectBriefingPresentationMode.ExecutiveTable or ProjectBriefingPresentationMode.Combined)
            {
                blocks.Add(new(ProjectBriefingDeckBlockType.StatusTable));
            }

            if (presentationMode is ProjectBriefingPresentationMode.DetailedProjects or ProjectBriefingPresentationMode.Combined)
            {
                blocks.Add(new(ProjectBriefingDeckBlockType.ProjectSlides));
            }
        }

        blocks.AddRange(AdditionalBlocks(additionalSlideOrder, ProjectBriefingAdditionalSlidePlacement.BeforeClosing));
        return blocks;
    }

    // Overview blocks only, each once, in the given order: what a deck stores as its overview order.
    public static IReadOnlyList<ProjectBriefingDeckBlockType> NormalizeOverviewOrder(
        IEnumerable<ProjectBriefingDeckBlockType>? order)
        => (order ?? Array.Empty<ProjectBriefingDeckBlockType>())
            .Where(type => Enum.IsDefined(type) && ZoneOf(type) == ProjectBriefingDeckZone.Overview)
            .Distinct()
            .ToArray();

    // Every block the deck's layout can have, switched on or off from its settings, in the order the
    // deck draws them: the opening (cover first, then the institution slides in their own order),
    // the overview in the stored order (blocks it does not name keep their usual place after the
    // named ones), the project slides, and the closing slides. The Deck tab lists exactly these.
    public static IReadOnlyList<ProjectBriefingDeckBlock> ForDeck(
        ProjectBriefingLayout layout,
        ProjectBriefingPresentationMode presentationMode,
        bool includeCoverSlide,
        bool includePortfolioSummarySlide,
        bool includeStageSummary,
        bool includeProjectCategorySummary,
        bool includeTechnicalCategorySummary,
        IReadOnlyList<ProjectBriefingAdditionalSlideType> additionalSlideOrder,
        Func<ProjectBriefingAdditionalSlideType, bool> additionalSlideEnabled,
        IReadOnlyList<ProjectBriefingDeckBlockType> overviewOrder)
    {
        ArgumentNullException.ThrowIfNull(additionalSlideOrder);
        ArgumentNullException.ThrowIfNull(additionalSlideEnabled);
        var standard = layout != ProjectBriefingLayout.ProjectUpdateSheet;
        var blocks = new List<ProjectBriefingDeckBlock> { new(ProjectBriefingDeckBlockType.Cover, includeCoverSlide) };

        // The institution slides keep their own stored order; ones never added follow, switched off.
        var additional = additionalSlideOrder
            .Where(ProjectBriefingAdditionalSlideCatalog.IsRegistered)
            .Concat(ProjectBriefingAdditionalSlideCatalog.All.Select(definition => definition.Type))
            .Distinct()
            .ToArray();
        blocks.AddRange(additional
            .Where(type => ProjectBriefingAdditionalSlideCatalog.Get(type).Placement == ProjectBriefingAdditionalSlidePlacement.AfterCover)
            .Select(type => new ProjectBriefingDeckBlock(ForAdditionalSlide(type), additionalSlideOrder.Contains(type) && additionalSlideEnabled(type))));

        var overview = new List<ProjectBriefingDeckBlock>
        {
            new(ProjectBriefingDeckBlockType.PortfolioSummary, includePortfolioSummarySlide)
        };
        if (standard)
        {
            overview.Add(new(ProjectBriefingDeckBlockType.StageSummary, includeStageSummary));
            overview.Add(new(ProjectBriefingDeckBlockType.ProjectCategorySummary, includeProjectCategorySummary));
            overview.Add(new(ProjectBriefingDeckBlockType.TechnicalCategorySummary, includeTechnicalCategorySummary));
            overview.Add(new(
                ProjectBriefingDeckBlockType.StatusTable,
                presentationMode is ProjectBriefingPresentationMode.ExecutiveTable or ProjectBriefingPresentationMode.Combined));
        }

        blocks.AddRange(Arrange(overview, overviewOrder));
        blocks.Add(new(
            ProjectBriefingDeckBlockType.ProjectSlides,
            !standard || presentationMode is ProjectBriefingPresentationMode.DetailedProjects or ProjectBriefingPresentationMode.Combined));
        blocks.AddRange(additional
            .Where(type => ProjectBriefingAdditionalSlideCatalog.Get(type).Placement == ProjectBriefingAdditionalSlidePlacement.BeforeClosing)
            .Select(type => new ProjectBriefingDeckBlock(ForAdditionalSlide(type), additionalSlideOrder.Contains(type) && additionalSlideEnabled(type))));
        return blocks;
    }

    // Blocks named in the stored order first, in that order; the rest keep their given order.
    public static IReadOnlyList<ProjectBriefingDeckBlock> Arrange(
        IReadOnlyList<ProjectBriefingDeckBlock> blocks,
        IReadOnlyList<ProjectBriefingDeckBlockType> order)
        => blocks
            .Select((block, index) => (block, index, rank: IndexOf(order, block.Type)))
            .OrderBy(item => item.rank < 0 ? int.MaxValue : item.rank)
            .ThenBy(item => item.index)
            .Select(item => item.block)
            .ToArray();

    private static int IndexOf(IReadOnlyList<ProjectBriefingDeckBlockType> order, ProjectBriefingDeckBlockType type)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == type) return index;
        }

        return -1;
    }

    // Blocks a deck can do without: they are added from the Deck tab's library and removed from the
    // list. The cover and the project slides stay listed and are switched on or off in place.
    public static bool IsOptional(ProjectBriefingDeckBlockType type)
        => type is not (ProjectBriefingDeckBlockType.Cover or ProjectBriefingDeckBlockType.ProjectSlides);

    public static string DisplayName(ProjectBriefingDeckBlockType type) => type switch
    {
        ProjectBriefingDeckBlockType.Cover => "Cover",
        ProjectBriefingDeckBlockType.InstitutionalProfile => "SDD profile",
        ProjectBriefingDeckBlockType.RoleAndCharter => "Role & Charter",
        ProjectBriefingDeckBlockType.PortfolioSummary => "Portfolio summary",
        ProjectBriefingDeckBlockType.StageSummary => "Stage-wise summary",
        ProjectBriefingDeckBlockType.ProjectCategorySummary => "Project-category distribution",
        ProjectBriefingDeckBlockType.TechnicalCategorySummary => "Technical-category distribution",
        ProjectBriefingDeckBlockType.StatusTable => "Status table",
        ProjectBriefingDeckBlockType.ProjectSlides => "Project slides",
        ProjectBriefingDeckBlockType.FfcGlobalFootprint => "FFC footprint",
        _ => type.ToString()
    };

    public static string Description(ProjectBriefingDeckBlockType type) => type switch
    {
        ProjectBriefingDeckBlockType.Cover => "Deck name, date and marking",
        ProjectBriefingDeckBlockType.InstitutionalProfile => "Growth and institutional output",
        ProjectBriefingDeckBlockType.RoleAndCharter => "Authorised role and functions",
        ProjectBriefingDeckBlockType.PortfolioSummary => "Counts by lifecycle and stage",
        ProjectBriefingDeckBlockType.StageSummary => "Where this deck's projects stand",
        ProjectBriefingDeckBlockType.ProjectCategorySummary => "This deck's projects by project category",
        ProjectBriefingDeckBlockType.TechnicalCategorySummary => "This deck's projects by technical category",
        ProjectBriefingDeckBlockType.StatusTable => "One row per project",
        ProjectBriefingDeckBlockType.ProjectSlides => "Slides for each project",
        ProjectBriefingDeckBlockType.FfcGlobalFootprint => "Countries and quantities, before closing",
        _ => string.Empty
    };

    public static ProjectBriefingAdditionalSlideType? AdditionalSlideFor(ProjectBriefingDeckBlockType type) => type switch
    {
        ProjectBriefingDeckBlockType.InstitutionalProfile => ProjectBriefingAdditionalSlideType.InstitutionalProfile,
        ProjectBriefingDeckBlockType.RoleAndCharter => ProjectBriefingAdditionalSlideType.RoleAndCharter,
        ProjectBriefingDeckBlockType.FfcGlobalFootprint => ProjectBriefingAdditionalSlideType.FfcGlobalFootprint,
        _ => null
    };

    public static IReadOnlyList<ProjectBriefingDeckBlock> FromPresentation(ProjectBriefingPresentationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return FromSettings(
            data.Layout,
            data.PresentationMode,
            data.IncludeCoverSlide,
            data.IncludePortfolioSummarySlide,
            data.IncludeStageSummary,
            data.IncludeProjectCategorySummary,
            data.IncludeTechnicalCategorySummary,
            data.AdditionalSlideOrder);
    }

    private static IEnumerable<ProjectBriefingDeckBlock> AdditionalBlocks(
        IReadOnlyList<ProjectBriefingAdditionalSlideType> order,
        ProjectBriefingAdditionalSlidePlacement placement)
        => order
            .Where(type => ProjectBriefingAdditionalSlideCatalog.IsRegistered(type)
                && ProjectBriefingAdditionalSlideCatalog.Get(type).Placement == placement)
            .Select(type => new ProjectBriefingDeckBlock(ForAdditionalSlide(type)));
}
