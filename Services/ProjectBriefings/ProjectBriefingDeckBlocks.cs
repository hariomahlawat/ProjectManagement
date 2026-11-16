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
