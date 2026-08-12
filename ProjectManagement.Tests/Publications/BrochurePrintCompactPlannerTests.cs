using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochurePrintCompactPlannerTests
{
    [Fact]
    public void Plan_DenseNinePointCandidates_EnableFourUpWithoutReducingTypography()
    {
        var planner = new BrochurePrintPagePlanner(new AdaptiveFixtureMeasurementService());
        var projects = Enumerable.Range(1, 4)
            .Select(index => PlanningItem(index, BrochureImageMode.Automatic))
            .ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            "Simulators of the Army, by the Army, for the Army",
            hasHandlingMarking: false);

        var projectPage = Assert.Single(plan.Pages.Where(page => page.Projects.Count > 0));
        Assert.Equal(4, projectPage.Projects.Count);
        Assert.All(projectPage.Projects, project =>
        {
            Assert.True(project.Measurement.BodyFontSize >= 8.99f);
            Assert.NotEqual(BrochurePrintLayoutVariant.Compact, project.Measurement.Variant);
        });
        Assert.Contains(projectPage.Projects, project =>
            project.Measurement.Variant == BrochurePrintLayoutVariant.Dense);
    }


    [Fact]
    public void Plan_NineProjectStressFixture_UsesDenseNinePointGeometryBeforeAddingAProjectSheet()
    {
        var planner = new BrochurePrintPagePlanner(new NineProjectStressMeasurementService());
        var projects = Enumerable.Range(1, 9).Select(PlanningItem).ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        // Front + three content sheets is the target class for the current 9-project regression.
        Assert.Equal(4, plan.EstimatedTotalPageCount);
        Assert.Equal(3, plan.Pages.Count(page => page.Projects.Count > 0));
        Assert.Contains(plan.Pages, page => page.Projects.Count == 4);
        Assert.All(plan.Pages.SelectMany(page => page.Projects), project =>
            Assert.Equal(9f, project.Measurement.BodyFontSize));
        Assert.True(plan.ClosingMatterSharesFinalPage);
    }

    [Fact]
    public void Plan_PreservesEditorialOrderExactly()
    {
        var planner = new BrochurePrintPagePlanner(new AdaptiveFixtureMeasurementService());
        var projects = new[]
        {
            PlanningItem(41), PlanningItem(7), PlanningItem(29), PlanningItem(3), PlanningItem(18)
        };

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        var plannedProjectIds = plan.Pages
            .SelectMany(page => page.Projects)
            .Select(project => projects[project.ProjectIndex].ProjectId)
            .ToArray();

        Assert.Equal(projects.Select(project => project.ProjectId), plannedProjectIds);
        Assert.Null(plan.SmartFlowSuggestion);
    }

    [Fact]
    public void PlanWithSmartFlow_ReturnsSuggestionWithoutMutatingCurrentPlan()
    {
        var service = new SmartFlowFixtureMeasurementService();
        var planner = new BrochurePrintPagePlanner(service);
        var projects = new[]
        {
            PlanningItem(1), PlanningItem(2), PlanningItem(3), PlanningItem(4), PlanningItem(5)
        };

        var plan = planner.PlanWithSmartFlow(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        // Current PDF order remains authoritative even when a better flow exists.
        var currentIds = plan.Pages
            .SelectMany(page => page.Projects)
            .Select(project => projects[project.ProjectIndex].ProjectId)
            .ToArray();
        Assert.Equal(projects.Select(project => project.ProjectId), currentIds);

        var suggestion = Assert.IsType<BrochurePrintFlowSuggestion>(plan.SmartFlowSuggestion);
        Assert.True(suggestion.SuggestedPageCount < suggestion.CurrentPageCount);
        Assert.True(suggestion.MovedProjectCount >= 1);
        Assert.NotEqual(projects.Select(project => project.ProjectId), suggestion.SuggestedProjectIds);
        Assert.All(suggestion.Moves, move =>
            Assert.InRange(
                Math.Abs(move.ToOrdinal - move.FromOrdinal),
                1,
                BrochurePrintLayoutMetrics.SmartFlowMaximumMoveDistance));
    }

    [Fact]
    public void PlanWithSmartFlow_DoesNotSuggestCosmeticReorderWithoutMaterialGain()
    {
        var planner = new BrochurePrintPagePlanner(new UniformFixtureMeasurementService());
        var projects = Enumerable.Range(1, 6).Select(PlanningItem).ToArray();

        var plan = planner.PlanWithSmartFlow(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        Assert.Null(plan.SmartFlowSuggestion);
    }

    [Fact]
    public void Plan_AutomaticImageModeMayChooseSingleImageToProtectPageFit()
    {
        var planner = new BrochurePrintPagePlanner(new AutomaticImageFixtureMeasurementService());
        var projects = Enumerable.Range(1, 4)
            .Select(index => PlanningItem(index, BrochureImageMode.Automatic, hasSecondary: true))
            .ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        var projectPage = Assert.Single(plan.Pages.Where(page => page.Projects.Count > 0));
        Assert.Equal(4, projectPage.Projects.Count);
        Assert.Contains(projectPage.Projects, project => !project.Measurement.UsesSecondaryImage);
        Assert.All(projectPage.Projects, project => Assert.Equal(9f, project.Measurement.BodyFontSize));
    }

    [Fact]
    public void Plan_ExplicitGalleryTwoNeverDropsSelectedSecondImage()
    {
        var planner = new BrochurePrintPagePlanner(new AutomaticImageFixtureMeasurementService());
        var projects = Enumerable.Range(1, 3)
            .Select(index => PlanningItem(index, BrochureImageMode.GalleryTwo, hasSecondary: true))
            .ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        Assert.All(
            plan.Pages.SelectMany(page => page.Projects),
            project => Assert.True(project.Measurement.UsesSecondaryImage));
    }

    [Fact]
    public void Plan_CompactEightPointFiveIsEmergencyOnlyForOversizeSingleProject()
    {
        var planner = new BrochurePrintPagePlanner(new EmergencyFixtureMeasurementService());
        var normalProjects = new[] { PlanningItem(1), PlanningItem(2) };
        var normalPlan = planner.Plan(
            normalProjects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        Assert.All(normalPlan.Pages.SelectMany(page => page.Projects), project =>
            Assert.Equal(9f, project.Measurement.BodyFontSize));

        var oversizePlan = planner.Plan(
            new[] { PlanningItem(99) },
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);
        var oversize = Assert.Single(oversizePlan.Pages.SelectMany(page => page.Projects));
        Assert.Equal(BrochurePrintLayoutVariant.Compact, oversize.Measurement.Variant);
        Assert.Equal(8.5f, oversize.Measurement.BodyFontSize);
    }

    [Fact]
    public void ResidualPolish_DoesNotChangePageMembershipOrderOrMeasuredImageWidth()
    {
        var planner = new BrochurePrintPagePlanner(new UniformFixtureMeasurementService());
        var projects = Enumerable.Range(1, 5).Select(PlanningItem).ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        var planned = plan.Pages.SelectMany(page => page.Projects).ToArray();
        Assert.Equal(Enumerable.Range(0, 5), planned.Select(project => project.ProjectIndex));
        Assert.All(planned, project => Assert.Equal(148f, project.Measurement.ImageWidthPoints));
        Assert.All(plan.Pages, page =>
            Assert.True(page.ExtraModuleVerticalPaddingPoints >= 0f
                        && page.ExtraInterModuleSpacingPoints >= 0f));
        var finalPage = Assert.IsType<BrochurePrintCompactPage>(plan.Pages.Last());
        Assert.Equal(0f, finalPage.ExtraModuleVerticalPaddingPoints);
        Assert.Equal(0f, finalPage.ExtraInterModuleSpacingPoints);
    }

    [Fact]
    public void Plan_PacksNonFinalSheetsForwardAndLeavesResidualOnFinalSheet()
    {
        var planner = new BrochurePrintPagePlanner(new UniformFixtureMeasurementService());
        var projects = Enumerable.Range(1, 5).Select(PlanningItem).ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        Assert.Equal(2, plan.Pages.Count);
        Assert.Equal(4, plan.Pages[0].Projects.Count);
        Assert.Single(plan.Pages[1].Projects);
        Assert.True(plan.Pages[1].IncludesClosingMatter);
        Assert.True(plan.Pages[0].UtilizationPercent > plan.Pages[1].UtilizationPercent);
        Assert.Equal(plan.Pages[0].UtilizationPercent, plan.LowestProjectPageUtilizationPercent!.Value);
        Assert.Equal(plan.Pages[1].UtilizationPercent, plan.FinalPageUtilizationPercent!.Value);
        Assert.Equal(0f, plan.Pages[1].ExtraModuleVerticalPaddingPoints);
        Assert.Equal(0f, plan.Pages[1].ExtraInterModuleSpacingPoints);

        var finalSummary = Assert.Single(plan.SheetPlan.Where(sheet => sheet.IsFinal));
        Assert.Equal(plan.EstimatedTotalPageCount, finalSummary.SheetNumber);
        Assert.Contains("residual allowed", finalSummary.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_AllowsMeasuredFiveUpSheetWithoutReducingTypography()
    {
        var planner = new BrochurePrintPagePlanner(new FiveUpFixtureMeasurementService());
        var projects = Enumerable.Range(1, 5).Select(PlanningItem).ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            null,
            hasHandlingMarking: false);

        var fiveUp = Assert.Single(plan.Pages.Where(page => page.Projects.Count == 5));
        Assert.All(fiveUp.Projects, project => Assert.Equal(9f, project.Measurement.BodyFontSize));
        Assert.True(fiveUp.MeasuredPhysicalUsedPoints <= fiveUp.CapacityPoints + .5f);
        Assert.Equal(BrochurePrintLayoutMetrics.MaximumProjectsPerSheet, fiveUp.Projects.Count);
    }

    private static BrochurePrintPlanningItem PlanningItem(
        int id,
        BrochureImageMode imageMode = BrochureImageMode.Automatic,
        bool hasSecondary = false)
        => new(
            id,
            $"Project {id}",
            "A concise publication narrative used for deterministic pagination tests. "
            + "It contains enough words to represent a normal project brief while keeping the fixture stable.",
            imageMode,
            HasPrimaryPhoto: true,
            HasSecondaryPhoto: hasSecondary);

    private abstract class FixtureMeasurementService : IBrochurePrintMeasurementService
    {
        public abstract IReadOnlyList<BrochurePrintProjectMeasurement> GenerateProjectCandidates(
            BrochurePrintPlanningItem item);

        public virtual BrochurePrintProjectMeasurement MeasureProject(
            BrochurePrintPlanningItem item,
            BrochurePrintLayoutVariant variant,
            float imageWidthAdjustmentPoints = 0f)
        {
            var useSecond = item.ImageMode == BrochureImageMode.GalleryTwo && item.HasSecondaryPhoto;
            var spec = BrochurePrintLayoutMetrics.VariantSpec(
                variant,
                variant == BrochurePrintLayoutVariant.Compact ? 128f : 140f,
                useSecond);
            return MeasureProject(item, spec);
        }

        public virtual BrochurePrintProjectMeasurement MeasureProject(
            BrochurePrintPlanningItem item,
            BrochurePrintVariantSpec spec)
            => Measurement(item.ProjectId, spec, HeightFor(item, spec));

        protected virtual float HeightFor(BrochurePrintPlanningItem item, BrochurePrintVariantSpec spec)
            => spec.Variant switch
            {
                BrochurePrintLayoutVariant.Visual => 225f,
                BrochurePrintLayoutVariant.Balanced => 212f,
                BrochurePrintLayoutVariant.Dense => 198f,
                _ => 188f
            };

        public virtual BrochurePrintClosingMeasurement MeasureClosing(BrochurePrintMatter? matter, string? strapline)
            => new(240f, 180f, 60f, 0f);

        public virtual BrochurePrintFrontPagePlan MeasureFrontPage(
            BrochurePrintMatter? matter,
            BrochureCoverStyle coverStyle,
            string? strapline)
            => new(
                Fits: true,
                HeroHeightPoints: 320f,
                CentreBlockHeightPoints: 44f,
                CentreFontSize: 11.2f,
                BodyBlockHeightPoints: 340f,
                BodyFontSize: 9f,
                BodyLineHeight: 1.07f,
                BodySpacingPoints: 6f,
                ContactBlockHeightPoints: 120f,
                ContactFontSize: 8.5f,
                StraplineHeightPoints: 22f,
                TotalUsedHeightPoints: BrochurePrintLayoutMetrics.ReferenceHeightPoints,
                UtilizationPercent: 100,
                UsesMinimumTypography: false,
                CoverStyle: coverStyle);

        protected static BrochurePrintProjectMeasurement Measurement(
            int projectId,
            BrochurePrintVariantSpec spec,
            float height)
            => new(
                ProjectId: projectId,
                Variant: spec.Variant,
                TotalHeightPoints: height,
                TitleHeightPoints: spec.TitleMinimumHeightPoints,
                TitleFontSize: spec.TitleFontSize,
                BodyFontSize: spec.BodyFontSize,
                BodyLineHeight: spec.BodyLineHeight,
                ImageWidthPoints: spec.ImageWidthPoints,
                BodyPaddingPoints: spec.BodyPaddingPoints,
                TextWidthPoints: 245f,
                TextHeightPoints: Math.Max(40f, height - spec.TitleMinimumHeightPoints - 14f),
                ImageHeightPoints: spec.ImageWidthPoints / BrochurePrintLayoutMetrics.SingleImageAspectRatio,
                QualityRank: spec.QualityRank,
                LeadingNarrative: "A concise publication narrative.",
                TrailingNarrative: string.Empty,
                LeadingTextHeightPoints: 55f,
                TrailingTextHeightPoints: 0f,
                FullTextWidthPoints: 390f,
                PrimaryImageHeightPoints: spec.ImageWidthPoints / BrochurePrintLayoutMetrics.SingleImageAspectRatio,
                SecondaryImageHeightPoints: spec.UseSecondaryImage
                    ? spec.ImageWidthPoints / BrochurePrintLayoutMetrics.GalleryImageAspectRatio
                    : 0f,
                UsesFloatLayout: true,
                ContinuationNarrative: string.Empty,
                ContinuationTextHeightPoints: 0f,
                FloatSplitKind: BrochureFloatSplitKind.Sentence,
                RemainderGapPoints: 1f,
                ParagraphSpacingPoints: spec.ParagraphSpacingPoints,
                UsesSecondaryImage: spec.UseSecondaryImage,
                VisualQualityScore: spec.VisualQualityScore,
                TitleMinimumHeightPoints: spec.TitleMinimumHeightPoints);
    }

    private sealed class AdaptiveFixtureMeasurementService : FixtureMeasurementService
    {
        public override IReadOnlyList<BrochurePrintProjectMeasurement> GenerateProjectCandidates(
            BrochurePrintPlanningItem item)
            => new[]
            {
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Visual, 156f), 225f),
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Balanced, 148f), 212f),
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 136f), 198f)
            };
    }


    private sealed class NineProjectStressMeasurementService : FixtureMeasurementService
    {
        private static readonly float[] DenseHeights = { 180f, 180f, 190f, 220f, 180f, 180f, 200f, 225f, 235f };

        public override IReadOnlyList<BrochurePrintProjectMeasurement> GenerateProjectCandidates(
            BrochurePrintPlanningItem item)
        {
            var index = Math.Clamp(item.ProjectId - 1, 0, DenseHeights.Length - 1);
            var denseHeight = DenseHeights[index];
            return new[]
            {
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Visual, 156f), denseHeight + 34f),
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Balanced, 148f), denseHeight + 16f),
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 136f), denseHeight)
            };
        }

        public override BrochurePrintClosingMeasurement MeasureClosing(BrochurePrintMatter? matter, string? strapline)
            => new(260f, 195f, 65f, 0f);
    }

    private sealed class UniformFixtureMeasurementService : FixtureMeasurementService
    {
        public override IReadOnlyList<BrochurePrintProjectMeasurement> GenerateProjectCandidates(
            BrochurePrintPlanningItem item)
            => new[]
            {
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Balanced, 148f), 205f)
            };
    }

    private sealed class FiveUpFixtureMeasurementService : FixtureMeasurementService
    {
        public override IReadOnlyList<BrochurePrintProjectMeasurement> GenerateProjectCandidates(
            BrochurePrintPlanningItem item)
            => new[]
            {
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 132f), 150f)
            };
    }

    private sealed class SmartFlowFixtureMeasurementService : FixtureMeasurementService
    {
        public override IReadOnlyList<BrochurePrintProjectMeasurement> GenerateProjectCandidates(
            BrochurePrintPlanningItem item)
        {
            // Current order heights: 280, 280, 280, 180, 180.
            // Pulling project 4 backwards yields 280+280+180 on one sheet and
            // 280+180+closing on the final sheet, saving one physical sheet.
            var height = item.ProjectId <= 3 ? 280f : 180f;
            return new[]
            {
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 136f), height)
            };
        }

        public override BrochurePrintClosingMeasurement MeasureClosing(BrochurePrintMatter? matter, string? strapline)
            => new(260f, 195f, 65f, 0f);
    }

    private sealed class AutomaticImageFixtureMeasurementService : FixtureMeasurementService
    {
        public override IReadOnlyList<BrochurePrintProjectMeasurement> GenerateProjectCandidates(
            BrochurePrintPlanningItem item)
        {
            if (item.ImageMode == BrochureImageMode.GalleryTwo)
            {
                var gallery = BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 136f, useSecondaryImage: true);
                return new[] { Measurement(item.ProjectId, gallery, 235f) };
            }

            var single = BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 136f, useSecondaryImage: false);
            var galleryCandidate = BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 136f, useSecondaryImage: true);
            return new[]
            {
                Measurement(item.ProjectId, single, 198f),
                Measurement(item.ProjectId, galleryCandidate, 235f)
            };
        }
    }

    private sealed class EmergencyFixtureMeasurementService : FixtureMeasurementService
    {
        public override IReadOnlyList<BrochurePrintProjectMeasurement> GenerateProjectCandidates(
            BrochurePrintPlanningItem item)
        {
            var height = item.ProjectId == 99
                ? BrochurePrintLayoutMetrics.ProjectContentCapacity(false) + 40f
                : 230f;
            return new[]
            {
                Measurement(item.ProjectId, BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Dense, 132f), height)
            };
        }

        public override BrochurePrintProjectMeasurement MeasureProject(
            BrochurePrintPlanningItem item,
            BrochurePrintLayoutVariant variant,
            float imageWidthAdjustmentPoints = 0f)
        {
            if (variant == BrochurePrintLayoutVariant.Compact)
            {
                var compact = BrochurePrintLayoutMetrics.VariantSpec(BrochurePrintLayoutVariant.Compact, 128f);
                return Measurement(item.ProjectId, compact, BrochurePrintLayoutMetrics.ProjectContentCapacity(false) - 20f);
            }
            return base.MeasureProject(item, variant, imageWidthAdjustmentPoints);
        }
    }
}
