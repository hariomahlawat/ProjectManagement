using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Pages.Projects.Publications.Compendium;
using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase30ContractTests
{
    [Fact]
    public void CoverEditor_IsAuthorizedAndExposesPhotoAndSaveHandlers()
    {
        var type = typeof(CoverModel);
        Assert.NotNull(type.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(type.GetMethod(nameof(CoverModel.OnGetProjectPhotosAsync)));
        Assert.NotNull(type.GetMethod(nameof(CoverModel.OnPostSaveAsync)));
    }

    [Fact]
    public void CoverComposer_ProvidesControlledFrontAndBackLayouts()
    {
        Assert.Contains(CompendiumFrontCoverTemplate.InstitutionalHero, Enum.GetValues<CompendiumFrontCoverTemplate>());
        Assert.Contains(CompendiumFrontCoverTemplate.FullBleedHero, Enum.GetValues<CompendiumFrontCoverTemplate>());
        Assert.Contains(CompendiumFrontCoverTemplate.EditorialSplit, Enum.GetValues<CompendiumFrontCoverTemplate>());
        Assert.Contains(CompendiumFrontCoverTemplate.Triptych, Enum.GetValues<CompendiumFrontCoverTemplate>());
        Assert.Contains(CompendiumFrontCoverTemplate.Minimal, Enum.GetValues<CompendiumFrontCoverTemplate>());

        Assert.Contains(CompendiumBackCoverTemplate.MinimalInstitutional, Enum.GetValues<CompendiumBackCoverTemplate>());
        Assert.Contains(CompendiumBackCoverTemplate.ImageEcho, Enum.GetValues<CompendiumBackCoverTemplate>());
        Assert.Contains(CompendiumBackCoverTemplate.PortfolioStrip, Enum.GetValues<CompendiumBackCoverTemplate>());
        Assert.Contains(CompendiumBackCoverTemplate.TypographyOnly, Enum.GetValues<CompendiumBackCoverTemplate>());
        Assert.Contains(CompendiumBackCoverTemplate.Clean, Enum.GetValues<CompendiumBackCoverTemplate>());
    }

    [Fact]
    public void CoverComposer_UsesVersionedPdfIdentity()
    {
        Assert.Equal("CompendiumPdf_2026-08-14_cover-fidelity-v9", CompendiumReadService.BuildStamp);
    }

    [Fact]
    public void PublicationImagePolicy_SupportsFillAndFit()
    {
        var fill = CompendiumPublicationImagePolicy.CalculateEffectiveDpi(1600, 900, "Short brief", CompendiumImageFitMode.Fill);
        var fit = CompendiumPublicationImagePolicy.CalculateEffectiveDpi(1600, 900, "Short brief", CompendiumImageFitMode.Fit);
        Assert.NotNull(fill);
        Assert.NotNull(fit);
        Assert.True(fill > 0);
        Assert.True(fit > 0);
    }
}
