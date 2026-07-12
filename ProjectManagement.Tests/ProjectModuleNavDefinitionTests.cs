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
        Assert.Contains(tabs, item => item.Text == "Pending approvals");
        Assert.DoesNotContain(tabs, item => item.Page == "/Projects/Create");
    }
}
