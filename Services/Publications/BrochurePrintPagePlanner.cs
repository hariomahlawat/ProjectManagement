namespace ProjectManagement.Services.Publications;

public interface IBrochurePrintPagePlanner
{
    BrochurePrintCompactPlan Plan(
        IReadOnlyList<BrochurePrintPlanningItem> projects,
        BrochurePrintMatter? printMatter,
        BrochureCoverStyle coverStyle,
        string? strapline,
        bool hasHandlingMarking);

    BrochurePrintCompactPlan Plan(
        IReadOnlyList<BrochurePublicationProject> projects,
        BrochurePrintMatter? printMatter,
        BrochureCoverStyle coverStyle,
        string? strapline,
        bool hasHandlingMarking);
}

/// <summary>
/// Order-preserving, font-aware hard-copy sheet planner. Phase 13 treats 9 pt project copy and
/// reference-quality image geometry as hard normal constraints, then minimises page count and worst
/// residual space. Compact 8.5 pt is available only when an individual project cannot physically fit
/// on a full sheet at 9 pt. The exact measurement selected here is consumed unchanged by QuestPDF.
/// </summary>
public sealed class BrochurePrintPagePlanner : IBrochurePrintPagePlanner
{
    private readonly IBrochurePrintMeasurementService _measurement;

    public BrochurePrintPagePlanner(IBrochurePrintMeasurementService measurement)
    {
        _measurement = measurement ?? throw new ArgumentNullException(nameof(measurement));
    }

    public BrochurePrintCompactPlan Plan(
        IReadOnlyList<BrochurePublicationProject> projects,
        BrochurePrintMatter? printMatter,
        BrochureCoverStyle coverStyle,
        string? strapline,
        bool hasHandlingMarking)
        => Plan(
            projects.Select(project => new BrochurePrintPlanningItem(
                project.ProjectId,
                project.ProjectName,
                project.Narrative,
                project.ImageMode,
                project.PrimaryPhoto is not null,
                project.SecondaryPhoto is not null)).ToArray(),
            printMatter,
            coverStyle,
            strapline,
            hasHandlingMarking);

