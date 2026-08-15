using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase32CompositionTests
{
    [Fact]
    public void SparseVisualDossier_InvestsAvailableSpaceInPhotographyAndTypography()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "The simulator provides a focused operational training capability with repeatable scenarios and controlled assessment.",
            6));

        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.VisualHero,
            CompendiumDossierLayout.VisualHero,
            1,
            narrative,
            Array.Empty<string>(),
            0,
            "Representative simulator");

        Assert.Equal(CompendiumDossierLayout.VisualHero, decision.Layout);
        Assert.Equal(1, decision.EstimatedPageCount);
        Assert.False(decision.UsesContinuation);
        Assert.True(decision.PrimaryImageHeightPoints >= CompendiumDossierPaginationPlanner.PreferredImageHeight(decision.Layout, 1));
        Assert.InRange(decision.NarrativeFontScale, 1f, 1.08f);
        Assert.Contains("optimised", decision.PaginationNote.ToLowerInvariant());
    }

    [Fact]
    public void TechnicalSpecifications_UseThreeColumnsOnlyForCompactFragments()
    {
        var compact = new[]
        {
            "Dedicated GPU workstation",
            "Secure Wi-Fi network",
            "Five VR headsets",
            "Weapon interface kit"
        };
        var descriptive = new[]
        {
            new string('A', 110),
            "Secure Wi-Fi network",
            "Five VR headsets",
            "Weapon interface kit"
        };
        var veryLong = new[]
        {
            new string('B', 220),
            "Secure Wi-Fi network",
            "Five VR headsets"
        };

        Assert.Equal(3, CompendiumDossierPaginationPlanner.ResolveTechnicalSpecificationColumns(compact));
        Assert.Equal(2, CompendiumDossierPaginationPlanner.ResolveTechnicalSpecificationColumns(descriptive));
        Assert.Equal(1, CompendiumDossierPaginationPlanner.ResolveTechnicalSpecificationColumns(veryLong));
    }

    [Fact]
    public void ExplicitLayout_IsPreservedWhileGeometryIsOptimisedWithinThatFamily()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "A concise project brief describes the capability, training value and maintainable technical architecture.",
            10));

        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Balanced,
            CompendiumDossierLayout.Balanced,
            3,
            narrative,
            Array.Empty<string>(),
            2,
            "Balanced dossier project");

        Assert.Equal(CompendiumDossierLayout.Balanced, decision.Layout);
        Assert.Equal(1, decision.EstimatedPageCount);
        Assert.False(decision.UsesContinuation);
        Assert.Contains("publisher-selected", decision.Reason.ToLowerInvariant());
    }

    [Fact]
    public void ProgrammeInformation_KeepsReadableColumnPolicy()
    {
        Assert.Equal(1, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(1));
        Assert.Equal(2, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(2));
        Assert.Equal(3, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(3));
        Assert.Equal(2, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(4));
    }
}
