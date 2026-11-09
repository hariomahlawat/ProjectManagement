using System;
using System.IO;
using System.Linq;
using ProjectManagement.Models.Navigation;
using ProjectManagement.ViewComponents;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class NavigationDrawerTests
{
    private static string? UrlFor(NavigationItem item) => item.Page;

    [Fact]
    public void ActivePagePrefix_HighlightsTheItemOnPagesInsideItsSection()
    {
        var items = new[]
        {
            new NavigationItem
            {
                Text = "Trackers",
                Children = new[]
                {
                    new NavigationItem { Text = "Activities", Page = "/Activities/Index", ActivePagePrefix = "/Activities/" },
                    new NavigationItem { Text = "Calendar", Page = "/Calendar/Index" }
                }
            }
        };

        var nodes = NavigationDrawerViewComponent.BuildNodes(items, null, "/Activities/Create", null, null, UrlFor);

        var section = Assert.Single(nodes);
        Assert.False(section.IsActive);
        Assert.True(section.HasActiveDescendant);
        Assert.True(section.Children.Single(child => child.Text == "Activities").IsActive);
        Assert.False(section.Children.Single(child => child.Text == "Calendar").IsActive);
    }

    [Fact]
    public void ExactPage_StaysActiveWithoutAPrefix()
    {
        var items = new[] { new NavigationItem { Text = "Dashboard", Page = "/Dashboard/Index" } };

        var node = Assert.Single(NavigationDrawerViewComponent.BuildNodes(items, null, "/Dashboard/Index", null, null, UrlFor));

        Assert.True(node.IsActive);
    }

    [Fact]
    public void ActivePagePrefix_DoesNotMatchAcrossAreas()
    {
        var items = new[]
        {
            new NavigationItem
            {
                Text = "Visits tracker",
                Area = "ProjectOfficeReports",
                Page = "/Visits/Index",
                ActivePagePrefix = "/Visits/"
            }
        };

        var node = Assert.Single(NavigationDrawerViewComponent.BuildNodes(items, null, "/Visits/Details", null, null, UrlFor));

        Assert.False(node.IsActive);
    }

    [Fact]
    public void DrawerMarkup_NamesTheDialogAndWritesValidAriaState()
    {
        var root = FindProjectRoot();
        var drawer = ReadDrawerFile(root, "Default.cshtml");
        var link = ReadDrawerFile(root, "_NavigationDrawerLink.cshtml");

        // The accessible name belongs on the element that carries role="dialog".
        Assert.Contains("role=\"dialog\" aria-modal=\"true\" aria-labelledby=\"@drawerLabelId\"", drawer, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"dialog\"", drawer, StringComparison.Ordinal);

        // Bootstrap's .btn-close inverts itself in dark mode, which hid the cross on the focus background.
        Assert.DoesNotContain("btn-close", drawer, StringComparison.Ordinal);

        // A Razor explicit expression ends at its closing parenthesis; anything after it is literal text.
        Assert.DoesNotContain(").ToString().ToLowerInvariant()\"", drawer, StringComparison.Ordinal);
        Assert.DoesNotContain(").ToString().ToLowerInvariant()\"", link, StringComparison.Ordinal);
    }

    [Fact]
    public void DrawerMarkup_KeepsTheScriptHooksAndOpensOnlyTheCurrentSection()
    {
        var drawer = ReadDrawerFile(FindProjectRoot(), "Default.cshtml");

        // drawer.js is imported without a version stamp, so a cached copy can meet this markup:
        // these hooks are the contract between them.
        foreach (var hook in new[]
                 {
                     "data-drawer-toggle", "data-drawer-target=", "data-drawer=", "data-drawer-overlay",
                     "data-drawer-panel", "data-drawer-close", "data-drawer-group", "data-drawer-group-summary",
                     "data-drawer-group-panel", "data-drawer-section="
                 })
        {
            Assert.Contains(hook, drawer, StringComparison.Ordinal);
        }

        // Main is a plain section; the others are <details> opened only around the current page.
        Assert.Contains("NavigationSectionKeys.Main", drawer, StringComparison.Ordinal);
        Assert.Contains("var isOpen = drawerSection.HasActiveDescendant;", drawer, StringComparison.Ordinal);
        Assert.Contains("open=\"@(isOpen ? \"open\" : null)\"", drawer, StringComparison.Ordinal);

        // Section summaries only expand and collapse: no link nested inside an interactive summary.
        Assert.DoesNotContain("data-drawer-group-link", drawer, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildNodes_CarriesSectionKeysForTheMarkup()
    {
        var items = new[]
        {
            new NavigationItem
            {
                Key = NavigationSectionKeys.Trackers,
                Text = "Trackers",
                Children = new[] { new NavigationItem { Text = "Activities", Page = "/Activities/Index" } }
            }
        };

        var section = Assert.Single(NavigationDrawerViewComponent.BuildNodes(items, null, "/Dashboard/Index", null, null, UrlFor));

        Assert.Equal(NavigationSectionKeys.Trackers, section.Key);
        Assert.False(section.HasActiveDescendant);
    }

    private static string ReadDrawerFile(string root, string name) =>
        File.ReadAllText(Path.Combine(root, "Pages", "Shared", "Components", "NavigationDrawer", name));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Project root could not be located.");
    }
}
