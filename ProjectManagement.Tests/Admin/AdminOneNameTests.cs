using System.Text.RegularExpressions;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// One name for each thing (docs/ux/admin-review-fixes.md, PR 7): the name in the sidebar, the
// search, Every admin page and the links that open a page is the name the page itself shows,
// and a role reads the same on every page.
public sealed class AdminOneNameTests
{
    // Home is titled "Administration": it is the module's front page, not a page within it.
    private static readonly HashSet<string> OwnTitle = new(StringComparer.Ordinal) { AdminNavigationKeys.Dashboard };

    [Fact]
    public void EveryCataloguePage_IsTitledWithItsCatalogueName()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (var entry in AdminNavigationCatalog.Entries.Where(entry => !OwnTitle.Contains(entry.Key)))
        {
            var basePath = Path.Combine(
                new[] { root }
                    .Concat(string.IsNullOrEmpty(entry.Item.Area) ? new[] { "Pages" } : new[] { "Areas", entry.Item.Area!, "Pages" })
                    .Concat(entry.Item.Page!.TrimStart('/').Split('/'))
                    .ToArray());
            var title = PageTitle(basePath);
            if (title is null)
            {
                continue; // The title comes from code, such as Every admin page's catalogue lookup.
            }

            if (!string.Equals(title, entry.Item.Text, StringComparison.Ordinal))
            {
                offenders.Add($"{entry.Item.Page}: page says \"{title}\", catalogue says \"{entry.Item.Text}\"");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void CapabilityLinks_UseTheNameOfThePageTheyOpen()
    {
        var offenders = new AdminCapabilityCatalog().Capabilities
            .SelectMany(capability => capability.Routes.Select(route => (capability.Key, Route: route)))
            .Select(item => (item.Key, item.Route, Entry: AdminNavigationCatalog.Entries.FirstOrDefault(entry =>
                string.Equals(entry.Item.Area ?? string.Empty, item.Route.Area, StringComparison.Ordinal)
                && string.Equals(entry.Item.Page, item.Route.Page, StringComparison.Ordinal)
                && (entry.Item.RouteValues is null || entry.Item.RouteValues.Count == 0))))
            .Where(item => item.Entry is not null && item.Entry.Key != AdminNavigationKeys.Dashboard)
            .Where(item => !string.Equals(item.Route.Label, item.Entry!.Item.Text, StringComparison.Ordinal))
            .Select(item => $"{item.Key}: \"{item.Route.Label}\" should be \"{item.Entry!.Item.Text}\"")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData(RoleNames.Admin, "Administrator")]
    [InlineData(RoleNames.HoD, "Head of Department")]
    [InlineData(RoleNames.MainOfficeClerk, "Main Office Clerk")]
    [InlineData(RoleNames.MainOfficeAlternate, "Main Office Clerk (old role name)")]
    [InlineData(RoleNames.ProjectOfficeAlternate, "Project Office (old role name)")]
    public void RoleNames_ReadTheSameEverywhere(string role, string expected)
    {
        Assert.Equal(expected, new AdminRoleDescriptorCatalog().Describe(role).DisplayName);
    }

    [Fact]
    public void RolePickersAndBadges_UseTheRoleMap()
    {
        var root = FindRepositoryRoot();
        foreach (var view in new[] { "Areas/Admin/Pages/Users/Index.cshtml", "Pages/Usage/Index.cshtml" })
        {
            var text = File.ReadAllText(Path.Combine(root, view));
            Assert.Contains("IAdminRoleDescriptorCatalog", text, StringComparison.Ordinal);
            Assert.DoesNotContain("<option value=\"@role\">@role</option>", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LegacyAliases_AreTheOldRoleNames()
    {
        Assert.Equal(
            new[] { RoleNames.ProjectOfficeAlternate, RoleNames.MainOfficeAlternate },
            RoleNames.LegacyAliases.ToArray());
        Assert.All(RoleNames.LegacyAliases, alias => Assert.True(RoleNames.IsLegacyAlias(alias)));
    }

    private static string? PageTitle(string basePath)
    {
        var view = basePath + ".cshtml";
        if (File.Exists(view))
        {
            var match = Regex.Match(File.ReadAllText(view), @"ViewData\[""Title""\]\s*=\s*""(?<text>[^""]*)""");
            if (match.Success)
            {
                return match.Groups["text"].Value;
            }
        }

        var model = basePath + ".cshtml.cs";
        if (File.Exists(model))
        {
            var match = Regex.Match(File.ReadAllText(model), @"\bTitle\s*=\s*""(?<text>[^""]*)""");
            if (match.Success)
            {
                return match.Groups["text"].Value;
            }
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