    public BrochurePrintCompactPlan Plan(
        IReadOnlyList<BrochurePrintPlanningItem> projects,
        BrochurePrintMatter? printMatter,
        BrochureCoverStyle coverStyle,
        string? strapline,
        bool hasHandlingMarking)
    {
        ArgumentNullException.ThrowIfNull(projects);

        printMatter ??= BrochurePrintPublicationPolicy.ApprovedReference;
        var frontPage = _measurement.MeasureFrontPage(printMatter, coverStyle, strapline);
        var closing = _measurement.MeasureClosing(printMatter, strapline);
        var capacity = BrochurePrintLayoutMetrics.ProjectContentCapacity(hasHandlingMarking);

        if (projects.Count == 0)
        {
            return new BrochurePrintCompactPlan(
                Array.Empty<BrochurePrintCompactPage>(),
                frontPage,
                closing,
                EstimatedTotalPageCount: 1,
                AverageContentUtilizationPercent: 0,
                ClosingMatterSharesFinalPage: false,
                ClosingPageProjectCount: 0,
                LowestProjectPageUtilizationPercent: null,
                FinalPageUtilizationPercent: null,
                SheetPlan: new[]
                {
                    new BrochurePrintSheetSummary(
                        SheetNumber: 1,
                        Kind: "front",
                        FirstProjectOrdinal: null,
                        LastProjectOrdinal: null,
                        ProjectCount: 0,
                        IncludesClosingMatter: false,
                        UtilizationPercent: frontPage.UtilizationPercent,
                        Label: "Institutional front page")
                });
        }

        var measurements = projects
            .Select(item => new ProjectCandidateSet(
                _measurement.MeasureProject(item, BrochurePrintLayoutVariant.Visual),
                _measurement.MeasureProject(item, BrochurePrintLayoutVariant.Balanced),
                _measurement.MeasureProject(item, BrochurePrintLayoutVariant.Compact)))
            .ToArray();

        var hasSharedPlan = TryPlanWithSharedClosing(
            measurements,
            closing.TotalHeightPoints,
            capacity,
            out var sharedPages);

        // Build the best project-only alternative as well. A shared closing sheet is desirable,
        // but it must never be purchased by reducing ordinary project copy below the 9 pt floor
        // when a dedicated closing sheet can preserve full publication quality.
        var hasProjectOnlyPlan = TryPlanProjectOnly(
            measurements,
            capacity,
            out var projectOnlyPages);
        if (!hasProjectOnlyPlan)
        {
            projectOnlyPages = Enumerable.Range(0, projects.Count)
                .Select(index => CreateSingleProjectFallback(index, measurements[index], capacity))
                .ToArray();
        }

        var sharedPenalty = hasSharedPlan
            ? sharedPages.Sum(page => page.TypographyPenalty)
            : int.MaxValue;
        var dedicatedPenalty = projectOnlyPages.Sum(page => page.TypographyPenalty);
        var dedicatedPageCount = projectOnlyPages.Count + 1;
        var chooseShared = hasSharedPlan
                           && (sharedPages.Count < dedicatedPageCount
                               || (sharedPages.Count == dedicatedPageCount
                                   && sharedPenalty <= dedicatedPenalty));

        if (chooseShared)
        {
            var expanded = ApplyResidualImageExpansion(
                sharedPages,
                projects,
                capacity);

            return BuildPlan(
                expanded,
                frontPage,
                closing,
                capacity,
                sharesClosingMatter: true,
                projects.Count);
        }

        projectOnlyPages = ApplyResidualImageExpansion(
            projectOnlyPages,
            projects,
            capacity);

        var dedicatedClosing = new PlannedSegment(
            Start: projects.Count,
            EndExclusive: projects.Count,
            Projects: Array.Empty<BrochurePrintPlannedProject>(),
            ProjectHeightPoints: 0f,
            PhysicalUsedPoints: closing.TotalHeightPoints,
            IncludesClosingMatter: true,
            ClosingHeightPoints: closing.TotalHeightPoints,
            UtilizationPercent: ToPercent(closing.TotalHeightPoints, capacity),
            TypographyPenalty: 0,
            Score: 0d);

        return BuildPlan(
            projectOnlyPages.Concat(new[] { dedicatedClosing }).ToArray(),
            frontPage,
            closing,
            capacity,
            sharesClosingMatter: false,
            projects.Count);
    }

    private static PlannedSegment CreateSingleProjectFallback(
        int projectIndex,
        ProjectCandidateSet candidates,
        float capacity)
    {
        var measurement = candidates.OrderedByQuality
            .FirstOrDefault(candidate => candidate.TotalHeightPoints <= capacity + .5f)
            ?? candidates.Compact;
        var planned = new BrochurePrintPlannedProject(projectIndex, measurement);
        return new PlannedSegment(
            projectIndex,
            projectIndex + 1,
            new[] { planned },
            measurement.TotalHeightPoints,
            measurement.TotalHeightPoints,
            IncludesClosingMatter: false,
            ClosingHeightPoints: 0f,
            UtilizationPercent: ToPercent(measurement.TotalHeightPoints, capacity),
            TypographyPenalty: TypographyPenalty(measurement),
            Score: 0d);
    }

    private static bool TryPlanWithSharedClosing(
        IReadOnlyList<ProjectCandidateSet> measurements,
        float closingHeight,
        float capacity,
        out IReadOnlyList<PlannedSegment> pages)
        => TryPlanInternal(
            measurements,
            capacity,
            finalClosingHeight: closingHeight,
            requireClosingOnFinalPage: true,
            out pages);

    private static bool TryPlanProjectOnly(
        IReadOnlyList<ProjectCandidateSet> measurements,
        float capacity,
        out IReadOnlyList<PlannedSegment> pages)
        => TryPlanInternal(
            measurements,
            capacity,
            finalClosingHeight: 0f,
            requireClosingOnFinalPage: false,
            out pages);

