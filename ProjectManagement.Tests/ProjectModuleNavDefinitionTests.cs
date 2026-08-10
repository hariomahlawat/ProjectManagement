using System.Linq;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectModuleNavDefinitionTests
{
    [Fact]
    public void Build_SeparatesCreateProjectAsModuleAction()
    {
        var items = ProjectModuleNavDefinition.Build();

        var create = Assert.Single(items.Where(item => item.IsAction));
        Assert.Equal("Create project", create.Text);
        Assert.Equal("/Projects/Create", create.Page);
        Assert.Equal("Project.Create", create.AuthorizationPolicy);
    }

    [Fact]
    public void Build_KeepsDestinationItemsAsNavigationTabs()
    {
        var items = ProjectModuleNavDefinition.Build();
        var tabs = items.Where(item => !item.IsAction).ToList();

        Assert.Contains(tabs, item => item.Text == "Projects repository");
        Assert.Contains(tabs, item => item.Text == "Ongoing projects");
        Assert.Contains(tabs, item => item.Text == "Completed projects summary");
        Assert.Contains(
            tabs,
            item => item.Text == "ARPP / PPP"
                    && item.Page == "/Projects/Arpp/Index"
                    && item.ActivePagePrefix == "/Projects/Arpp/");
        Assert.Contains(
            tabs,
            item => item.Text == "Publications"
                    && item.Page == "/Projects/Publications/Index"
                    && item.ActivePagePrefix == "/Projects/Publications/");
        Assert.Contains(tabs, item => item.Text == "Pending approvals");
        Assert.DoesNotContain(tabs, item => item.Page == "/Projects/Create");
    }

    [Fact]
    public void Build_PlacesArppReaderAfterProcessAndBeforeAnalytics()
    {
        var tabs = ProjectModuleNavDefinition.Build()
            .Where(item => !item.IsAction)
            .ToList();

        var processIndex = tabs.FindIndex(item => item.Page == "/Process/Index");
        var arppIndex = tabs.FindIndex(item => item.Page == "/Projects/Arpp/Index");
        var analyticsIndex = tabs.FindIndex(item => item.Page == "/Analytics/Index");

        Assert.True(processIndex >= 0);
        Assert.Equal(processIndex + 1, arppIndex);
        Assert.Equal(arppIndex + 1, analyticsIndex);
    }

    [Fact]
    public void Build_PlacesPublicationsAfterAnalyticsAndBeforeIndustryDirectory()
    {
        var tabs = ProjectModuleNavDefinition.Build()
            .Where(item => !item.IsAction)
            .ToList();

        var analyticsIndex = tabs.FindIndex(item => item.Page == "/Analytics/Index");
        var publicationsIndex = tabs.FindIndex(item => item.Page == "/Projects/Publications/Index");
        var industryIndex = tabs.FindIndex(item => item.Page == "/IndustryPartners/Index");

        Assert.True(analyticsIndex >= 0);
        Assert.Equal(analyticsIndex + 1, publicationsIndex);
        Assert.Equal(publicationsIndex + 1, industryIndex);
    }

    [Fact]
    public void Build_ContainsExactlyOnePublicationsDestination()
    {
        var publications = ProjectModuleNavDefinition.Build()
            .Where(item => item.Text == "Publications")
            .ToArray();

        var item = Assert.Single(publications);
        Assert.False(item.IsAction);
        Assert.Equal("/Projects/Publications/Index", item.Page);
        Assert.Equal("/Projects/Publications/", item.ActivePagePrefix);
    }
}
