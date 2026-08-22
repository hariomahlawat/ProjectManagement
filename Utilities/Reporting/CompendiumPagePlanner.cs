using ProjectManagement.Services.Compendiums;

namespace ProjectManagement.Utilities.Reporting;

public enum CompendiumPageKind
{
    Cover = 0,
    Index = 1,
    Project = 2,
    ProjectContinuation = 3,
    BackCover = 4
}

public enum CompendiumProjectLayoutVariant
{
    PhotoLong = 0,
    PhotoMedium = 1,
    PhotoShort = 2,
    NoPhoto = 3
}

public sealed record CompendiumIndexEntryPlan(
    int ProjectId,
    string ProjectName,
    string LifecycleDisplay,
    string CompletionDisplay,
    int ProjectPageNumber);

public sealed record CompendiumIndexGroupPlan(
    string CategoryName,
    IReadOnlyList<CompendiumIndexEntryPlan> Projects);

public sealed record CompendiumPagePlanItem(
    int PhysicalPageNumber,
    CompendiumPageKind Kind)
{
    public IReadOnlyList<CompendiumIndexGroupPlan> IndexGroups { get; init; }
        = Array.Empty<CompendiumIndexGroupPlan>();
    public CompendiumPdfProjectSection? Project { get; init; }
    public string DescriptionMarkdown { get; init; } = string.Empty;
    public CompendiumProjectLayoutVariant ProjectLayout { get; init; }
        = CompendiumProjectLayoutVariant.PhotoMedium;
    public bool IsFirstProjectInCategory { get; init; }
    public int ContinuationPart { get; init; }
    public IReadOnlyList<string> TechnicalSpecifications { get; init; } = Array.Empty<string>();
    public string AdditionalNoteMarkdown { get; init; } = string.Empty;
    public bool IsTechnicalContinuation { get; init; }
    public bool IsAdditionalNoteContinuation { get; init; }
}

public sealed record CompendiumPagePlan(
    IReadOnlyList<CompendiumPagePlanItem> Pages,
    IReadOnlyDictionary<int, int> ProjectStartPages)
{
    public int ExpectedPageCount => Pages.Count;
    public int IndexPageCount => Pages.Count(page => page.Kind == CompendiumPageKind.Index);
    public int ProjectPageCount => Pages.Count(page =>
        page.Kind is CompendiumPageKind.Project or CompendiumPageKind.ProjectContinuation);
}

public interface ICompendiumPagePlanner
{
    CompendiumPagePlan Plan(CompendiumPdfReportContext context);
}