    private static bool TryPlanInternal(
        IReadOnlyList<ProjectCandidateSet> measurements,
        float capacity,
        float finalClosingHeight,
        bool requireClosingOnFinalPage,
        out IReadOnlyList<PlannedSegment> pages)
    {
        var count = measurements.Count;
        pages = Array.Empty<PlannedSegment>();
        var states = new PlannerState?[count + 1];
        states[0] = new PlannerState(
            PageCount: 0,
            TypographyPenalty: 0,
            WorstResidualFraction: 0d,
            Score: 0d,
            PreviousIndex: null,
            Segment: null);

        for (var start = 0; start < count; start++)
        {
            var state = states[start];
            if (state is null)
            {
                continue;
            }

            var maxEnd = Math.Min(count, start + BrochurePrintLayoutMetrics.MaximumProjectsPerSheet);
            for (var end = start + 1; end <= maxEnd; end++)
            {
                var isFinal = end == count;
                var includesClosingMatter = isFinal && requireClosingOnFinalPage;
                var closingHeight = includesClosingMatter ? finalClosingHeight : 0f;
                var closingGap = includesClosingMatter && closingHeight > .5f
                    ? BrochurePrintLayoutMetrics.ClosingGapPoints
                    : 0f;
                var projectCapacity = capacity - closingHeight - closingGap;
                if (projectCapacity <= 0f)
                {
                    continue;
                }

                var segment = FindBestSegment(
                    start,
                    end,
                    measurements,
                    projectCapacity,
                    closingHeight,
                    capacity,
                    includesClosingMatter);
                if (segment is null)
                {
                    continue;
                }

                var typographyPenalty = state.TypographyPenalty + segment.TypographyPenalty;
                var pageCount = state.PageCount + 1;
                var residualFraction = Math.Max(0d, (capacity - segment.PhysicalUsedPoints) / capacity);
                var worstResidual = Math.Max(state.WorstResidualFraction, residualFraction);
                var score = state.Score + segment.Score;
                var existing = states[end];

                // Normal candidates already satisfy the 9 pt hard floor. Among those valid layouts,
                // page count wins first, then the worst dead tail, then aggregate composition score.
                if (existing is null
                    || pageCount < existing.PageCount
                    || (pageCount == existing.PageCount && typographyPenalty < existing.TypographyPenalty)
                    || (pageCount == existing.PageCount
                        && typographyPenalty == existing.TypographyPenalty
                        && worstResidual < existing.WorstResidualFraction - .0001d)
                    || (pageCount == existing.PageCount
                        && typographyPenalty == existing.TypographyPenalty
                        && Math.Abs(worstResidual - existing.WorstResidualFraction) <= .0001d
                        && score < existing.Score))
                {
                    states[end] = new PlannerState(
                        pageCount,
                        typographyPenalty,
                        worstResidual,
                        score,
                        start,
                        segment);
                }
            }
        }

        var finalState = states[count];
        if (finalState is null)
        {
            return false;
        }

        var result = new List<PlannedSegment>(finalState.PageCount);
        var cursor = count;
        while (cursor > 0)
        {
            var state = states[cursor];
            if (state?.PreviousIndex is null || state.Segment is null)
            {
                return false;
            }

            result.Add(state.Segment);
            cursor = state.PreviousIndex.Value;
        }
        result.Reverse();
        pages = result;
        return true;
    }

