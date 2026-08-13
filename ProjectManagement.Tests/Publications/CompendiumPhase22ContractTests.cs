using ProjectManagement.Models.Publications;
using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase22ContractTests
{
    [Fact]
    public void Preflight_WithSelectedProjectsAndNoBlockers_CanGenerate()
    {
        var preflight = CompendiumPreflightDto.Empty with
        {
            SelectedProjectCount = 3,
            BlockerCount = 0
        };
        Assert.True(preflight.CanGenerate);
    }

    [Fact]
    public void Preflight_WithBlocker_CannotGenerate()
    {
        var preflight = CompendiumPreflightDto.Empty with
        {
            SelectedProjectCount = 3,
            BlockerCount = 1
        };
        Assert.False(preflight.CanGenerate);
    }

    [Fact]
    public void PresetProject_StoresMembershipAndOrderOnly()
    {
        var item = new CompendiumPresetProject
        {
            ProjectId = 42,
            ProjectNameSnapshot = "Example project",
            SortOrder = 7
        };
        Assert.Equal(42, item.ProjectId);
        Assert.Equal(7, item.SortOrder);
    }
}