/// <summary>
/// Deterministic physical-page planner. The planner decides page membership before QuestPDF draws
/// anything. The generated PDF is subsequently reopened and verified against this exact plan.
/// </summary>
public sealed class CompendiumPagePlanner : ICompendiumPagePlanner
{
    public CompendiumPagePlan Plan(CompendiumPdfReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projectSeeds = new List<ProjectPageSeed>();
        foreach (var category in context.Categories)
        {
            var firstInCategory = true;
            foreach (var project in category.Projects)
            {
                var projectSeedStartIndex = projectSeeds.Count;
                var hasPhoto = project.Images.Any(image => image.Content is { Length: > 0 }) || project.CoverPhoto is { Length: > 0 };
                var layout = ResolveLayout(project.DescriptionMarkdown, hasPhoto);
                var continuationBodyHeight = CompendiumDossierPaginationPlanner.ResolveContinuationBodyHeightPoints(project.ProjectName);
                var cleanSpecifications = (project.TechnicalSpecifications ?? Array.Empty<string>())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Take(6)
                    .ToArray();
                var firstSpecificationCount = Math.Clamp(
                    project.DossierFirstPageSpecificationCount,
                    0,
                    cleanSpecifications.Length);
                var firstSpecs = cleanSpecifications.Take(firstSpecificationCount).ToArray();
                var flow = project.NarrativeFlow;
                if (string.IsNullOrWhiteSpace(flow.SideSegment)
                    && string.IsNullOrWhiteSpace(flow.BelowImageSegment)
                    && flow.ContinuationSegments.Count == 0
                    && !string.IsNullOrWhiteSpace(project.DescriptionMarkdown))
                {
                    flow = CompendiumDossierNarrativeFlowPlanner.Resolve(
                        project.DescriptionMarkdown,
                        project.BalancedTextFlowMode,
                        project.DossierLayout,
                        hasPhoto,
                        project.DossierPrimaryImageHeightPoints,
                        project.DossierNarrativeFontScale,
                        project.DossierFirstPageNarrativeBudget,
                        project.NarrativeAlignment,
                        CompendiumDossierPaginationPlanner.ResolveBalancedSideWidthPoints(project.Images.Count),
                        project.DossierFirstPageNarrativeHeightPoints);
                }
                var firstNarrative = string.Join("\n\n", new[] { flow.SideSegment, flow.BelowImageSegment }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                var chunks = new[] { firstNarrative }.Concat(flow.ContinuationSegments).ToArray();

                var remainingSpecChunks = CompendiumDossierPaginationPlanner
                    .SplitTechnicalSpecificationsForPhysicalPages(
                        cleanSpecifications.Skip(firstSpecificationCount).ToArray(),
                        project.DossierSpecificationColumns,
                        continuationBodyHeight)
                    .ToList();
                IReadOnlyList<string> attachedContinuationSpecifications = Array.Empty<string>();
                if (chunks.Length > 1
                    && remainingSpecChunks.Count > 0
                    && CompendiumDossierPaginationPlanner.CanShareContinuationPage(
                        chunks[^1],
                        remainingSpecChunks[0],
                        project.DossierSpecificationColumns,
                        project.DossierNarrativeFontScale,
                        availableHeightPoints: continuationBodyHeight))
                {
                    attachedContinuationSpecifications = remainingSpecChunks[0];
                    remainingSpecChunks.RemoveAt(0);
                }

                var additionalNote = CompendiumPublicationNotePolicy.Normalize(project.AdditionalNote);
                var keepNoteOnFirstPage = additionalNote.Length > 0
                    && project.EstimatedDossierPageCount <= 1
                    && chunks.Length == 1
                    && remainingSpecChunks.Count == 0;

                for (var index = 0; index < chunks.Length; index++)
                {
                    var continuationSpecifications = index == 0
                        ? firstSpecs
                        : index == chunks.Length - 1
                            ? attachedContinuationSpecifications
                            : Array.Empty<string>();
                    projectSeeds.Add(new ProjectPageSeed(
                        project,
                        category.CategoryName,
                        index == 0 ? CompendiumPageKind.Project : CompendiumPageKind.ProjectContinuation,
                        chunks[index],
                        layout,
                        firstInCategory && index == 0,
                        index,
                        continuationSpecifications,
                        index == 0 && keepNoteOnFirstPage ? additionalNote : string.Empty,
                        false,
                        false));
                }

                var continuationIndex = chunks.Length;
                foreach (var specChunk in remainingSpecChunks)
                {
                    projectSeeds.Add(new ProjectPageSeed(
                        project,
                        category.CategoryName,
                        CompendiumPageKind.ProjectContinuation,
                        string.Empty,
                        layout,
                        false,
                        continuationIndex++,
                        specChunk,
                        string.Empty,
                        true,
                        false));
                }

                if (additionalNote.Length > 0 && !keepNoteOnFirstPage)
                {
                    var noteChunks = CompendiumDossierNarrativeFlowPlanner.SplitForPhysicalPages(
                            additionalNote,
                            CompendiumLayoutMetrics.ContentWidthPoints,
                            continuationBodyHeight,
                            project.DossierNarrativeFontScale,
                            includeHeading: false,
                            allowMinorHeadings: false)
                        .ToList();

                    // When the last continuation page has genuine measured room, append the first
                    // note chunk there. The note remains the final substantive block and a new page
                    // is created only when the physical page cannot accommodate it safely.
                    if (noteChunks.Count > 0
                        && projectSeeds.Count > projectSeedStartIndex
                        && projectSeeds[^1].Project.ProjectId == project.ProjectId
                        && projectSeeds[^1].Kind == CompendiumPageKind.ProjectContinuation)
                    {
                        var last = projectSeeds[^1];
                        if (CompendiumDossierPaginationPlanner.CanShareContinuationPage(
                                last.DescriptionMarkdown,
                                last.TechnicalSpecifications,
                                project.DossierSpecificationColumns,
                                project.DossierNarrativeFontScale,
                                noteChunks[0],
                                continuationBodyHeight))
                        {
                            projectSeeds[^1] = last with { AdditionalNoteMarkdown = noteChunks[0] };
                            noteChunks.RemoveAt(0);
                        }
                    }

                    foreach (var noteChunk in noteChunks)
                    {
                        projectSeeds.Add(new ProjectPageSeed(
                            project,
                            category.CategoryName,
                            CompendiumPageKind.ProjectContinuation,
                            string.Empty,
                            layout,
                            false,
                            continuationIndex++,
                            Array.Empty<string>(),
                            noteChunk,
                            false,
                            true));
                    }
                }

                firstInCategory = false;
            }
        }

        var indexSeeds = BuildIndexSeeds(context.Title, context.Categories);
        var projectStartPhysicalPage = 1 + indexSeeds.Count + 1; // Cover + all index pages + first project.
        var projectStartPages = new Dictionary<int, int>();
        var cursor = projectStartPhysicalPage;
        foreach (var seed in projectSeeds)
        {
            if (seed.Kind == CompendiumPageKind.Project)
            {
                projectStartPages[seed.Project.ProjectId] = cursor;
            }
            cursor++;
        }

        var pages = new List<CompendiumPagePlanItem>
        {
            new(1, CompendiumPageKind.Cover)
        };

        for (var index = 0; index < indexSeeds.Count; index++)
        {
            var seed = indexSeeds[index];
            var groups = seed.Groups
                .Select(group => new CompendiumIndexGroupPlan(
                    group.CategoryName,
                    group.Projects.Select(project => new CompendiumIndexEntryPlan(
                        project.ProjectId,
                        project.ProjectName,
                        project.LifecycleDisplay,
                        project.CompletionYearDisplay,
                        projectStartPages.GetValueOrDefault(project.ProjectId))).ToArray()))
                .ToArray();

            pages.Add(new CompendiumPagePlanItem(index + 2, CompendiumPageKind.Index)
            {
                IndexGroups = groups
            });
        }

        var physical = projectStartPhysicalPage;
        foreach (var seed in projectSeeds)
        {
            pages.Add(new CompendiumPagePlanItem(physical++, seed.Kind)
            {
                Project = seed.Project,
                DescriptionMarkdown = seed.DescriptionMarkdown,
                ProjectLayout = seed.Layout,
                IsFirstProjectInCategory = seed.IsFirstProjectInCategory,
                ContinuationPart = seed.ContinuationPart,
                TechnicalSpecifications = seed.TechnicalSpecifications,
                AdditionalNoteMarkdown = seed.AdditionalNoteMarkdown,
                IsTechnicalContinuation = seed.IsTechnicalContinuation,
                IsAdditionalNoteContinuation = seed.IsAdditionalNoteContinuation
            });
        }

        pages.Add(new CompendiumPagePlanItem(physical, CompendiumPageKind.BackCover));
        return new CompendiumPagePlan(pages, projectStartPages);
    }


    private static CompendiumProjectLayoutVariant ResolveLayout(string? markdown, bool hasPhoto)
    {
        if (!hasPhoto) return CompendiumProjectLayoutVariant.NoPhoto;
        var frameHeight = CompendiumPublicationImagePolicy.ResolveFrameHeightPoints(markdown);
        if (frameHeight >= CompendiumPublicationImagePolicy.ShortFrameHeightPoints) return CompendiumProjectLayoutVariant.PhotoShort;
        if (frameHeight >= CompendiumPublicationImagePolicy.MediumFrameHeightPoints) return CompendiumProjectLayoutVariant.PhotoMedium;
        return CompendiumProjectLayoutVariant.PhotoLong;
    }

    private static IReadOnlyList<IndexPageSeed> BuildIndexSeeds(
        string? publicationTitle,
        IReadOnlyList<CompendiumPdfCategorySection> categories)
    {
        var pages = new List<IndexPageSeed>();
        var current = new List<IndexGroupSeed>();
        var measurement = new CompendiumDossierTextMeasurementService.Session();
        var baseHeight = MeasureIndexHeadingHeight(publicationTitle, measurement)
                         + CompendiumLayoutMetrics.IndexColumnSpacingPoints
                         + CompendiumLayoutMetrics.IndexRuleHeightPoints;
        var usedHeight = baseHeight;
        var hideProjectHeading = categories.Count == 1
                                 && string.Equals(categories[0].CategoryName, "Projects", StringComparison.OrdinalIgnoreCase);

        void Flush()
        {
            if (current.Count == 0) return;
            pages.Add(new IndexPageSeed(current.ToArray()));
            current = new List<IndexGroupSeed>();
            usedHeight = baseHeight;
        }

        foreach (var category in categories)
        {
            var projectIndex = 0;
            while (projectIndex < category.Projects.Count)
            {
                var groupProjects = new List<CompendiumPdfProjectSection>();
                var groupHeight = hideProjectHeading
                    ? 0f
                    : MeasureIndexGroupHeadingHeight(category.CategoryName, measurement);

                while (projectIndex < category.Projects.Count)
                {
                    var project = category.Projects[projectIndex];
                    var rowHeight = MeasureIndexProjectRowHeight(project.ProjectName, measurement);
                    var projectedGroupHeight = groupHeight + rowHeight;
                    var projectedPageHeight = usedHeight
                                              + CompendiumLayoutMetrics.IndexColumnSpacingPoints
                                              + projectedGroupHeight;

                    if (projectedPageHeight > CompendiumLayoutMetrics.SecondaryContentHeightPoints
                        && groupProjects.Count > 0)
                    {
                        break;
                    }

                    if (projectedPageHeight > CompendiumLayoutMetrics.SecondaryContentHeightPoints
                        && groupProjects.Count == 0
                        && current.Count > 0)
                    {
                        Flush();
                        continue;
                    }

                    // A single exceptionally tall project row is still kept intact on an otherwise
                    // empty index page. ShowEntire() in the compositor turns any impossible geometry
                    // into a controlled generation failure instead of a silent extra PDF page.
                    groupProjects.Add(project);
                    groupHeight = projectedGroupHeight;
                    projectIndex++;
                }

                if (groupProjects.Count == 0)
                {
                    groupProjects.Add(category.Projects[projectIndex++]);
                    groupHeight += MeasureIndexProjectRowHeight(groupProjects[0].ProjectName, measurement);
                }

                current.Add(new IndexGroupSeed(category.CategoryName, groupProjects.ToArray()));
                usedHeight += CompendiumLayoutMetrics.IndexColumnSpacingPoints + groupHeight;

                if (projectIndex < category.Projects.Count) Flush();
            }
        }

        Flush();
        if (pages.Count == 0) pages.Add(new IndexPageSeed(Array.Empty<IndexGroupSeed>()));
        return pages;
    }

    private static float MeasureIndexHeadingHeight(
        string? publicationTitle,
        CompendiumDossierTextMeasurementService.Session measurement)
    {
        var leftWidth = Math.Max(120f,
            CompendiumLayoutMetrics.ContentWidthPoints - CompendiumLayoutMetrics.IndexGroupCountReserveWidthPoints);
        var heading = measurement.MeasureAtFontSize(
            "Compendium Index",
            leftWidth,
            CompendiumLayoutMetrics.IndexHeadingTitleFontSize,
            CompendiumLayoutMetrics.IndexHeadingTitleLineHeightMultiplier,
            semiBold: true).HeightPoints;
        var title = measurement.MeasureAtFontSize(
            publicationTitle,
            leftWidth,
            CompendiumLayoutMetrics.IndexPublicationTitleFontSize,
            CompendiumLayoutMetrics.IndexPublicationTitleLineHeightMultiplier).HeightPoints;
        return Math.Max(12f, heading + title);
    }

    private static float MeasureIndexGroupHeadingHeight(
        string? categoryName,
        CompendiumDossierTextMeasurementService.Session measurement)
    {
        var width = Math.Max(100f,
            CompendiumLayoutMetrics.ContentWidthPoints
            - CompendiumLayoutMetrics.IndexGroupLeftBorderPoints
            - (2f * CompendiumLayoutMetrics.IndexGroupHorizontalPaddingPoints)
            - CompendiumLayoutMetrics.IndexGroupCountReserveWidthPoints);
        var textHeight = measurement.MeasureAtFontSize(
            categoryName,
            width,
            CompendiumLayoutMetrics.IndexGroupTitleFontSize,
            CompendiumLayoutMetrics.IndexGroupTitleLineHeightMultiplier,
            semiBold: true).HeightPoints;
        return Math.Max(13.8f, textHeight)
               + (2f * CompendiumLayoutMetrics.IndexGroupVerticalPaddingPoints);
    }

    private static float MeasureIndexProjectRowHeight(
        string? projectName,
        CompendiumDossierTextMeasurementService.Session measurement)
    {
        var width = Math.Max(100f,
            CompendiumLayoutMetrics.ContentWidthPoints
            - (2f * CompendiumLayoutMetrics.IndexRowHorizontalPaddingPoints)
            - CompendiumLayoutMetrics.IndexLifecycleWidthPoints
            - CompendiumLayoutMetrics.IndexPageNumberWidthPoints);
        var nameHeight = measurement.MeasureAtFontSize(
            projectName,
            width,
            CompendiumLayoutMetrics.IndexProjectNameFontSize,
            CompendiumLayoutMetrics.IndexProjectNameLineHeightMultiplier).HeightPoints;
        return Math.Max(CompendiumLayoutMetrics.IndexMinimumTextLineHeightPoints, nameHeight)
               + (2f * CompendiumLayoutMetrics.IndexRowVerticalPaddingPoints)
               + CompendiumLayoutMetrics.IndexRowBorderBottomPoints;
    }

    private sealed record ProjectPageSeed(
        CompendiumPdfProjectSection Project,
        string CategoryName,
        CompendiumPageKind Kind,
        string DescriptionMarkdown,
        CompendiumProjectLayoutVariant Layout,
        bool IsFirstProjectInCategory,
        int ContinuationPart,
        IReadOnlyList<string> TechnicalSpecifications,
        string AdditionalNoteMarkdown,
        bool IsTechnicalContinuation,
        bool IsAdditionalNoteContinuation);

    private sealed record IndexGroupSeed(
        string CategoryName,
        IReadOnlyList<CompendiumPdfProjectSection> Projects);

    private sealed record IndexPageSeed(IReadOnlyList<IndexGroupSeed> Groups);
}