    private static PlannedSegment? FindBestSegment(
        int start,
        int endExclusive,
        IReadOnlyList<ProjectCandidateSet> measurements,
        float projectCapacity,
        float closingHeight,
        float physicalCapacity,
        bool includesClosingMatter)
    {
        var itemCount = endExclusive - start;
        if (itemCount <= 0 || itemCount > BrochurePrintLayoutMetrics.MaximumProjectsPerSheet)
        {
            return null;
        }

        PlannedSegment? best = null;
        var selected = new BrochurePrintProjectMeasurement[itemCount];

        void Search(int offset)
        {
            if (offset == itemCount)
            {
                var projectHeight = selected.Sum(item => item.TotalHeightPoints)
                                    + (BrochurePrintLayoutMetrics.InterModuleSpacingPoints * Math.Max(0, itemCount - 1));
                if (projectHeight > projectCapacity + .5f)
                {
                    return;
                }

                var closingGap = includesClosingMatter && closingHeight > .5f
                    ? BrochurePrintLayoutMetrics.ClosingGapPoints
                    : 0f;
                var physicalUsed = projectHeight + closingGap + closingHeight;
                var utilization = Math.Min(1d, physicalUsed / physicalCapacity);
                var underfill = Math.Max(0d, BrochurePrintLayoutMetrics.TargetMinimumUtilization - utilization);
                var preferredDelta = Math.Abs(BrochurePrintLayoutMetrics.PreferredUtilization - utilization);
                var qualityPenalty = selected.Sum(item => 3 - item.QualityRank);
                var typographyPenalty = selected.Sum(TypographyPenalty);
                var singleProjectPenalty = itemCount == 1 ? 8d : 0d;

                var residual = Math.Max(0d, 1d - utilization);
                var score = (underfill * underfill * 5200d)
                            + (residual * residual * 900d)
                            + (preferredDelta * preferredDelta * 90d)
                            + (qualityPenalty * 3d)
                            + singleProjectPenalty;

                var projects = selected
                    .Select((measurement, index) => new BrochurePrintPlannedProject(start + index, measurement))
                    .ToArray();
                var candidate = new PlannedSegment(
                    start,
                    endExclusive,
                    projects,
                    projectHeight,
                    physicalUsed,
                    includesClosingMatter,
                    closingHeight,
                    ToPercent(physicalUsed, physicalCapacity),
                    typographyPenalty,
                    score);

                if (best is null
                    || candidate.TypographyPenalty < best.TypographyPenalty
                    || (candidate.TypographyPenalty == best.TypographyPenalty
                        && candidate.Score < best.Score))
                {
                    best = candidate;
                }
                return;
            }

            var set = measurements[start + offset];
            foreach (var candidate in set.CandidatesForSegment(itemCount, physicalCapacity))
            {
                selected[offset] = candidate;
                Search(offset + 1);
            }
        }

        Search(0);
        return best;
    }

    private IReadOnlyList<PlannedSegment> ApplyResidualImageExpansion(
        IReadOnlyList<PlannedSegment> pages,
        IReadOnlyList<BrochurePrintPlanningItem> projects,
        float capacity)
    {
        var result = new List<PlannedSegment>(pages.Count);
        var targetPhysicalUsed = capacity * BrochurePrintLayoutMetrics.ResidualTargetUtilization;

        foreach (var page in pages)
        {
            if (page.Projects.Count == 0
                || page.PhysicalUsedPoints >= targetPhysicalUsed - .5f)
            {
                result.Add(page);
                continue;
            }

            var selected = page.Projects.ToList();
            var boosts = selected.ToDictionary(
                planned => planned.ProjectIndex,
                _ => 0f);
            var currentProjectHeight = page.ProjectHeightPoints;
            var currentPhysicalUsed = page.PhysicalUsedPoints;

            while (currentPhysicalUsed < targetPhysicalUsed - .5f)
            {
                ExpansionCandidate? best = null;
                var currentDistance = Math.Abs(targetPhysicalUsed - currentPhysicalUsed);

                for (var offset = 0; offset < selected.Count; offset++)
                {
                    var planned = selected[offset];
                    var source = projects[planned.ProjectIndex];
                    if (!source.HasPrimaryPhoto)
                    {
                        continue;
                    }

                    var currentBoost = boosts[planned.ProjectIndex];
                    var nextBoost = currentBoost + BrochurePrintLayoutMetrics.ResidualImageExpansionStepPoints;
                    if (nextBoost > BrochurePrintLayoutMetrics.ResidualMaximumImageExpansionPoints + .01f)
                    {
                        continue;
                    }

                    var expanded = _measurement.MeasureProject(
                        source,
                        planned.Measurement.Variant,
                        nextBoost);
                    var delta = expanded.TotalHeightPoints - planned.Measurement.TotalHeightPoints;
                    if (delta <= .1f)
                    {
                        continue;
                    }

                    var nextProjectHeight = currentProjectHeight + delta;
                    var nextPhysicalUsed = currentPhysicalUsed + delta;
                    if (nextPhysicalUsed > capacity + .5f)
                    {
                        continue;
                    }

                    var nextDistance = Math.Abs(targetPhysicalUsed - nextPhysicalUsed);
                    if (nextDistance >= currentDistance - .05f)
                    {
                        continue;
                    }

                    var candidate = new ExpansionCandidate(
                        offset,
                        planned.ProjectIndex,
                        nextBoost,
                        expanded,
                        nextProjectHeight,
                        nextPhysicalUsed,
                        nextDistance);

                    if (best is null || candidate.DistanceToTarget < best.DistanceToTarget)
                    {
                        best = candidate;
                    }
                }

                if (best is null)
                {
                    break;
                }

                selected[best.Offset] = new BrochurePrintPlannedProject(
                    best.ProjectIndex,
                    best.Measurement);
                boosts[best.ProjectIndex] = best.ImageWidthBoostPoints;
                currentProjectHeight = best.ProjectHeightPoints;
                currentPhysicalUsed = best.PhysicalUsedPoints;
            }

            // Once imagery has reached its bounded reference-quality size, spend remaining
            // residual height on measured vertical breathing room rather than leaving a large
            // dead tail. This never changes page membership or typography.
            var extraModuleVerticalPadding = 0f;
            var extraInterModuleSpacing = 0f;
            var remainingToTarget = Math.Max(0f, targetPhysicalUsed - currentPhysicalUsed);
            if (remainingToTarget > .5f && selected.Count > 0)
            {
                extraModuleVerticalPadding = Math.Min(
                    BrochurePrintLayoutMetrics.ResidualMaximumExtraModuleVerticalPaddingPoints,
                    remainingToTarget / selected.Count);
                var moduleDelta = extraModuleVerticalPadding * selected.Count;
                currentProjectHeight += moduleDelta;
                currentPhysicalUsed += moduleDelta;
                remainingToTarget = Math.Max(0f, targetPhysicalUsed - currentPhysicalUsed);
            }

            if (remainingToTarget > .5f && selected.Count > 1)
            {
                extraInterModuleSpacing = Math.Min(
                    BrochurePrintLayoutMetrics.ResidualMaximumExtraInterModuleSpacingPoints,
                    remainingToTarget / (selected.Count - 1));
                var spacingDelta = extraInterModuleSpacing * (selected.Count - 1);
                currentProjectHeight += spacingDelta;
                currentPhysicalUsed += spacingDelta;
            }

            result.Add(page with
            {
                Projects = selected,
                ProjectHeightPoints = currentProjectHeight,
                PhysicalUsedPoints = currentPhysicalUsed,
                UtilizationPercent = ToPercent(currentPhysicalUsed, capacity),
                ExtraModuleVerticalPaddingPoints = extraModuleVerticalPadding,
                ExtraInterModuleSpacingPoints = extraInterModuleSpacing
            });
        }

        return result;
    }

