using System;
using System.IO;
using System.Linq;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests;

// The navigation drawer lists areas, not a module's internal pages or actions
// (docs/ux/navigation-drawer.md). Each destination the drawer stopped listing must keep
// another way in, or a role could lose its only route to a page. When a link named here
// moves, update the evidence rather than deleting the row.
public sealed class NavigationReachabilityTests
{
    public static TheoryData<string, string, string> LinkedFromPages => new()
    {
        // Document repository: its own page carries the upload button and the admin menu.
        { "DocumentRepository /Documents/Upload", "Areas/DocumentRepository/Pages/Documents/Index.cshtml", "asp-page=\"./Upload\"" },
        { "DocumentRepository /Admin/DeleteRequests/Index", "Areas/DocumentRepository/Pages/Documents/Index.cshtml", "asp-page=\"/Admin/DeleteRequests/Index\"" },
        { "DocumentRepository /Admin/Trash/Index", "Areas/DocumentRepository/Pages/Documents/Index.cshtml", "asp-page=\"/Admin/Trash/Index\"" },
        { "DocumentRepository /Admin/OCRFailures/OcrFailures", "Areas/DocumentRepository/Pages/Documents/Index.cshtml", "asp-page=\"/Admin/OCRFailures/OcrFailures\"" },
        { "DocumentRepository /Admin/OfficeCategories/Index", "Areas/DocumentRepository/Pages/Documents/Index.cshtml", "asp-page=\"/Admin/OfficeCategories/Index\"" },
        { "DocumentRepository /Admin/DocumentCategories/Index", "Areas/DocumentRepository/Pages/Documents/Index.cshtml", "asp-page=\"/Admin/DocumentCategories/Index\"" },

        // Visit types open from the Visits tracker.
        { "ProjectOfficeReports /VisitTypes/Index", "Areas/ProjectOfficeReports/Pages/Visits/Index.cshtml", "asp-page=\"/VisitTypes/Index\"" },

        // The FFC simulators list is the module root in the FFC map's breadcrumb.
        { "ProjectOfficeReports /FFC/Index", "Areas/ProjectOfficeReports/Pages/FFC/MapTableDetailed.cshtml.cs", "Url.Page(\"/FFC/Index\"" },
    };

    [Theory]
    [MemberData(nameof(LinkedFromPages))]
    public void RemovedDrawerDestination_IsStillLinkedFromItsModule(string destination, string evidenceFile, string link)
    {
        var source = File.ReadAllText(Path.Combine(FindProjectRoot(), evidenceFile));

        Assert.True(
            source.Contains(link, StringComparison.Ordinal),
            $"{destination} left the navigation drawer because {evidenceFile} links to it, but that link is gone.");
    }

    [Theory]
    [InlineData("/Admin/SocialMediaTypes/Index")]
    [InlineData("/Admin/SocialMediaTypes/Platforms/Index")]
    public void RemovedDrawerDestination_IsStillInTheAdministrationCatalog(string page)
    {
        // The catalog drives the Administration sidebar, sub-nav and "Every admin page".
        Assert.Contains(AdminNavigationCatalog.Entries, entry => entry.Item.Page == page);
    }

    [Fact]
    public void CreateProject_StaysAnActionOnTheProjectsSubNav()
    {
        var create = ProjectModuleNavDefinition.Build().Single(item => item.Page == "/Projects/Create");

        Assert.True(create.IsAction);
    }

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
