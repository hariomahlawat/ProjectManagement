using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochurePrintCompactPlannerTests
{
    [Fact]
    public void Plan_EightRepresentativeProjects_KeepsClosingMatterWithProjects()
    {
        var projects = Enumerable.Range(1, 8)
            .Select(index => PlanningItem(index, words: index == 1 ? 185 : 150))
            .ToArray();
        var approved = BrochurePrintPublicationPolicy.ApprovedReference;

        var plan = BrochurePrintCompactPlanner.Plan(
            projects,
            approved.VisionaryHorizons,
            approved.NewSimulatorsGuidance);

        Assert.True(plan.ClosingMatterSharesFinalPage);
        Assert.True(plan.ClosingPageProjectCount > 0);
        Assert.Equal(1 + plan.Pages.Count, plan.EstimatedTotalPageCount);
        Assert.InRange(plan.AverageContentUtilizationPercent, 50, 100);

        var plannedIndexes = plan.Pages
            .SelectMany(page => page.ProjectIndexes)
            .ToArray();
        Assert.Equal(Enumerable.Range(0, projects.Length).ToArray(), plannedIndexes);
    }

    [Fact]
    public void Plan_ReservesClosingHeightOnFinalSheet()
    {
        var projects = Enumerable.Range(1, 6)
            .Select(index => PlanningItem(index, words: 135))
            .ToArray();
        var approved = BrochurePrintPublicationPolicy.ApprovedReference;

        var plan = BrochurePrintCompactPlanner.Plan(
            projects,
            approved.VisionaryHorizons,
            approved.NewSimulatorsGuidance);

        var finalPage = Assert.Single(plan.Pages.Where(page => page.IncludesClosingMatter));
        Assert.True(finalPage.ProjectIndexes.Count > 0);
        Assert.True(plan.EstimatedClosingHeightPoints > 0);
        Assert.True(finalPage.EstimatedPhysicalUsedPoints <= finalPage.CapacityPoints);
    }

    [Fact]
    public void EstimateProjectHeight_GalleryTwo_ReservesMoreImageHeightThanSingle()
    {
        var single = PlanningItem(1, words: 75) with { ImageMode = BrochureImageMode.Single, HasSecondaryPhoto = true };
        var gallery = single with { ImageMode = BrochureImageMode.GalleryTwo };

        var singleHeight = BrochurePrintCompactPlanner.EstimateProjectHeight(single);
        var galleryHeight = BrochurePrintCompactPlanner.EstimateProjectHeight(gallery);

        Assert.True(galleryHeight > singleHeight);
    }

    private static BrochurePrintPlanningItem PlanningItem(int id, int words)
        => new(
            id,
            $"Representative Compact Project {id}",
            words,
            BrochureImageMode.Automatic,
            HasPrimaryPhoto: true,
            HasSecondaryPhoto: false);
}