    private static BrochurePrintCompactPlan BuildPlan(
        IReadOnlyList<PlannedSegment> segments,
        BrochurePrintFrontPagePlan frontPage,
        BrochurePrintClosingMeasurement closing,
        float capacity,
        bool sharesClosingMatter,
        int projectCount)
    {
        var pages = segments.Select(segment => new BrochurePrintCompactPage(
            segment.Projects,
            segment.ProjectHeightPoints,
            segment.PhysicalUsedPoints,
            capacity,
            segment.IncludesClosingMatter,
            segment.ClosingHeightPoints,
            segment.UtilizationPercent,
            segment.ExtraModuleVerticalPaddingPoints,
            segment.ExtraInterModuleSpacingPoints)).ToArray();

        var projectPages = pages.Where(page => page.Projects.Count > 0).ToArray();
        var averageUtilization = pages.Length == 0
            ? 0
            : (int)Math.Round(pages.Average(page => page.UtilizationPercent));
        var lowestProjectUtilization = projectPages.Length == 0
            ? (int?)null
            : projectPages.Min(page => page.UtilizationPercent);
        var finalPage = pages.LastOrDefault();
        var closingPage = pages.LastOrDefault(page => page.IncludesClosingMatter);

        var sheetSummaries = new List<BrochurePrintSheetSummary>(pages.Length + 1)
        {
            new(
                SheetNumber: 1,
                Kind: "front",
                FirstProjectOrdinal: null,
                LastProjectOrdinal: null,
                ProjectCount: 0,
                IncludesClosingMatter: false,
                UtilizationPercent: frontPage.UtilizationPercent,
                Label: "Institutional front page")
        };

        for (var index = 0; index < pages.Length; index++)
        {
            var page = pages[index];
            var first = page.Projects.Count > 0 ? page.Projects[0].ProjectIndex + 1 : (int?)null;
            var last = page.Projects.Count > 0 ? page.Projects[^1].ProjectIndex + 1 : (int?)null;
            var label = page.Projects.Count switch
            {
                0 when page.IncludesClosingMatter => "Closing institutional matter",
                > 0 when page.IncludesClosingMatter => $"Projects {first}{(first == last ? string.Empty : $"–{last}")} + closing",
                _ => $"Projects {first}{(first == last ? string.Empty : $"–{last}")}"
            };

            sheetSummaries.Add(new BrochurePrintSheetSummary(
                SheetNumber: index + 2,
                Kind: page.IncludesClosingMatter ? "closing" : "projects",
                FirstProjectOrdinal: first,
                LastProjectOrdinal: last,
                ProjectCount: page.Projects.Count,
                IncludesClosingMatter: page.IncludesClosingMatter,
                UtilizationPercent: page.UtilizationPercent,
                Label: label));
        }

        return new BrochurePrintCompactPlan(
            pages,
            frontPage,
            closing,
            EstimatedTotalPageCount: 1 + pages.Length,
            AverageContentUtilizationPercent: Math.Clamp(averageUtilization, 0, 100),
            ClosingMatterSharesFinalPage: sharesClosingMatter,
            ClosingPageProjectCount: closingPage?.Projects.Count ?? 0,
            LowestProjectPageUtilizationPercent: lowestProjectUtilization,
            FinalPageUtilizationPercent: finalPage?.UtilizationPercent,
            SheetPlan: sheetSummaries);
    }

