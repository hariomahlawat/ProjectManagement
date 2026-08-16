using ProjectManagement.Models;
using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase37_4ParticularsStyleTests
{
    [Fact]
    public void Panel_FourModulesPreservesTwoByTwoInstitutionalGrid()
    {
        var layout = CompendiumProjectParticularsLayoutPolicy.Resolve(
            CompendiumProjectParticularsStyle.Panel,
            FourCompactModules());

        Assert.Equal(2, layout.Columns);
        Assert.Equal(2, layout.Rows);
        Assert.False(layout.IsCompactSingle);
    }

    [Fact]
    public void Minimal_FourCompactModulesMayUseOneEditorialRow()
    {
        var layout = CompendiumProjectParticularsLayoutPolicy.Resolve(
            CompendiumProjectParticularsStyle.Minimal,
            FourCompactModules());

        Assert.Equal(4, layout.Columns);
        Assert.Equal(1, layout.Rows);
    }

    [Fact]
    public void Minimal_LongValuesFallBackToCalmTwoColumnGrid()
    {
        var modules = FourCompactModules().ToArray();
        modules[3] = modules[3] with
        {
            Value = "Under Progress · Prototype documentation, training package and transfer records under detailed compilation"
        };

        var layout = CompendiumProjectParticularsLayoutPolicy.Resolve(
            CompendiumProjectParticularsStyle.Minimal,
            modules);

        Assert.Equal(2, layout.Columns);
        Assert.Equal(2, layout.Rows);
    }

    [Fact]
    public void Minimal_IsPhysicallyLighterThanPanelForSameCompactFacts()
    {
        var modules = FourCompactModules();
        var panel = CompendiumProjectParticularsLayoutPolicy.Resolve(
            CompendiumProjectParticularsStyle.Panel,
            modules);
        var minimal = CompendiumProjectParticularsLayoutPolicy.Resolve(
            CompendiumProjectParticularsStyle.Minimal,
            modules);

        Assert.True(minimal.HeightPoints < panel.HeightPoints);
    }

    [Fact]
    public void ReviewFingerprint_ChangesWhenPublicationParticularsStyleChanges()
    {
        var panel = CompendiumReviewFingerprint.Create(CreateFingerprintInput(CompendiumProjectParticularsStyle.Panel));
        var minimal = CompendiumReviewFingerprint.Create(CreateFingerprintInput(CompendiumProjectParticularsStyle.Minimal));

        Assert.NotEqual(panel, minimal);
    }

    [Fact]
    public void Pagination_ConsumesResolvedParticularsStyleGeometry()
    {
        var modules = FourCompactModules();
        var narrative = string.Join(" ", Enumerable.Repeat(
            "The guaranteed project brief remains available for measured publication composition.", 16));

        var panel = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Technical,
            CompendiumDossierLayout.Technical,
            availablePhotoCount: 0,
            narrative,
            Array.Empty<string>(),
            programmeModuleCount: modules.Count,
            projectName: "Panel particulars pagination",
            programmeModules: modules,
            projectParticularsStyle: CompendiumProjectParticularsStyle.Panel);
        var minimal = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Technical,
            CompendiumDossierLayout.Technical,
            availablePhotoCount: 0,
            narrative,
            Array.Empty<string>(),
            programmeModuleCount: modules.Count,
            projectName: "Minimal particulars pagination",
            programmeModules: modules,
            projectParticularsStyle: CompendiumProjectParticularsStyle.Minimal);

        Assert.Equal(2, panel.ProgrammeColumns);
        Assert.Equal(4, minimal.ProgrammeColumns);
        Assert.True(panel.EstimatedPageCount >= 1);
        Assert.True(minimal.EstimatedPageCount >= 1);
    }

    private static IReadOnlyList<CompendiumProgrammeModuleDto> FourCompactModules()
        => new[]
        {
            new CompendiumProgrammeModuleDto(CompendiumProgrammeModuleKind.ArmsServices, "Arms / Services", "AAD", "arms-services", "maroon"),
            new CompendiumProgrammeModuleDto(CompendiumProgrammeModuleKind.ProliferationCost, "Proliferation Cost", "₹24 lakh", "proliferation-cost", "green"),
            new CompendiumProgrammeModuleDto(CompendiumProgrammeModuleKind.Ipr, "IPR", "Patent · Filed · 2026", "ipr-filed", "gold", CompendiumIprVisualState.Filed),
            new CompendiumProgrammeModuleDto(CompendiumProgrammeModuleKind.TechnologyTransfer, "Technology Transfer", "Completed · 2026", "technology-transfer", "blue")
        };

    private static CompendiumReviewFingerprintInput CreateFingerprintInput(CompendiumProjectParticularsStyle style)
        => new(
            ProjectId: 77,
            ProjectName: "Particulars style fingerprint",
            LifecycleStatus: ProjectLifecycleStatus.Active,
            ProjectCategory: "R&D",
            TechnicalCategory: "Simulation",
            SponsoringLineDirectorate: "AAD",
            CompletionYear: null,
            ProliferationAvailability: true,
            ProliferationCostLakhs: 24m,
            Description: "Guaranteed project brief content.",
            ResolvedPhotoId: null,
            ImageSelectionMode: CompendiumImageSelectionMode.Automatic,
            FocalX: .5d,
            FocalY: .5d)
        {
            NarrativeSource = CompendiumNarrativeSource.ProjectBrief,
            ProjectParticularsStyle = style
        };
}
