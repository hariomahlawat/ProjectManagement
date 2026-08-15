using ProjectManagement.Services.Compendiums;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase31_1PaginationTests
{
    [Fact]
    public void Pagination_NormalEditorialBriefStaysOnOnePage()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "Operational capability supports realistic training, controlled assessment and repeatable mission rehearsal.",
            18));
        var layout = CompendiumDossierLayoutPlanner.Resolve(
            CompendiumDossierLayout.Automatic,
            1,
            narrative,
            Array.Empty<string>(),
            2,
            "Representative simulator project");

        var pagination = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Automatic,
            layout.Layout,
            1,
            narrative,
            Array.Empty<string>(),
            2,
            "Representative simulator project");

        Assert.Equal(1, pagination.EstimatedPageCount);
        Assert.False(pagination.UsesContinuation);
        Assert.True(pagination.FirstPageNarrativeBudget >= narrative.Length);
    }

    [Fact]
    public void Pagination_AutomaticYieldsPhotographyBeforeCreatingContinuation()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "The project provides a modular technical capability with controlled workflows, field-ready integration and maintainable system architecture.",
            20));
        var initial = CompendiumDossierLayoutPlanner.Resolve(
            CompendiumDossierLayout.Automatic,
            1,
            narrative,
            Array.Empty<string>(),
            2,
            "Longer capability project");

        var pagination = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Automatic,
            initial.Layout,
            1,
            narrative,
            Array.Empty<string>(),
            2,
            "Longer capability project");

        Assert.Equal(1, pagination.EstimatedPageCount);
        Assert.False(pagination.UsesContinuation);
        Assert.True(pagination.PrimaryImageHeightPoints <= CompendiumDossierPaginationPlanner.MaximumImageHeight(pagination.Layout, 1));
        Assert.True(pagination.NarrativeFontScale >= 1f);
    }

    [Fact]
    public void Pagination_ExplicitVisualLayoutUsesControlledContinuationForExtremeNarrative()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "Detailed operational and technical narrative for a deliberately oversized publication dossier.",
            90));

        var pagination = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.VisualHero,
            CompendiumDossierLayout.VisualHero,
            1,
            narrative,
            Array.Empty<string>(),
            2,
            "Oversized visual dossier");

        Assert.True(pagination.EstimatedPageCount > 1);
        Assert.True(pagination.UsesContinuation);
        Assert.Equal(CompendiumDossierLayout.VisualHero, pagination.Layout);
    }

    [Fact]
    public void TechnicalSpecificationColumns_RespectLongestBulletNotOnlyAggregateLength()
    {
        var shortItems = new[]
        {
            "Rugged workstation with dedicated GPU.",
            "Secure high-throughput local network.",
            "Five head-mounted training displays.",
            "Custom weapon-interface kits."
        };
        var oneVeryLongItem = new[]
        {
            new string('A', 700),
            "Short requirement.",
            "Another short requirement."
        };

        Assert.Equal(3, CompendiumDossierPaginationPlanner.ResolveTechnicalSpecificationColumns(shortItems));
        Assert.Equal(1, CompendiumDossierPaginationPlanner.ResolveTechnicalSpecificationColumns(oneVeryLongItem));
    }

    [Fact]
    public void ProgrammeInformation_FourModulesUseReadableTwoByTwoGrid()
    {
        Assert.Equal(1, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(1));
        Assert.Equal(2, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(2));
        Assert.Equal(3, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(3));
        Assert.Equal(2, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(4));
    }

    [Fact]
    public void PagePlanner_UsesFitPlanInsteadOfLegacyFixedNarrativeBudget()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "This representative project brief contains enough detail to exercise the adaptive one-page dossier planner without producing an orphan continuation.",
            15));
        var initial = CompendiumDossierLayoutPlanner.Resolve(
            CompendiumDossierLayout.Automatic,
            1,
            narrative,
            Array.Empty<string>(),
            1,
            "Adaptive pagination project");
        var fit = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Automatic,
            initial.Layout,
            1,
            narrative,
            Array.Empty<string>(),
            1,
            "Adaptive pagination project");

        var project = new CompendiumPdfProjectSection(
            71,
            "Adaptive pagination project",
            null,
            "AI",
            "2026",
            "Inf",
            "20 lakh",
            null,
            narrative,
            new byte[] { 1, 2, 3 },
            true)
        {
            DossierLayoutRequested = CompendiumDossierLayout.Automatic,
            DossierLayout = fit.Layout,
            DossierPrimaryImageHeightPoints = fit.PrimaryImageHeightPoints,
            DossierFirstPageNarrativeBudget = fit.FirstPageNarrativeBudget,
            DossierFirstPageSpecificationCount = fit.FirstPageSpecificationCount
        };
        var context = new CompendiumPdfReportContext(
            "SDD Simulators Compendium",
            "Detailed Project Reference",
            "Simulator Development Division",
            "Simulator Development Division",
            null,
            DateTimeOffset.UtcNow,
            new[] { new CompendiumPdfCategorySection("AI", new[] { project }) },
            false);

        var plan = new CompendiumPagePlanner().Plan(context);
        var projectPages = plan.Pages.Where(page => page.Project?.ProjectId == project.ProjectId).ToArray();

        Assert.Single(projectPages);
        Assert.Equal(CompendiumPageKind.Project, projectPages[0].Kind);
    }
}
