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
            BrochurePrintLayoutVariant variant)
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

            return new BrochurePrintProjectMeasurement(
                item.ProjectId,
                variant,
                baseHeight + delta,
                22f,
                8f,
                7.8f,
                1.05f,
                120f,
                4.5f,
                260f,
                100f,
                72f,
                quality);
        }

        public BrochurePrintClosingMeasurement MeasureClosing(BrochurePrintMatter? matter, string? strapline)
            => new(235f, 165f, 50f, 10f);

        public BrochurePrintFrontPagePlan MeasureFrontPage(
            BrochurePrintMatter? matter,
            BrochureCoverStyle coverStyle,
            string? strapline)
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
}
