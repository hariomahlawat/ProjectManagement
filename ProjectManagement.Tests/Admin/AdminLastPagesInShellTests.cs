using Microsoft.AspNetCore.Http;
using ProjectManagement.Areas.Admin.Pages.MasterData;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// Fourth admin review, PR 6 (docs/ux/admin-review-4-fixes.md): Unreadable PDFs and Missing
// files render inside Administration with one way back, and "Lists used in forms" only says
// "Opens in …" for a list that really leaves Administration.
public sealed class AdminLastPagesInShellTests
{
    private static readonly IQueryCollection NoQuery = new QueryCollection();

    public static TheoryData<string, string> Pages => new()
    {
        { AdminNavigationKeys.UnreadablePdfs, "/Admin/OCRFailures/OcrFailures" },
        { AdminNavigationKeys.MissingDocumentFiles, "/Admin/MissingFiles" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void Page_RendersInsideTheAdminShell_UnderSystemHealth(string key, string page)
    {
        var entry = AdminNavigationCatalog.GetEntry(key);

        Assert.True(entry.UsesAdminShell);
        Assert.False(entry.InSidebar);
        Assert.Equal(AdminNavigationKeys.DatabaseHealth, entry.SidebarParent);
        Assert.Same(entry, AdminNavigationCatalog.FindActiveEntry("DocumentRepository", page, NoQuery));
        Assert.True(AdminNavigationCatalog.IsInScope("DocumentRepository", page, null, null, NoQuery));
        Assert.Equal(AdminNavigationGroups.System, AdminNavigationCatalog.EyebrowFor("DocumentRepository", page, NoQuery, null));
        // The library itself keeps its own layout.
        Assert.False(AdminNavigationCatalog.IsInScope("DocumentRepository", "/Documents/Index", null, null, NoQuery));
    }

    [Theory]
    [InlineData("Areas/DocumentRepository/Pages/Admin/OCRFailures/OcrFailures.cshtml")]
    [InlineData("Areas/DocumentRepository/Pages/Admin/MissingFiles.cshtml")]
    public void Page_UsesTheAdminHeader_AndShowsTheLibraryLinkOnlyOutsideTheShell(string file)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), file));

        Assert.Contains("~/Areas/Admin/Pages/Shared/_AdminPageHeader.cshtml", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("library-head", text, StringComparison.Ordinal);
        // One way back: administrators have the sidebar; the library link is for everyone else.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "Back to the library"));
        Assert.Contains("!User.IsInRole(ProjectManagement.Configuration.RoleNames.Admin)", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DocumentRepository", "/Admin/DocumentCategories/Index")]
    [InlineData("DocumentRepository", "/Admin/OfficeCategories/Index")]
    [InlineData("ProjectOfficeReports", "/Admin/SocialMediaTypes/Index")]
    [InlineData("ProjectOfficeReports", "/Admin/SocialMediaTypes/Platforms/Index")]
    [InlineData("Admin", "/MasterData/SponsoringUnits/Index")]
    public void ListsThatOpenInAdministration_HaveNoOpensInHint(string area, string page)
    {
        Assert.Null(IndexModel.OpensIn(Domain(area, page)));
    }

    [Fact]
    public void AListThatLeavesAdministration_StillSaysWhere()
    {
        Assert.Equal("the Document Repository", IndexModel.OpensIn(Domain("DocumentRepository", "/Documents/Index")));
        Assert.Equal("Project office reports", IndexModel.OpensIn(Domain("ProjectOfficeReports", "/Visits/Index")));
    }

    private static MasterDataDomainSummary Domain(string area, string page) =>
        new("key", "Title", "Description", "bi-list", area, page, 0, 0, 0, 0, "Policy");

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
