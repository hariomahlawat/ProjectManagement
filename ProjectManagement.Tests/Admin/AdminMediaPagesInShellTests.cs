using Microsoft.AspNetCore.Http;
using ProjectManagement.Areas.Admin.Pages.AllPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// Owner decision 2, option A (docs/ux/admin-review-3-fixes.md, PR 13): Media sources, Media
// intelligence and Classification review render inside Administration, with its sidebar,
// header, eyebrow and "Administration" context, instead of leaving for the module layout.
public sealed class AdminMediaPagesInShellTests
{
    public static TheoryData<string, string> MediaPages => new()
    {
        { AdminNavigationKeys.MediaSources, "/Admin/MediaSources/Index" },
        { AdminNavigationKeys.MediaIntelligence, "/Admin/MediaIntelligence/Index" },
        { AdminNavigationKeys.MediaClassification, "/Admin/MediaIntelligence/Classifications" },
    };

    [Theory]
    [MemberData(nameof(MediaPages))]
    public void MediaPages_UseTheAdminShell(string key, string page)
    {
        var entry = AdminNavigationCatalog.GetEntry(key);

        Assert.True(entry.UsesAdminShell);
        Assert.Same(entry, AdminNavigationCatalog.FindActiveEntry(string.Empty, page, new QueryCollection()));
        // In scope means the layout picks the admin shell (for administrators) and the
        // "Administration ›" return bar stays away, because the shell already leads back.
        Assert.True(AdminNavigationCatalog.IsInScope(string.Empty, page, null, null, new QueryCollection()));
        Assert.Equal(entry.Item.Page, page);
        Assert.Equal(AdminPolicies.MediaView, entry.Item.AuthorizationPolicy);
    }

    [Theory]
    [MemberData(nameof(MediaPages))]
    public void MediaPages_ShowTheSystemEyebrow(string key, string page)
    {
        _ = key;
        Assert.Equal(AdminNavigationGroups.System, AdminNavigationCatalog.EyebrowFor(string.Empty, page, null, "Old eyebrow"));
    }

    [Fact]
    public void MediaIntelligencePages_HighlightMediaSourcesInTheSidebar()
    {
        Assert.True(AdminNavigationCatalog.GetEntry(AdminNavigationKeys.MediaSources).InSidebar);
        Assert.Equal(AdminNavigationKeys.MediaSources, AdminNavigationCatalog.GetEntry(AdminNavigationKeys.MediaIntelligence).SidebarParent);
        Assert.Equal(AdminNavigationKeys.MediaSources, AdminNavigationCatalog.GetEntry(AdminNavigationKeys.MediaClassification).SidebarParent);
    }

    [Theory]
    [MemberData(nameof(MediaPages))]
    public void MediaPages_AreNoLongerListedAsOpeningInAnotherModule(string key, string page)
    {
        _ = page;
        var entry = AdminNavigationCatalog.GetEntry(key);
        Assert.Equal("its own module", IndexModel.OwnModuleName(entry));
        Assert.True(entry.UsesAdminShell);
    }

    [Theory]
    [InlineData("Pages/Admin/MediaSources/Index.cshtml")]
    [InlineData("Pages/Admin/MediaIntelligence/Index.cshtml")]
    [InlineData("Pages/Admin/MediaIntelligence/Classifications.cshtml")]
    public void MediaPages_UseTheSharedAdminHeader_NotTheirOwn(string file)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), file));

        Assert.Contains("~/Areas/Admin/Pages/Shared/_AdminPageHeader.cshtml", text, StringComparison.Ordinal);
        // The old hand-made header and page containers would double the shell's padding.
        Assert.DoesNotContain("<h1 class=\"h3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("container-xxl py-4", text, StringComparison.Ordinal);
        Assert.DoesNotContain("container-fluid py-4", text, StringComparison.Ordinal);
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