    private static int TypographyPenalty(BrochurePrintProjectMeasurement measurement)
    {
        if (measurement.BodyFontSize >= BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize - .01f)
        {
            return 0;
        }

        var reduction = BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize - measurement.BodyFontSize;
        return 1 + Math.Max(0, (int)Math.Round(reduction * 4f));
    }

    private static int ToPercent(float used, float capacity)
        => capacity <= 0f
            ? 0
            : Math.Clamp((int)Math.Round(Math.Min(1d, used / capacity) * 100d), 0, 100);

    private sealed record ProjectCandidateSet(
        BrochurePrintProjectMeasurement Visual,
        BrochurePrintProjectMeasurement Balanced,
        BrochurePrintProjectMeasurement Compact)
    {
        public IEnumerable<BrochurePrintProjectMeasurement> OrderedByQuality
        {
            get
            {
                yield return Visual;
                yield return Balanced;
                yield return Compact;
            }
        }

        public IEnumerable<BrochurePrintProjectMeasurement> CandidatesForSegment(
            int itemCount,
            float fullSheetCapacity)
        {
            yield return Visual;
            yield return Balanced;

            // Compact is not a packing tool. It is exposed only when this individual project cannot
            // fit on a complete sheet at the normal 9 pt typography floor. It therefore cannot be
            // selected merely to squeeze closing matter or an additional project onto a page.
            var requiresEmergencyCompact = Visual.TotalHeightPoints > fullSheetCapacity + .5f
                                           && Balanced.TotalHeightPoints > fullSheetCapacity + .5f;
            if (itemCount == 1 && requiresEmergencyCompact)
            {
                yield return Compact;
            }
        }
    }

    private sealed record PlannerState(
        int PageCount,
        int TypographyPenalty,
        double WorstResidualFraction,
        double Score,
        int? PreviousIndex,
        PlannedSegment? Segment);

    private sealed record PlannedSegment(
        int Start,
        int EndExclusive,
        IReadOnlyList<BrochurePrintPlannedProject> Projects,
        float ProjectHeightPoints,
        float PhysicalUsedPoints,
        bool IncludesClosingMatter,
        float ClosingHeightPoints,
        int UtilizationPercent,
        int TypographyPenalty,
        double Score,
        float ExtraModuleVerticalPaddingPoints = 0f,
        float ExtraInterModuleSpacingPoints = 0f);

    private sealed record ExpansionCandidate(
        int Offset,
        int ProjectIndex,
        float ImageWidthBoostPoints,
        BrochurePrintProjectMeasurement Measurement,
        float ProjectHeightPoints,
        float PhysicalUsedPoints,
        float DistanceToTarget);
}
