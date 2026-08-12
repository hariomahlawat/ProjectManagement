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

    BrochurePrintCompactPlan PlanWithSmartFlow(
        IReadOnlyList<BrochurePrintPlanningItem> projects,
        BrochurePrintMatter? printMatter,
        BrochureCoverStyle coverStyle,
        string? strapline,
        bool hasHandlingMarking);
}

/// <summary>
/// Adaptive, order-safe hard-copy planner.
///
/// Phase 14 rules:
/// 1. Normal project copy remains 9 pt. Dense composition changes geometry, not typography.
/// 2. Each project exposes a Pareto frontier of valid 9 pt layouts, including planner-aware
///    Automatic Single/Gallery candidates when a second image has been explicitly selected.
/// 3. Current user order is always authoritative for PDF generation.
/// 4. Preflight may calculate a bounded Smart Flow alternative, but never applies it silently.
/// 5. Residual-space handling is a final polish step only; it cannot change page membership.
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
        => PlanInternal(
            projects,
            printMatter,
            coverStyle,
            strapline,
            hasHandlingMarking,
            includeSmartFlow: false);

    public BrochurePrintCompactPlan PlanWithSmartFlow(
        IReadOnlyList<BrochurePrintPlanningItem> projects,
        BrochurePrintMatter? printMatter,
        BrochureCoverStyle coverStyle,
        string? strapline,
        bool hasHandlingMarking)
        => PlanInternal(
            projects,
            printMatter,
            coverStyle,
            strapline,
            hasHandlingMarking,
            includeSmartFlow: true);

    private BrochurePrintCompactPlan PlanInternal(
        IReadOnlyList<BrochurePrintPlanningItem> projects,
        BrochurePrintMatter? printMatter,
        BrochureCoverStyle coverStyle,
        string? strapline,
        bool hasHandlingMarking,
        bool includeSmartFlow)
    {
        ArgumentNullException.ThrowIfNull(projects);

        printMatter ??= BrochurePrintPublicationPolicy.ApprovedReference;
        var frontPage = _measurement.MeasureFrontPage(printMatter, coverStyle, strapline);
        var closing = _measurement.MeasureClosing(printMatter, strapline);
        var capacity = BrochurePrintLayoutMetrics.ProjectContentCapacity(hasHandlingMarking);

        if (projects.Count == 0)
        {
            return EmptyPlan(frontPage, closing);
        }

        var candidateSets = projects
            .Select((item, index) => CreateCandidateSet(index, item, capacity))
            .ToArray();
        var originalOrder = Enumerable.Range(0, projects.Count).ToArray();
        var current = ComputePlan(
            candidateSets,
            originalOrder,
            frontPage,
            closing,
            capacity,
            includeResidualPolish: true);

        if (!includeSmartFlow || projects.Count < 3)
        {
            return current.Plan;
        }

        var suggestion = FindSmartFlowSuggestion(
            projects,
            candidateSets,
            originalOrder,
            current,
            frontPage,
            closing,
            capacity);

        return current.Plan with { SmartFlowSuggestion = suggestion };
    }

    private ProjectCandidateSet CreateCandidateSet(
        int projectIndex,
        BrochurePrintPlanningItem item,
        float fullSheetCapacity)
    {
        var normal = _measurement.GenerateProjectCandidates(item)
            .Where(candidate => candidate.BodyFontSize >= BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize - .01f)
            .OrderByDescending(candidate => candidate.VisualQualityScore)
            .ThenBy(candidate => candidate.TotalHeightPoints)
            .ToArray();

        if (normal.Length == 0)
        {
            normal = new[] { _measurement.MeasureProject(item, BrochurePrintLayoutVariant.Dense) };
        }

        var requiresEmergency = normal.All(candidate => candidate.TotalHeightPoints > fullSheetCapacity + .5f);
        var emergency = requiresEmergency
            ? _measurement.MeasureProject(item, BrochurePrintLayoutVariant.Compact)
            : null;

        return new ProjectCandidateSet(projectIndex, normal, emergency);
    }

    private static BrochurePrintCompactPlan EmptyPlan(
        BrochurePrintFrontPagePlan frontPage,
        BrochurePrintClosingMeasurement closing)
        => new(
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

    private PlanComputation ComputePlan(
        IReadOnlyList<ProjectCandidateSet> candidateSets,
        IReadOnlyList<int> order,
        BrochurePrintFrontPagePlan frontPage,
        BrochurePrintClosingMeasurement closing,
        float capacity,
        bool includeResidualPolish)
    {
        var orderedSets = order.Select(index => candidateSets[index]).ToArray();

        var hasSharedPlan = TryPlanInternal(
            orderedSets,
            capacity,
            finalClosingHeight: closing.TotalHeightPoints,
            requireClosingOnFinalPage: true,
            out var sharedPages);

        var hasProjectOnlyPlan = TryPlanInternal(
            orderedSets,
            capacity,
            finalClosingHeight: 0f,
            requireClosingOnFinalPage: false,
            out var projectOnlyPages);

        if (!hasProjectOnlyPlan)
        {
            projectOnlyPages = orderedSets
                .Select(set => CreateSingleProjectFallback(set, capacity))
                .ToArray();
        }

        var dedicatedClosing = new PlannedSegment(
            Start: order.Count,
            EndExclusive: order.Count,
            Projects: Array.Empty<BrochurePrintPlannedProject>(),
            ProjectHeightPoints: 0f,
            PhysicalUsedPoints: closing.TotalHeightPoints,
            IncludesClosingMatter: true,
            ClosingHeightPoints: closing.TotalHeightPoints,
            UtilizationPercent: ToPercent(closing.TotalHeightPoints, capacity),
            TypographyPenalty: 0,
            VisualQualityLoss: 0d,
            Score: 0d);

        var dedicated = projectOnlyPages.Concat(new[] { dedicatedClosing }).ToArray();
        IReadOnlyList<PlannedSegment> selected;
        var sharesClosing = false;

        if (hasSharedPlan && ComparePlanAlternatives(sharedPages, dedicated) < 0)
        {
            selected = sharedPages;
            sharesClosing = true;
        }
        else
        {
            selected = dedicated;
        }

        if (includeResidualPolish)
        {
            selected = ApplyResidualPolish(selected, capacity);
        }

        var plan = BuildPlan(
            selected,
            frontPage,
            closing,
            capacity,
            sharesClosing,
            order);

        return new PlanComputation(
            plan,
            selected,
            order.ToArray(),
            AggregateVisualQuality(selected),
            AggregateCompositionScore(selected));
    }

    private static int ComparePlanAlternatives(
        IReadOnlyList<PlannedSegment> left,
        IReadOnlyList<PlannedSegment> right)
    {
        if (left.Count != right.Count)
        {
            return left.Count.CompareTo(right.Count);
        }

        var leftTypography = left.Sum(page => page.TypographyPenalty);
        var rightTypography = right.Sum(page => page.TypographyPenalty);
        if (leftTypography != rightTypography)
        {
            return leftTypography.CompareTo(rightTypography);
        }

        var leftWorst = left.Where(page => page.Projects.Count > 0).Select(page => page.UtilizationPercent).DefaultIfEmpty(100).Min();
        var rightWorst = right.Where(page => page.Projects.Count > 0).Select(page => page.UtilizationPercent).DefaultIfEmpty(100).Min();
        if (leftWorst != rightWorst)
        {
            return rightWorst.CompareTo(leftWorst);
        }

        var leftScore = AggregateCompositionScore(left);
        var rightScore = AggregateCompositionScore(right);
        return leftScore.CompareTo(rightScore);
    }

    private static PlannedSegment CreateSingleProjectFallback(
        ProjectCandidateSet candidates,
        float capacity)
    {
        var measurement = candidates.NormalCandidates
            .OrderByDescending(candidate => candidate.VisualQualityScore)
            .FirstOrDefault(candidate => candidate.TotalHeightPoints <= capacity + .5f)
            ?? candidates.EmergencyCompact
            ?? candidates.NormalCandidates.OrderBy(candidate => candidate.TotalHeightPoints).First();
        var planned = new BrochurePrintPlannedProject(candidates.ProjectIndex, measurement);
        return new PlannedSegment(
            0,
            1,
            new[] { planned },
            measurement.TotalHeightPoints,
            measurement.TotalHeightPoints,
            IncludesClosingMatter: false,
            ClosingHeightPoints: 0f,
            UtilizationPercent: ToPercent(measurement.TotalHeightPoints, capacity),
            TypographyPenalty: TypographyPenalty(measurement),
            VisualQualityLoss: VisualQualityLoss(measurement),
            Score: 0d);
    }

    private static bool TryPlanInternal(
        IReadOnlyList<ProjectCandidateSet> orderedSets,
        float capacity,
        float finalClosingHeight,
        bool requireClosingOnFinalPage,
        out IReadOnlyList<PlannedSegment> pages)
    {
        var count = orderedSets.Count;
        pages = Array.Empty<PlannedSegment>();
        var states = new PlannerState?[count + 1];
        states[0] = new PlannerState(
            PageCount: 0,
            TypographyPenalty: 0,
            WorstResidualFraction: 0d,
            VisualQualityLoss: 0d,
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
                    orderedSets,
                    projectCapacity,
                    closingHeight,
                    capacity,
                    includesClosingMatter);
                if (segment is null)
                {
                    continue;
                }

                var pageCount = state.PageCount + 1;
                var typographyPenalty = state.TypographyPenalty + segment.TypographyPenalty;
                var residualFraction = Math.Max(0d, (capacity - segment.PhysicalUsedPoints) / capacity);
                var worstResidual = Math.Max(state.WorstResidualFraction, residualFraction);
                var visualQualityLoss = state.VisualQualityLoss + segment.VisualQualityLoss;
                var score = state.Score + segment.Score;
                var existing = states[end];

                var candidateState = new PlannerState(
                    pageCount,
                    typographyPenalty,
                    worstResidual,
                    visualQualityLoss,
                    score,
                    start,
                    segment);

                if (existing is null || ComparePlannerState(candidateState, existing) < 0)
                {
                    states[end] = candidateState;
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

    private static int ComparePlannerState(PlannerState left, PlannerState right)
    {
        if (left.PageCount != right.PageCount)
        {
            return left.PageCount.CompareTo(right.PageCount);
        }
        if (left.TypographyPenalty != right.TypographyPenalty)
        {
            return left.TypographyPenalty.CompareTo(right.TypographyPenalty);
        }
        if (Math.Abs(left.WorstResidualFraction - right.WorstResidualFraction) > .0001d)
        {
            return left.WorstResidualFraction.CompareTo(right.WorstResidualFraction);
        }
        if (Math.Abs(left.Score - right.Score) > .0001d)
        {
            return left.Score.CompareTo(right.Score);
        }
        return left.VisualQualityLoss.CompareTo(right.VisualQualityLoss);
    }

    private static PlannedSegment? FindBestSegment(
        int start,
        int endExclusive,
        IReadOnlyList<ProjectCandidateSet> orderedSets,
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
                var typographyPenalty = selected.Sum(TypographyPenalty);
                var qualityLoss = selected.Sum(VisualQualityLoss);
                var singleProjectPenalty = itemCount == 1 ? 8d : 0d;
                var residual = Math.Max(0d, 1d - utilization);

                // Underfill remains the dominant within-segment consideration. Visual quality then
                // breaks near-equal geometry choices so a roomy sheet naturally selects larger,
                // more comfortable candidates without a post-pagination image expansion pass.
                var score = (underfill * underfill * 5200d)
                            + (residual * residual * 900d)
                            + (preferredDelta * preferredDelta * 90d)
                            + (qualityLoss * .75d)
                            + singleProjectPenalty;

                var projects = selected
                    .Select((measurement, offsetIndex) => new BrochurePrintPlannedProject(
                        orderedSets[start + offsetIndex].ProjectIndex,
                        measurement))
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
                    qualityLoss,
                    score);

                if (best is null || CompareSegments(candidate, best) < 0)
                {
                    best = candidate;
                }
                return;
            }

            var set = orderedSets[start + offset];
            foreach (var candidate in set.CandidatesForSegment(itemCount, physicalCapacity))
            {
                selected[offset] = candidate;
                Search(offset + 1);
            }
        }

        Search(0);
        return best;
    }

    private static int CompareSegments(PlannedSegment left, PlannedSegment right)
    {
        if (left.TypographyPenalty != right.TypographyPenalty)
        {
            return left.TypographyPenalty.CompareTo(right.TypographyPenalty);
        }
        if (Math.Abs(left.Score - right.Score) > .0001d)
        {
            return left.Score.CompareTo(right.Score);
        }
        return left.VisualQualityLoss.CompareTo(right.VisualQualityLoss);
    }

    private static IReadOnlyList<PlannedSegment> ApplyResidualPolish(
        IReadOnlyList<PlannedSegment> pages,
        float capacity)
    {
        var result = new List<PlannedSegment>(pages.Count);
        var targetPhysicalUsed = capacity * BrochurePrintLayoutMetrics.ResidualTargetUtilization;

        foreach (var page in pages)
        {
            if (page.Projects.Count == 0 || page.PhysicalUsedPoints >= targetPhysicalUsed - .5f)
            {
                result.Add(page);
                continue;
            }

            var currentProjectHeight = page.ProjectHeightPoints;
            var currentPhysicalUsed = page.PhysicalUsedPoints;
            var remainingToTarget = Math.Max(0f, targetPhysicalUsed - currentPhysicalUsed);
            var extraModuleVerticalPadding = 0f;
            var extraInterModuleSpacing = 0f;

            if (remainingToTarget > .5f)
            {
                extraModuleVerticalPadding = Math.Min(
                    BrochurePrintLayoutMetrics.ResidualMaximumExtraModuleVerticalPaddingPoints,
                    remainingToTarget / page.Projects.Count);
                var moduleDelta = extraModuleVerticalPadding * page.Projects.Count;
                currentProjectHeight += moduleDelta;
                currentPhysicalUsed += moduleDelta;
                remainingToTarget = Math.Max(0f, targetPhysicalUsed - currentPhysicalUsed);
            }

            if (remainingToTarget > .5f && page.Projects.Count > 1)
            {
                extraInterModuleSpacing = Math.Min(
                    BrochurePrintLayoutMetrics.ResidualMaximumExtraInterModuleSpacingPoints,
                    remainingToTarget / (page.Projects.Count - 1));
                var spacingDelta = extraInterModuleSpacing * (page.Projects.Count - 1);
                currentProjectHeight += spacingDelta;
                currentPhysicalUsed += spacingDelta;
            }

            result.Add(page with
            {
                ProjectHeightPoints = currentProjectHeight,
                PhysicalUsedPoints = currentPhysicalUsed,
                UtilizationPercent = ToPercent(currentPhysicalUsed, capacity),
                ExtraModuleVerticalPaddingPoints = extraModuleVerticalPadding,
                ExtraInterModuleSpacingPoints = extraInterModuleSpacing
            });
        }

        return result;
    }

    private BrochurePrintFlowSuggestion? FindSmartFlowSuggestion(
        IReadOnlyList<BrochurePrintPlanningItem> projects,
        IReadOnlyList<ProjectCandidateSet> candidateSets,
        IReadOnlyList<int> originalOrder,
        PlanComputation current,
        BrochurePrintFrontPagePlan frontPage,
        BrochurePrintClosingMeasurement closing,
        float capacity)
    {
        var originalPositions = originalOrder
            .Select((projectIndex, position) => (projectIndex, position))
            .ToDictionary(pair => pair.projectIndex, pair => pair.position);
        var best = new FlowSearchState(
            originalOrder.ToArray(),
            current,
            Array.Empty<AppliedMove>());
        var frontier = new List<FlowSearchState> { best };
        var seen = new HashSet<string>(StringComparer.Ordinal) { OrderKey(originalOrder) };

        var maximumPasses = projects.Count > 24
            ? 1
            : BrochurePrintLayoutMetrics.SmartFlowMaximumPasses;
        var beamWidth = projects.Count > 24
            ? 1
            : BrochurePrintLayoutMetrics.SmartFlowBeamWidth;
        var maximumMoves = projects.Count > 24
            ? 6
            : BrochurePrintLayoutMetrics.SmartFlowMaximumBoundaryMovesPerState;

        for (var pass = 0; pass < maximumPasses; pass++)
        {
            var next = new List<FlowSearchState>();
            foreach (var state in frontier)
            {
                foreach (var move in CandidateMoves(state.Order, state.Computation.Plan).Take(maximumMoves))
                {
                    var reordered = Move(state.Order, move.FromPosition, move.ToPosition);
                    var key = OrderKey(reordered);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    var computation = ComputePlan(
                        candidateSets,
                        reordered,
                        frontPage,
                        closing,
                        capacity,
                        includeResidualPolish: false);
                    var operations = state.Operations.Concat(new[]
                    {
                        new AppliedMove(
                            state.Order[move.FromPosition],
                            move.FromPosition,
                            move.ToPosition)
                    }).ToArray();
                    next.Add(new FlowSearchState(reordered, computation, operations));
                }
            }

            if (next.Count == 0)
            {
                break;
            }

            frontier = next
                .OrderBy(state => FlowScore(state, originalPositions))
                .ThenBy(state => TotalPositionShift(state.Order, originalPositions))
                .Take(beamWidth)
                .ToList();

            var passBest = frontier[0];
            if (CompareFlow(passBest, best, originalPositions) < 0)
            {
                best = passBest;
            }
        }

        if (best.Order.SequenceEqual(originalOrder)
            || !IsMaterialImprovement(current.Plan, best.Computation.Plan))
        {
            return null;
        }

        // Recompute with final residual polish so the suggestion shown in UI is exactly the plan
        // that will be obtained after the user applies this order and preflight runs again.
        var polished = ComputePlan(
            candidateSets,
            best.Order,
            frontPage,
            closing,
            capacity,
            includeResidualPolish: true);
        var moves = BuildUserFacingMoves(projects, originalOrder, best.Order, best.Operations);
        var currentLowest = current.Plan.LowestProjectPageUtilizationPercent ?? 100;
        var suggestedLowest = polished.Plan.LowestProjectPageUtilizationPercent ?? 100;
        var saved = Math.Max(0, current.Plan.EstimatedTotalPageCount - polished.Plan.EstimatedTotalPageCount);
        var selectedProjects = polished.Plan.Pages.SelectMany(page => page.Projects).ToArray();
        var denseProjectCount = selectedProjects.Count(project =>
            project.Measurement.Variant == BrochurePrintLayoutVariant.Dense);
        var automaticSingleProjectCount = selectedProjects.Count(project =>
        {
            var source = projects[project.ProjectIndex];
            return source.ImageMode == BrochureImageMode.Automatic
                   && source.HasSecondaryPhoto
                   && !project.Measurement.UsesSecondaryImage;
        });
        var minimumImageWidth = selectedProjects
            .Where(project => project.Measurement.ImageWidthPoints > .5f)
            .Select(project => project.Measurement.ImageWidthPoints)
            .DefaultIfEmpty(0f)
            .Min();
        var treatmentParts = new List<string>();
        if (denseProjectCount > 0)
        {
            treatmentParts.Add($"Dense 9 pt geometry on {denseProjectCount} project{(denseProjectCount == 1 ? string.Empty : "s")}");
        }
        if (automaticSingleProjectCount > 0)
        {
            treatmentParts.Add($"Automatic single-image treatment on {automaticSingleProjectCount} project{(automaticSingleProjectCount == 1 ? string.Empty : "s")}");
        }
        if (minimumImageWidth > .5f)
        {
            treatmentParts.Add($"minimum printed image width {Math.Round(minimumImageWidth):0} pt");
        }
        var adaptiveTreatmentSummary = treatmentParts.Count == 0
            ? "Reference 9 pt geometry; no additional compaction required."
            : string.Join(" · ", treatmentParts) + ".";
        var summary = saved > 0
            ? $"A {polished.Plan.EstimatedTotalPageCount}-sheet composition is available at 9 pt with {moves.Count} order change{(moves.Count == 1 ? string.Empty : "s")}. No project is removed."
            : $"A better-balanced composition is available at 9 pt with {moves.Count} order change{(moves.Count == 1 ? string.Empty : "s")}.";

        return new BrochurePrintFlowSuggestion(
            SuggestedProjectIds: best.Order.Select(index => projects[index].ProjectId).ToArray(),
            CurrentPageCount: current.Plan.EstimatedTotalPageCount,
            SuggestedPageCount: polished.Plan.EstimatedTotalPageCount,
            CurrentLowestProjectUtilizationPercent: currentLowest,
            SuggestedLowestProjectUtilizationPercent: suggestedLowest,
            CurrentAverageUtilizationPercent: current.Plan.AverageContentUtilizationPercent,
            SuggestedAverageUtilizationPercent: polished.Plan.AverageContentUtilizationPercent,
            MovedProjectCount: moves.Count,
            TotalPositionShift: TotalPositionShift(best.Order, originalPositions),
            DenseProjectCount: denseProjectCount,
            AutomaticSingleProjectCount: automaticSingleProjectCount,
            MinimumImageWidthPoints: (int)Math.Round(minimumImageWidth),
            AdaptiveTreatmentSummary: adaptiveTreatmentSummary,
            Moves: moves,
            SuggestedSheetPlan: polished.Plan.SheetPlan,
            Summary: summary);
    }

    private static IEnumerable<MoveCandidate> CandidateMoves(
        IReadOnlyList<int> order,
        BrochurePrintCompactPlan plan)
    {
        var positions = order
            .Select((projectIndex, position) => (projectIndex, position))
            .ToDictionary(pair => pair.projectIndex, pair => pair.position);
        var moves = new List<MoveCandidate>();

        var projectPages = plan.Pages.Where(page => page.Projects.Count > 0).ToArray();
        for (var pageIndex = 0; pageIndex < projectPages.Length - 1; pageIndex++)
        {
            var page = projectPages[pageIndex];
            var next = projectPages[pageIndex + 1];
            if (next.Projects.Count == 0)
            {
                continue;
            }

            var lastCurrent = positions[page.Projects[^1].ProjectIndex];
            var firstNext = positions[next.Projects[0].ProjectIndex];

            var boundaryPriority = (200 - Math.Min(page.UtilizationPercent, next.UtilizationPercent))
                                   + (pageIndex == projectPages.Length - 2 ? 45 : 0)
                                   + (next.IncludesClosingMatter ? 80 : 0);

            // Pull the first project from the next sheet backwards across the boundary. This is
            // the most valuable local move when a prior sheet can become four-up at Dense 9 pt.
            for (var distance = 1; distance <= BrochurePrintLayoutMetrics.SmartFlowMaximumMoveDistance; distance++)
            {
                var target = firstNext - distance;
                if (target < 0)
                {
                    break;
                }
                moves.Add(new MoveCandidate(firstNext, target, boundaryPriority - distance));
            }

            // Also test pushing the last project forward. This can pair two medium projects with
            // closing matter without disturbing the broader editorial sequence.
            for (var distance = 1; distance <= BrochurePrintLayoutMetrics.SmartFlowMaximumMoveDistance; distance++)
            {
                var target = lastCurrent + distance;
                if (target >= order.Count)
                {
                    break;
                }
                moves.Add(new MoveCandidate(lastCurrent, target, boundaryPriority - distance - 2));
            }
        }

        // If the final project sheet is conspicuously sparse, test local movement around its start
        // even when the previous sheet is not itself below the normal utilisation threshold.
        var finalProjects = projectPages.LastOrDefault();
        if (finalProjects is not null && finalProjects.UtilizationPercent < 85 && finalProjects.Projects.Count > 0)
        {
            var first = positions[finalProjects.Projects[0].ProjectIndex];
            for (var distance = 1; distance <= BrochurePrintLayoutMetrics.SmartFlowMaximumMoveDistance; distance++)
            {
                var target = first - distance;
                if (target < 0)
                {
                    break;
                }
                moves.Add(new MoveCandidate(first, target, 260 - distance));
            }
        }

        return moves
            .Where(move => move.FromPosition != move.ToPosition)
            .GroupBy(move => (move.FromPosition, move.ToPosition))
            .Select(group => group.OrderByDescending(move => move.Priority).First())
            .OrderByDescending(move => move.Priority)
            .ThenBy(move => Math.Abs(move.ToPosition - move.FromPosition))
            .Take(BrochurePrintLayoutMetrics.SmartFlowMaximumBoundaryMovesPerState)
            .ToArray();
    }

    private static int CompareFlow(
        FlowSearchState left,
        FlowSearchState right,
        IReadOnlyDictionary<int, int> originalPositions)
    {
        var scoreComparison = FlowScore(left, originalPositions).CompareTo(FlowScore(right, originalPositions));
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }
        return TotalPositionShift(left.Order, originalPositions)
            .CompareTo(TotalPositionShift(right.Order, originalPositions));
    }

    private static double FlowScore(
        FlowSearchState state,
        IReadOnlyDictionary<int, int> originalPositions)
    {
        var plan = state.Computation.Plan;
        var lowest = plan.LowestProjectPageUtilizationPercent ?? 100;
        var displacement = TotalPositionShift(state.Order, originalPositions);

        // One saved physical sheet must dominate all secondary concerns. Thereafter prefer a
        // stronger worst sheet, higher average fill and the smallest possible editorial change.
        return (plan.EstimatedTotalPageCount * 1_000_000d)
               + ((100 - lowest) * 4_000d)
               + ((100 - plan.AverageContentUtilizationPercent) * 700d)
               + (displacement * 90d)
               - (state.Computation.VisualQuality * .2d);
    }

    private static bool IsMaterialImprovement(
        BrochurePrintCompactPlan current,
        BrochurePrintCompactPlan suggestion)
    {
        if (suggestion.EstimatedTotalPageCount < current.EstimatedTotalPageCount)
        {
            return true;
        }
        if (suggestion.EstimatedTotalPageCount > current.EstimatedTotalPageCount)
        {
            return false;
        }

        var currentLowest = current.LowestProjectPageUtilizationPercent ?? 100;
        var suggestedLowest = suggestion.LowestProjectPageUtilizationPercent ?? 100;
        var fillGain = suggestedLowest - currentLowest;
        var averageGain = suggestion.AverageContentUtilizationPercent - current.AverageContentUtilizationPercent;
        return fillGain >= BrochurePrintLayoutMetrics.SmartFlowMinimumFillImprovementPercent
               && averageGain >= BrochurePrintLayoutMetrics.SmartFlowMinimumAverageImprovementPercent;
    }

    private static IReadOnlyList<BrochurePrintOrderMove> BuildUserFacingMoves(
        IReadOnlyList<BrochurePrintPlanningItem> projects,
        IReadOnlyList<int> originalOrder,
        IReadOnlyList<int> suggestedOrder,
        IReadOnlyList<AppliedMove> operations)
    {
        var originalPosition = originalOrder
            .Select((projectIndex, position) => (projectIndex, position))
            .ToDictionary(pair => pair.projectIndex, pair => pair.position);
        var suggestedPosition = suggestedOrder
            .Select((projectIndex, position) => (projectIndex, position))
            .ToDictionary(pair => pair.projectIndex, pair => pair.position);

        var explicitProjects = operations
            .Select(move => move.ProjectIndex)
            .Distinct()
            .Where(projectIndex => originalPosition[projectIndex] != suggestedPosition[projectIndex])
            .ToArray();

        // Search operations are a compact representation of what the optimiser intentionally
        // moved. Projects shifted incidentally by an insertion are not reported as separate user
        // actions.
        return explicitProjects
            .Select(projectIndex => new BrochurePrintOrderMove(
                projects[projectIndex].ProjectId,
                projects[projectIndex].ProjectName,
                originalPosition[projectIndex] + 1,
                suggestedPosition[projectIndex] + 1))
            .OrderBy(move => move.FromOrdinal)
            .ToArray();
    }

    private static int[] Move(IReadOnlyList<int> source, int from, int to)
    {
        var result = source.ToList();
        var item = result[from];
        result.RemoveAt(from);
        result.Insert(to, item);
        return result.ToArray();
    }

    private static string OrderKey(IReadOnlyList<int> order)
        => string.Join(',', order);

    private static int TotalPositionShift(
        IReadOnlyList<int> order,
        IReadOnlyDictionary<int, int> originalPositions)
        => order.Select((projectIndex, position) => Math.Abs(position - originalPositions[projectIndex])).Sum();

    private static double AggregateVisualQuality(IEnumerable<PlannedSegment> pages)
        => pages.SelectMany(page => page.Projects).Sum(project => project.Measurement.VisualQualityScore);

    private static double AggregateCompositionScore(IEnumerable<PlannedSegment> pages)
        => pages.Sum(page => page.Score);

    private static int TypographyPenalty(BrochurePrintProjectMeasurement measurement)
    {
        if (measurement.BodyFontSize >= BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize - .01f)
        {
            return 0;
        }

        var reduction = BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize - measurement.BodyFontSize;
        return 1 + Math.Max(0, (int)Math.Round(reduction * 4f));
    }

    private static double VisualQualityLoss(BrochurePrintProjectMeasurement measurement)
        => Math.Max(0d, 110d - measurement.VisualQualityScore);

    private static int ToPercent(float used, float capacity)
        => capacity <= 0f
            ? 0
            : Math.Clamp((int)Math.Round(Math.Min(1d, used / capacity) * 100d), 0, 100);

    private static BrochurePrintCompactPlan BuildPlan(
        IReadOnlyList<PlannedSegment> segments,
        BrochurePrintFrontPagePlan frontPage,
        BrochurePrintClosingMeasurement closing,
        float capacity,
        bool sharesClosingMatter,
        IReadOnlyList<int> order)
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
        var ordinalByProject = order
            .Select((projectIndex, ordinal) => (projectIndex, ordinal: ordinal + 1))
            .ToDictionary(pair => pair.projectIndex, pair => pair.ordinal);

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
            var ordinals = page.Projects
                .Select(project => ordinalByProject.GetValueOrDefault(project.ProjectIndex))
                .Where(ordinal => ordinal > 0)
                .OrderBy(ordinal => ordinal)
                .ToArray();
            var first = ordinals.Length > 0 ? ordinals[0] : (int?)null;
            var last = ordinals.Length > 0 ? ordinals[^1] : (int?)null;
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

    private sealed record ProjectCandidateSet(
        int ProjectIndex,
        IReadOnlyList<BrochurePrintProjectMeasurement> NormalCandidates,
        BrochurePrintProjectMeasurement? EmergencyCompact)
    {
        public IEnumerable<BrochurePrintProjectMeasurement> CandidatesForSegment(
            int itemCount,
            float fullSheetCapacity)
        {
            foreach (var candidate in NormalCandidates)
            {
                yield return candidate;
            }

            if (itemCount == 1
                && EmergencyCompact is not null
                && NormalCandidates.All(candidate => candidate.TotalHeightPoints > fullSheetCapacity + .5f))
            {
                yield return EmergencyCompact;
            }
        }
    }

    private sealed record PlannerState(
        int PageCount,
        int TypographyPenalty,
        double WorstResidualFraction,
        double VisualQualityLoss,
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
        double VisualQualityLoss,
        double Score,
        float ExtraModuleVerticalPaddingPoints = 0f,
        float ExtraInterModuleSpacingPoints = 0f);

    private sealed record PlanComputation(
        BrochurePrintCompactPlan Plan,
        IReadOnlyList<PlannedSegment> Segments,
        IReadOnlyList<int> Order,
        double VisualQuality,
        double CompositionScore);

    private sealed record FlowSearchState(
        IReadOnlyList<int> Order,
        PlanComputation Computation,
        IReadOnlyList<AppliedMove> Operations);

    private sealed record AppliedMove(
        int ProjectIndex,
        int FromPosition,
        int ToPosition);

    private sealed record MoveCandidate(int FromPosition, int ToPosition, int Priority);
}
