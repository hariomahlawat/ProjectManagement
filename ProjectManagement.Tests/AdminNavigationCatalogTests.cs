using Microsoft.AspNetCore.Http;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminNavigationCatalogTests
{
    [Fact]
    public void Catalog_HasUniqueKeysAndValidDestinations()
    {
        var entries = AdminNavigationCatalog.Entries;

        Assert.NotEmpty(entries);
        Assert.Equal(entries.Count, entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Item.Text));
            Assert.False(string.IsNullOrWhiteSpace(entry.Item.Page));
            Assert.False(string.IsNullOrWhiteSpace(entry.Item.AuthorizationPolicy));
            Assert.Contains(entry.Group, AdminNavigationGroups.Ordered);
        });
    }

    [Fact]
    public void RootDestinations_ExplicitlyClearTheAmbientAdminArea()
    {
        Assert.Equal(string.Empty, AdminNavigationCatalog.Get(AdminNavigationKeys.Holidays).Area);
        Assert.Equal(string.Empty, AdminNavigationCatalog.Get(AdminNavigationKeys.Celebrations).Area);
    }

    [Fact]
    public void AdministrationDestinations_UseDedicatedAdminRoutes()
    {
        Assert.Equal("Admin", AdminNavigationCatalog.Get(AdminNavigationKeys.RecoveryCentre).Area);
        Assert.Equal("Admin", AdminNavigationCatalog.Get(AdminNavigationKeys.ArchivedProjects).Area);
        Assert.Equal("Admin", AdminNavigationCatalog.Get(AdminNavigationKeys.MaintenanceCentre).Area);
        Assert.Equal("Admin", AdminNavigationCatalog.Get(AdminNavigationKeys.MasterDataCentre).Area);
    }

    [Fact]
    public void SensitiveDestinations_UseExpectedPolicies()
    {
        Assert.Equal(AdminPolicies.UsersManage, AdminNavigationCatalog.Get(AdminNavigationKeys.Users).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.AccessGovernanceView, AdminNavigationCatalog.Get(AdminNavigationKeys.AccessGovernance).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.LogsView, AdminNavigationCatalog.Get(AdminNavigationKeys.Logs).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.RecoveryCentre).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.ProjectTrash).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.DocumentRecycle).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.DeletedEvents).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.MasterDataManage, AdminNavigationCatalog.Get(AdminNavigationKeys.MasterDataCentre).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.IntegrityManage, AdminNavigationCatalog.Get(AdminNavigationKeys.MasterDataIntegrity).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.HolidaysManage, AdminNavigationCatalog.Get(AdminNavigationKeys.Holidays).AuthorizationPolicy);
        Assert.Equal(Policies.Calendar.ManageCelebrations, AdminNavigationCatalog.Get(AdminNavigationKeys.Celebrations).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.IngestionManage, AdminNavigationCatalog.Get(AdminNavigationKeys.PdfIngestion).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.SecurityView, AdminNavigationCatalog.Get(AdminNavigationKeys.SearchIndex).AuthorizationPolicy);
    }

    [Theory]
    [InlineData("Admin", "/Users/Index", AdminNavigationKeys.Users)]
    [InlineData("Admin", "/Users/Edit", AdminNavigationKeys.Users)]
    [InlineData("Admin", "/AccessGovernance/Index", AdminNavigationKeys.AccessGovernance)]
    [InlineData("Admin", "/Analytics/Index", AdminNavigationKeys.Logins)]
    [InlineData("Admin", "/Diagnostics/SearchIndex", AdminNavigationKeys.SearchIndex)]
    [InlineData("Admin", "/Recovery/Index", AdminNavigationKeys.RecoveryCentre)]
    [InlineData("Admin", "/Projects/Archived", AdminNavigationKeys.ArchivedProjects)]
    [InlineData("Admin", "/MasterData/Index", AdminNavigationKeys.MasterDataCentre)]
    [InlineData("Admin", "/MasterData/Integrity/Index", AdminNavigationKeys.MasterDataIntegrity)]
    [InlineData("Admin", "/Lookups/ProjectTypes/Edit", AdminNavigationKeys.ProjectTypes)]
    [InlineData("", "/Settings/Holidays/Edit", AdminNavigationKeys.Holidays)]
    [InlineData("", "/Celebrations/Edit", AdminNavigationKeys.Celebrations)]
    [InlineData("ProjectOfficeReports", "/Projects/LegacyImport", AdminNavigationKeys.LegacyImport)]
    public void FindActiveEntry_UsesStableSectionMatching(
        string area,
        string page,
        string expectedKey)
    {
        var match = AdminNavigationCatalog.FindActiveEntry(area, page, new QueryCollection());

        Assert.NotNull(match);
        Assert.Equal(expectedKey, match!.Key);
    }

    [Fact]
    public void OrdinaryProjectDirectory_IsNotClassifiedAsAdministration()
    {
        var ordinaryProjects = AdminNavigationCatalog.FindActiveEntry(
            string.Empty,
            "/Projects/Index",
            new QueryCollection());

        Assert.Null(ordinaryProjects);
    }

    // docs/ux/admin-module.md: an administrator can find every page and setting, and every
    // page says in one sentence what it is for.
    [Fact]
    public void EveryEntry_SaysWhatItIsForAndHowPeopleSearchForIt()
    {
        Assert.All(AdminNavigationCatalog.Entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Description), $"{entry.Key} needs a description.");
            Assert.EndsWith(".", entry.Description.Trim());
            Assert.False(string.IsNullOrWhiteSpace(entry.Keywords), $"{entry.Key} needs search words.");
        });
    }

    [Fact]
    public void EveryEntry_OpensAPageThatExists()
    {
        var root = RepositoryRoot();
        Assert.All(AdminNavigationCatalog.Entries, entry =>
        {
            var area = entry.Item.Area ?? string.Empty;
            var pagesFolder = string.IsNullOrEmpty(area)
                ? Path.Combine(root, "Pages")
                : Path.Combine(root, "Areas", area, "Pages");
            var file = Path.Combine(pagesFolder, entry.Item.Page!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar) + ".cshtml");
            Assert.True(File.Exists(file), $"{entry.Key} points to {area}{entry.Item.Page}, which does not exist.");
        });
    }

    [Fact]
    public void Sidebar_IsShortAndGroupedByTask()
    {
        var sidebar = AdminNavigationCatalog.Entries.Where(entry => entry.InSidebar).ToArray();

        Assert.InRange(sidebar.Length, 10, 20);
        Assert.Equal(AdminNavigationCatalog.BuildModuleItems().Count, sidebar.Length);
        Assert.All(AdminNavigationGroups.Ordered, group => Assert.Contains(sidebar, entry => entry.Group == group));
    }

    [Fact]
    public void PagesOutsideTheSidebar_PointToASidebarEntryOrStaySearchable()
    {
        var byKey = AdminNavigationCatalog.Entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        Assert.All(AdminNavigationCatalog.Entries.Where(entry => entry.SidebarParent is not null), entry =>
        {
            Assert.False(entry.InSidebar);
            Assert.True(byKey.TryGetValue(entry.SidebarParent!, out var parent));
            Assert.True(parent!.InSidebar);
        });
    }

    [Fact]
    public void DocumentDeletionApprovals_IsTheDecisionCentreFilteredToRepositoryDeletions()
    {
        var entry = AdminNavigationCatalog.GetEntry(AdminNavigationKeys.DocumentDeleteRequests);
        var repositoryDeletions = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["Type"] = "RepositoryDocumentDelete" });

        Assert.Equal("/Approvals/Pending/Index", entry.Item.Page);
        Assert.Equal("RepositoryDocumentDelete", entry.Item.RouteValues!["Type"]);
        Assert.Same(entry, AdminNavigationCatalog.FindActiveEntry("", "/Approvals/Pending/Index", repositoryDeletions));
        // The Decision Centre unfiltered is not an Administration page.
        Assert.Null(AdminNavigationCatalog.FindActiveEntry("", "/Approvals/Pending/Index", new QueryCollection()));
        Assert.False(AdminNavigationCatalog.IsInScope("", "/Approvals/Pending/Index", null, null, repositoryDeletions));
    }

    [Fact]
    public void HubPages_HighlightTheirSidebarEntry()
    {
        Assert.Equal(
            AdminNavigationKeys.RecoveryCentre,
            AdminNavigationCatalog.GetEntry(AdminNavigationKeys.ProjectTrash).SidebarParent);
        Assert.Equal(
            AdminNavigationKeys.MasterDataCentre,
            AdminNavigationCatalog.GetEntry(AdminNavigationKeys.SponsoringUnits).SidebarParent);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }

    [Fact]
    public void AdminArea_IsAlwaysRecognisedAsAdministrationScope()
    {
        Assert.True(AdminNavigationCatalog.IsInScope(
            "Admin",
            "/Uncatalogued/Diagnostic",
            null,
            null,
            new QueryCollection()));
    }
}
