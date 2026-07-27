using ProjectManagement.Models.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ModuleSubNavActiveMatcherTests
{
    private static readonly NavigationItem ArppItem = new()
    {
        Text = "ARPP / PPP",
        Page = "/Projects/Arpp/Index",
        ActivePagePrefix = "/Projects/Arpp/"
    };

    [Theory]
    [InlineData("/Projects/Arpp/Index")]
    [InlineData("/Projects/Arpp/History")]
    [InlineData("/Projects/Arpp/Print")]
    public void IsActive_MatchesConfiguredArppRouteFamily(string currentPage)
    {
        var active = ModuleSubNavActiveMatcher.IsActive(
            ArppItem,
            currentArea: null,
            currentPage: currentPage,
            currentController: null,
            currentAction: null);

        Assert.True(active);
    }

    [Theory]
    [InlineData("/Projects/Index")]
    [InlineData("/Projects/Ongoing/Index")]
    [InlineData("/Projects/ArppArchive/Index")]
    public void IsActive_DoesNotLeakIntoOtherProjectRoutes(string currentPage)
    {
        var active = ModuleSubNavActiveMatcher.IsActive(
            ArppItem,
            currentArea: null,
            currentPage: currentPage,
            currentController: null,
            currentAction: null);

        Assert.False(active);
    }

    [Fact]
    public void IsActive_RequiresMatchingArea()
    {
        var areaItem = ArppItem with { Area = "Admin" };

        var active = ModuleSubNavActiveMatcher.IsActive(
            areaItem,
            currentArea: null,
            currentPage: "/Projects/Arpp/History",
            currentController: null,
            currentAction: null);

        Assert.False(active);
    }
}
