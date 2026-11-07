using ProjectManagement.Models.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.ViewComponents;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// Admin destinations that live outside the Admin area (such as document deletion
// approvals) carry an explicit empty area so links from Admin pages do not keep the
// ambient Admin area. The module bar overflow and the navigation drawer must keep it.
public sealed class NavigationRouteValuesTests
{
    public static TheoryData<Func<NavigationItem, Microsoft.AspNetCore.Routing.RouteValueDictionary?>> Builders => new()
    {
        ModuleSubNavViewComponent.BuildRouteValues,
        NavigationDrawerViewComponent.BuildRouteValues,
    };

    [Theory]
    [MemberData(nameof(Builders))]
    public void ExplicitEmptyArea_IsKeptSoTheAmbientAreaIsCleared(
        Func<NavigationItem, Microsoft.AspNetCore.Routing.RouteValueDictionary?> build)
    {
        var item = AdminNavigationCatalog.Entries
            .Single(entry => entry.Key == AdminNavigationKeys.DocumentDeleteRequests)
            .Item;

        var values = build(item);

        Assert.NotNull(values);
        Assert.True(values!.TryGetValue("area", out var area));
        Assert.Equal(string.Empty, area);
        Assert.Equal("RepositoryDocumentDelete", values["Type"]);
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void NoArea_LeavesTheAmbientAreaAlone(
        Func<NavigationItem, Microsoft.AspNetCore.Routing.RouteValueDictionary?> build)
    {
        Assert.Null(build(new NavigationItem { Text = "Home", Page = "/Index" }));
    }
}
