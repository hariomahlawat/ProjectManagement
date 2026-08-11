using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochurePrintCompactPlannerTests
{
    [Fact]
    public void Plan_MediumProjects_PreservesOrderAndUsesFourProjectSheetsWhereTheyFit()
    {
        var planner = new BrochurePrintPagePlanner(new DeterministicMeasurementService());
        var projects = Enumerable.Range(1, 10)
            .Select(index => PlanningItem(index, words: 135))
            .ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            "Simulators of the Army, by the Army, for the Army",
            hasHandlingMarking: false);

        var plannedIndexes = plan.Pages
            .SelectMany(page => page.ProjectIndexes)
            .ToArray();
        Assert.Equal(Enumerable.Range(0, projects.Length).ToArray(), plannedIndexes);
        Assert.Contains(plan.Pages, page => page.Projects.Count == 4);
        Assert.True(plan.ClosingMatterSharesFinalPage);
        Assert.True(plan.ClosingPageProjectCount > 0);
        Assert.Equal(1 + plan.Pages.Count, plan.EstimatedTotalPageCount);
    }

    [Fact]
    public void Plan_FinalSheet_ReservesMeasuredClosingMatter()
    {
        var planner = new BrochurePrintPagePlanner(new DeterministicMeasurementService());
        var projects = Enumerable.Range(1, 8)
            .Select(index => PlanningItem(index, words: 150))
            .ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            "Simulators of the Army, by the Army, for the Army",
            hasHandlingMarking: false);

        var closingPage = Assert.Single(plan.Pages.Where(page => page.IncludesClosingMatter));
        Assert.True(closingPage.ClosingHeightPoints > 0);
        Assert.True(closingPage.Projects.Count > 0);
        Assert.True(closingPage.MeasuredPhysicalUsedPoints <= closingPage.CapacityPoints + .5f);
        Assert.Equal(closingPage.UtilizationPercent, plan.FinalPageUtilizationPercent);
    }

    [Fact]
    public void Plan_PrefersHigherQualityVariantWhenItStillFits()
    {
        var planner = new BrochurePrintPagePlanner(new DeterministicMeasurementService());
        var projects = Enumerable.Range(1, 3)
            .Select(index => PlanningItem(index, words: 110))
            .ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            "Simulators of the Army, by the Army, for the Army",
            hasHandlingMarking: false);

        var selectedVariants = plan.Pages
            .SelectMany(page => page.Projects)
            .Select(project => project.Measurement.Variant)
            .ToArray();

        Assert.Contains(BrochurePrintLayoutVariant.Visual, selectedVariants);
    }

    [Fact]
    public void Plan_NinePointTypographyOutranksSavingOneSheet()
    {
        var planner = new BrochurePrintPagePlanner(new QualityFloorMeasurementService());
        var projects = Enumerable.Range(1, 4)
            .Select(index => PlanningItem(index, words: 150))
            .ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            "Simulators of the Army, by the Army, for the Army",
            hasHandlingMarking: false);

        // All four Compact measurements could fit on one project sheet, but they are 8.5 pt.
        // Phase 12 deliberately chooses more sheets and preserves the 9 pt publication body.
        Assert.True(plan.Pages.Count(page => page.Projects.Count > 0) >= 2);
        Assert.All(
            plan.Pages.SelectMany(page => page.Projects),
            project => Assert.True(
                project.Measurement.BodyFontSize
                >= BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize - .01f));
    }

    [Fact]
    public void Plan_ResidualPassExpandsImagesWithoutChangingProjectOrder()
    {
        var measurement = new DeterministicMeasurementService();
        var planner = new BrochurePrintPagePlanner(measurement);
        var projects = Enumerable.Range(1, 3)
            .Select(index => PlanningItem(index, words: 105))
            .ToArray();

        var plan = planner.Plan(
            projects,
            BrochurePrintPublicationPolicy.ApprovedReference,
            BrochureCoverStyle.Institutional,
            "Simulators of the Army, by the Army, for the Army",
            hasHandlingMarking: false);

        var planned = plan.Pages.SelectMany(page => page.Projects).ToArray();
        Assert.Equal(Enumerable.Range(0, 3).ToArray(), planned.Select(project => project.ProjectIndex).ToArray());
        Assert.Contains(planned, project => project.Measurement.ImageWidthPoints > 140f);
        Assert.Contains(plan.Pages, page =>
            page.ExtraModuleVerticalPaddingPoints > 0f
            || page.ExtraInterModuleSpacingPoints > 0f
            || page.Projects.Any(project => project.Measurement.ImageWidthPoints > 140f));
    }

    private static BrochurePrintPlanningItem PlanningItem(int id, int words)
        => new(
            id,
            $"Representative Compact Project {id}",
            string.Join(" ", Enumerable.Range(1, words).Select(index => $"word{index}")),
            BrochureImageMode.Automatic,
            HasPrimaryPhoto: true,
            HasSecondaryPhoto: false);

    private sealed class DeterministicMeasurementService : IBrochurePrintMeasurementService
    {
        public BrochurePrintProjectMeasurement MeasureProject(
            BrochurePrintPlanningItem item,
            BrochurePrintLayoutVariant variant,
            float imageWidthAdjustmentPoints = 0f)
        {
            var baseHeight = item.NarrativeWordCount switch
            {
                > 170 => 185f,
                > 140 => 165f,
                > 110 => 148f,
                _ => 132f
            };
            var delta = variant switch
            {
                BrochurePrintLayoutVariant.Visual => 14f,
                BrochurePrintLayoutVariant.Balanced => 7f,
                _ => 0f
            };
            var quality = variant switch
            {
                BrochurePrintLayoutVariant.Visual => 3,
                BrochurePrintLayoutVariant.Balanced => 2,
                _ => 1
            };
            var bodyFont = variant == BrochurePrintLayoutVariant.Compact
                ? BrochurePrintLayoutMetrics.ProjectBodyMinimumFontSize
                : BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize;
            var imageWidth = 140f + imageWidthAdjustmentPoints;
            var imageExpansionHeight = imageWidthAdjustmentPoints * .45f;

            return new BrochurePrintProjectMeasurement(
                item.ProjectId,
                variant,
                baseHeight + delta + imageExpansionHeight,
                22f,
                variant == BrochurePrintLayoutVariant.Compact ? 9.5f : 10f,
                bodyFont,
                1.05f,
                imageWidth,
                5.6f,
                250f,
                100f,
                imageWidth / BrochurePrintLayoutMetrics.SingleImageAspectRatio,
                quality);
        }

        public BrochurePrintClosingMeasurement MeasureClosing(BrochurePrintMatter? matter, string? strapline)
            => new(235f, 165f, 50f, 10f);

        public BrochurePrintFrontPagePlan MeasureFrontPage(
            BrochurePrintMatter? matter,
            BrochureCoverStyle coverStyle,
            string? strapline)
            => FrontPlan(coverStyle);
    }

    private sealed class QualityFloorMeasurementService : IBrochurePrintMeasurementService
    {
        public BrochurePrintProjectMeasurement MeasureProject(
            BrochurePrintPlanningItem item,
            BrochurePrintLayoutVariant variant,
            float imageWidthAdjustmentPoints = 0f)
        {
            var preferred = variant != BrochurePrintLayoutVariant.Compact;
            var height = preferred ? 230f : 200f;
            var bodyFont = preferred
                ? BrochurePrintLayoutMetrics.ProjectBodyPreferredFontSize
                : BrochurePrintLayoutMetrics.ProjectBodyMinimumFontSize;

            return new BrochurePrintProjectMeasurement(
                item.ProjectId,
                variant,
                height,
                22f,
                preferred ? 10f : 9.5f,
                bodyFont,
                1.05f,
                140f,
                5.6f,
                250f,
                100f,
                78.75f,
                preferred ? 3 : 1);
        }

        public BrochurePrintClosingMeasurement MeasureClosing(BrochurePrintMatter? matter, string? strapline)
            => new(0f, 0f, 0f, 0f);

        public BrochurePrintFrontPagePlan MeasureFrontPage(
            BrochurePrintMatter? matter,
            BrochureCoverStyle coverStyle,
            string? strapline)
            => FrontPlan(coverStyle);
    }

    private static BrochurePrintFrontPagePlan FrontPlan(BrochureCoverStyle coverStyle)
        => new(
            Fits: true,
            HeroHeightPoints: 270f,
            CentreBlockHeightPoints: 55f,
            CentreFontSize: 11f,
            BodyBlockHeightPoints: 350f,
            BodyFontSize: 8.8f,
            BodyLineHeight: 1.07f,
            BodySpacingPoints: 6f,
            ContactBlockHeightPoints: 150f,
            ContactFontSize: 8.5f,
            StraplineHeightPoints: 21.755f,
            TotalUsedHeightPoints: BrochurePrintLayoutMetrics.ReferenceHeightPoints,
            UtilizationPercent: 100,
            UsesMinimumTypography: false,
            CoverStyle: coverStyle);
}
