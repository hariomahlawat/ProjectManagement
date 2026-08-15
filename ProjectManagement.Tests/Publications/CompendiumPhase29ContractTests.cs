using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Pages.Projects.Publications.Compendium;
using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase29ContractTests
{
    [Fact]
    public void StructureEditor_IsAuthorizedAndExposesBulkSaveHandler()
    {
        var type = typeof(StructureModel);
        Assert.NotNull(type.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(type.GetMethod(nameof(StructureModel.OnPostSaveAsync)));
    }

    [Fact]
    public void StructureComposer_DoesNotChangePdfBuildIdentity()
    {
        Assert.StartsWith("CompendiumPdf_2026-08-", CompendiumReadService.BuildStamp);
    }
}
