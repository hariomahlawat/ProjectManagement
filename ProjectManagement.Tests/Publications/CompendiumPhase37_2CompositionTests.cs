using ProjectManagement.Models;
using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase37_2CompositionTests
{
    [Fact]
    public void SideColumnAssessment_FlagsTallNarrativeTowerAsEditoriallyUnbalanced()
    {
        var assessment = CompendiumDossierEditorialPolicy.AssessSideColumn(
            imageHeightPoints: 246f,
            narrativeHeightPoints: 430f);

        Assert.False(assessment.IsEditoriallyBalanced);
        Assert.True(assessment.OverflowHeightPoints > 150f);
        Assert.Contains("Flow below image", assessment.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutomaticComposition_DoesNotSelectEditoriallyInvalidBalancedSideColumn()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "The indigenous capability requires coordinated command control mission management integration evidence documentation and operational validation.",
            28));

        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Automatic,
            CompendiumDossierLayout.Balanced,
            availablePhotoCount: 1,
            narrative,
            Array.Empty<string>(),
            programmeModuleCount: 1,
            projectName: "Long side-column automatic test",
            primaryImageEffectiveDpi: 220,
            balancedTextFlowMode: CompendiumBalancedTextFlowMode.SideColumn,
            primaryImageSourceWidth: 1800,
            primaryImageSourceHeight: 1200,
            primaryImageFitMode: CompendiumImageFitMode.Fill);

        Assert.False(decision.HasEditorialWarning);
        Assert.NotEqual(CompendiumDossierLayout.Balanced, decision.Layout);
    }

    [Fact]
    public void ExplicitBalancedSideColumn_IsRetainedButReturnsPublisherWarningWhenUnbalanced()
    {
        var narrative = string.Join(" ", Enumerable.Repeat(
            "The project will establish an indigenous operational capability with complete documentation validation integration and field demonstration.",
            28));

        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Balanced,
            CompendiumDossierLayout.Balanced,
            availablePhotoCount: 1,
            narrative,
            Array.Empty<string>(),
            programmeModuleCount: 1,
            projectName: "Publisher side-column override",
            primaryImageEffectiveDpi: 220,
            balancedTextFlowMode: CompendiumBalancedTextFlowMode.SideColumn,
            primaryImageSourceWidth: 1800,
            primaryImageSourceHeight: 1200,
            primaryImageFitMode: CompendiumImageFitMode.Fill);

        Assert.Equal(CompendiumDossierLayout.Balanced, decision.Layout);
        Assert.True(decision.HasEditorialWarning);
        Assert.Contains("Flow below image", decision.EditorialWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitFillLayout_DoesNotCollapseBelowEditorialMinimumToProtectLowDpi()
    {
        var narrative = string.Join(" ", Enumerable.Repeat("Aerial delivery training narrative remains readable and concise.", 28));

        var decision = CompendiumDossierPaginationPlanner.Resolve(
            CompendiumDossierLayout.Balanced,
            CompendiumDossierLayout.Balanced,
            availablePhotoCount: 1,
            narrative,
            Array.Empty<string>(),
            programmeModuleCount: 2,
            projectName: "Low DPI explicit Fill test",
            primaryImageEffectiveDpi: 70,
            balancedTextFlowMode: CompendiumBalancedTextFlowMode.FlowBelowImage,
            primaryImageSourceWidth: 400,
            primaryImageSourceHeight: 400,
            primaryImageFitMode: CompendiumImageFitMode.Fill);

        Assert.True(decision.PrimaryImageHeightPoints + .1f >=
                    CompendiumDossierEditorialPolicy.MinimumEditorialFillHeightPoints(CompendiumDossierLayout.Balanced));
    }

    [Fact]
    public void Readiness_WarnsOnNearDuplicateLongNarrativeEvenWhenPunctuationDiffers()
    {
        var block = string.Join(" ", Enumerable.Repeat(
            "Aerial delivery training involves packing cargo parachutes and dropping supply loads from aircraft while weather and serviceability constrain live training.",
            8));
        var duplicate = block + " " + block.Replace('.', ',');
        var policy = new CompendiumReadinessPolicy();

        var result = policy.Evaluate(new CompendiumProjectReadinessContext(
            ProjectId: 7,
            ProjectName: "Duplicate narrative test",
            LifecycleStatus: ProjectLifecycleStatus.Active,
            CompletionYear: null,
            SponsoringLineDirectorate: "Inf",
            Description: duplicate,
            ProliferationCostLakhs: null,
            ProliferationAvailability: false,
            ResolvedPhotoId: 1,
            ResolvedPhotoUsable: true,
            ImageSelectionMode: CompendiumImageSelectionMode.Automatic,
            EffectiveDpi: 220,
            ExplicitPhotoUnavailable: false,
            CurrentReviewFingerprint: "current",
            SubmittedReviewFingerprint: null));

        Assert.Contains(result.Findings, finding => finding.Code == "duplicateNarrativeParagraph");
    }
}
