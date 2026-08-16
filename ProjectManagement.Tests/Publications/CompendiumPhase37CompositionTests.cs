using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase37CompositionTests
{
    [Fact]
    public void BalancedFlow_UsesCompleteSentenceFromNextParagraph_WhenItClosesSideGap()
    {
        var first = "This project aims to develop a modern, reliable, and user-friendly digital platform for planning, monitoring, and managing organisational projects from initiation to completion. " +
                    "The system will provide a central workspace for maintaining project details, milestones, documents, responsibilities, approvals, financial information, progress updates, risks, and key decisions. " +
                    "It is intended to improve visibility, accountability, coordination, and timely decision-making across multiple teams and levels of management.";
        var second = "The proposed solution will include role-based access, structured workflows, dashboards, search, filters, notifications, reporting, and secure document management. " +
                     "Users will be able to review project status, identify pending actions, record remarks, track important dates, and generate management reports from a single integrated interface.";
        var narrative = first + "\n\n" + second;

        var assessment = CompendiumDossierNarrativeFlowPlanner.AssessSideFlow(
            narrative,
            imageHeightPoints: 270f,
            narrativeFontScale: 1f);

        Assert.Contains("The proposed solution will include", assessment.SideSegment);
        Assert.Contains("Users will be able to review", assessment.BelowSegment);
        Assert.True(assessment.RemainingHeightPoints <= 30f);
    }

    [Fact]
    public void NarrativeTypography_JustifiesWideRegions_ButProtectsNarrowBalancedColumn()
    {
        Assert.Equal(
            CompendiumNarrativeAlignment.Left,
            CompendiumNarrativeTypographyPolicy.ResolveSideAlignment(
                CompendiumNarrativeAlignment.Justified,
                223f));

        Assert.Equal(
            CompendiumNarrativeAlignment.Justified,
            CompendiumNarrativeTypographyPolicy.ResolveSideAlignment(
                CompendiumNarrativeAlignment.Justified,
                300f));

        Assert.Equal(
            CompendiumNarrativeAlignment.Justified,
            CompendiumNarrativeTypographyPolicy.ResolveFullWidthAlignment(
                CompendiumNarrativeAlignment.Justified));
    }

    [Theory]
    [InlineData(55)]
    [InlineData(100)]
    [InlineData(119)]
    public void AutomaticLayout_DoesNotPromoteVeryLowDpiPhotographyToLargeHero(int dpi)
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "A concise capability narrative should retain readable text while automatic composition protects print fidelity.",
            8));

        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Automatic,
            CompendiumDossierLayout.VisualHero,
            availablePhotoCount: 1,
            narrative,
            Array.Empty<string>(),
            programmeModuleCount: 1,
            projectName: "Low DPI automatic layout test",
            primaryImageEffectiveDpi: dpi,
            balancedTextFlowMode: CompendiumBalancedTextFlowMode.FlowBelowImage);

        Assert.DoesNotContain(
            decision.Layout,
            new[] { CompendiumDossierLayout.VisualHero, CompendiumDossierLayout.MultiImageEditorial });
    }

    [Fact]
    public void ExplicitVisualHero_RemainsAvailableAsPublisherOverride()
    {
        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.VisualHero,
            CompendiumDossierLayout.VisualHero,
            availablePhotoCount: 1,
            narrative: "Publisher override remains available even when the source image is low resolution.",
            technicalSpecifications: Array.Empty<string>(),
            programmeModuleCount: 0,
            projectName: "Manual hero override",
            primaryImageEffectiveDpi: 55,
            balancedTextFlowMode: CompendiumBalancedTextFlowMode.FlowBelowImage);

        Assert.Equal(CompendiumDossierLayout.VisualHero, decision.Layout);
    }

    [Fact]
    public void ProgrammeColumns_KeepSingleFactCompactAndLargerSetsStructured()
    {
        Assert.Equal(1, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(1));
        Assert.Equal(2, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(2));
        Assert.Equal(3, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(3));
        Assert.Equal(2, CompendiumDossierPaginationPlanner.ResolveProgrammeColumns(4));
    }

    [Fact]
    public void NaturalSplitter_NeverSlicesSingleOversizedSentence()
    {
        var sentence = "A" + new string('x', 1800) + ".";
        var chunks = CompendiumDossierNarrativeFlowPlanner.SplitNatural(sentence, 400, 500);

        Assert.Single(chunks);
        Assert.Equal(sentence, chunks[0]);
    }
}
