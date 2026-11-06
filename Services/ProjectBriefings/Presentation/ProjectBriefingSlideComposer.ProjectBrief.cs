using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

/// <summary>
/// Project brief slides. A brief that fits the panel beside the photograph keeps its familiar
/// typography on one slide. A longer one would previously only shrink; it now keeps the photograph,
/// the start of the brief and the status/cost strip on the first slide and continues, at a
/// readable size (never below the 12 pt update-sheet profile), on full-width
/// "Project brief — continued" slides, split at paragraph or sentence boundaries.
/// </summary>
public sealed partial class ProjectBriefingSlideComposer
{
    // Slide geometry of the brief panel (inches), matching the two brief layouts.
    private const double BriefTop = 1.28;
    private const double BriefBottom = 6.70;
    private const double BriefSectionGap = .16;
    private const double BriefStandardPanelWidth = 8.25;
    private const double BriefPhotoEmphasisPanelWidth = 12.73 - (.60 + 5.48 + .22);
    private const double BriefContinuationX = .60;
    private const double BriefContinuationWidth = 12.13;
    private const double BriefContinuationHeight = 5.42;

    // Inside the panel: text box insets (.28 + .28 + .05 + .05) and padding (.20 + .20 + .02 + .02),
    // plus the heading line (12.6 pt) and the space after it (12 pt).
    private const double BriefPanelHorizontalInsets = .66;
    private const double BriefPanelVerticalInsets = .44 + ((12.6 + 12.0) / 72d);

    // How many slides this project's brief takes in this deck, by the same pagination the slides
    // use (photo layout, information strip and cost cards included).
    public static int ProjectBriefSlideCount(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
        => PaginateProjectBrief(data, project).Count;

    private static IReadOnlyList<ProjectBriefingUpdateSheetBriefPage> PaginateProjectBrief(
        ProjectBriefingPresentationData data,
        ProjectBriefingPresentationProject project)
    {
        if (UsesKeyPointLayout(data))
        {
            return PaginateKeyPointProjectBrief(data, project);
        }

        var brief = ProjectBriefingUpdateSheetPlanner.NormalizeBrief(project.ProjectBrief);
        var panelWidth = ResolveProjectBriefLayout(data, project) == ProjectBriefingProjectBriefLayout.PhotoEmphasis
            ? BriefPhotoEmphasisPanelWidth
            : BriefStandardPanelWidth;
        var stripHeight = ResolveProjectBriefInformationStripHeight(
            ResolvePresentStatusValue(
                project,
                data.StandardSlideOptions.ShowPresentStage,
                data.StandardSlideOptions.ShowPresentStatus),
            CostCardCount(data.CostMode, project));
        var contentBottom = stripHeight > 0 ? BriefBottom - stripHeight - BriefSectionGap : BriefBottom;

        return PaginateProjectBrief(brief, panelWidth, contentBottom - BriefTop);
    }

    /// <summary>
    /// Pages for a brief whose first slide offers a panel of the given size. Public for the slide
    /// estimate, which knows the brief but not the photograph or cost cards.
    /// </summary>
    public static IReadOnlyList<ProjectBriefingUpdateSheetBriefPage> PaginateProjectBrief(
        string normalizedBrief,
        double firstPanelWidth,
        double firstPanelHeight)
    {
        var firstWidth = Math.Max(.50, firstPanelWidth - BriefPanelHorizontalInsets);
        var firstHeight = Math.Max(.30, firstPanelHeight - BriefPanelVerticalInsets);

        if (ProjectBriefingUpdateSheetPlanner.IsMissingBrief(normalizedBrief))
        {
            return new[]
            {
                new ProjectBriefingUpdateSheetBriefPage(
                    normalizedBrief,
                    new ProjectBriefingUpdateSheetBriefTypography(13.0, 16.0, 0),
                    IsContinuation: false,
                    IsMissing: true)
            };
        }

        // Keep today's typography whenever it is readable and the brief fits beside the photo.
        var preferred = ProjectBriefingNarrativeTypography.ResolveProjectBrief(normalizedBrief);
        var preferredTypography = new ProjectBriefingUpdateSheetBriefTypography(
            preferred.BodyFontSize,
            preferred.LineSpacingPoints,
            preferred.SpaceAfterPoints);
        if (preferred.BodyFontSize >= 12.0
            && ProjectBriefingUpdateSheetPlanner.Fits(normalizedBrief, firstWidth, firstHeight, preferredTypography))
        {
            return new[]
            {
                new ProjectBriefingUpdateSheetBriefPage(
                    normalizedBrief,
                    preferredTypography,
                    IsContinuation: false,
                    IsMissing: false)
            };
        }

        return ProjectBriefingUpdateSheetPlanner.PaginateBrief(
            normalizedBrief,
            firstWidth,
            firstHeight,
            BriefContinuationWidth - BriefPanelHorizontalInsets,
            BriefContinuationHeight - BriefPanelVerticalInsets);
    }

    /// <summary>Brief slides for the estimate: the standard layout with a status strip.</summary>
    public static int EstimateProjectBriefSlideCount(string? projectBrief)
        => PaginateProjectBrief(
            ProjectBriefingUpdateSheetPlanner.NormalizeBrief(projectBrief),
            BriefStandardPanelWidth,
            BriefBottom - .94 - BriefSectionGap - BriefTop).Count;

    // The same cards CostCards() draws, counted without a canvas.
    // The cost cards the slide draws for this project, by the same rule as CostCards: no
    // proliferation card for an ongoing project without one (owner, 6 Nov 2026).
    private static int CostCardCount(ProjectBriefingCostMode mode, ProjectBriefingPresentationProject project)
    {
        var count = 0;
        if (mode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both) count++;
        if (mode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both
            && ProjectBriefingContentGaps.ShowsProliferationCost(project.LifecycleStatus, project.ProliferationCost)) count++;
        return count;
    }

    private static void RenderProjectBriefContinuation(
        SlideCanvas canvas,
        ProjectBriefingPresentationProject project,
        ProjectBriefingUpdateSheetBriefPage page,
        int continuationPage,
        int continuationPages)
    {
        AddSlideTitle(
            canvas,
            Truncate(project.ProjectName, 82),
            $"{project.LifecycleDisplay} · {CategoryLine(project)}");

        var heading = continuationPages > 1
            ? $"PROJECT BRIEF — CONTINUED ({continuationPage}/{continuationPages})"
            : "PROJECT BRIEF — CONTINUED";

        AddProjectBriefPanel(
            canvas,
            BriefContinuationX,
            BriefTop,
            BriefContinuationWidth,
            BriefContinuationHeight,
            page,
            heading);
    }
}
