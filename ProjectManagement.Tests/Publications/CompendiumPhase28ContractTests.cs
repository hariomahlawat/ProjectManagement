using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase28ContractTests
{
    [Fact]
    public void BuildStamp_IdentifiesProofFirstPublicationReviewWorkspace()
    {
        Assert.Equal("CompendiumPdf_2026-08-14_publication-review-v7", CompendiumReadService.BuildStamp);
    }
}
