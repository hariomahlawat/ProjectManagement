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
        Assert.Equal(AdminPolicies.LogsView, AdminNavigationCatalog.Get(AdminNavigationKeys.Logs).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.RecoveryCentre).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.ProjectTrash).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.DocumentRecycle).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.DeletedEvents).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.MasterDataManage, AdminNavigationCatalog.Get(AdminNavigationKeys.MasterDataCentre).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.HolidaysManage, AdminNavigationCatalog.Get(AdminNavigationKeys.Holidays).AuthorizationPolicy);
        Assert.Equal(Policies.Calendar.ManageCelebrations, AdminNavigationCatalog.Get(AdminNavigationKeys.Celebrations).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.IngestionManage, AdminNavigationCatalog.Get(AdminNavigationKeys.PdfIngestion).AuthorizationPolicy);
    }

    [Theory]
    [InlineData("Admin", "/Users/Index", AdminNavigationKeys.Users)]
    [InlineData("Admin", "/Users/Edit", AdminNavigationKeys.Users)]
    [InlineData("Admin", "/Analytics/Index", AdminNavigationKeys.Logins)]
    [InlineData("Admin", "/Recovery/Index", AdminNavigationKeys.RecoveryCentre)]
    [InlineData("Admin", "/Projects/Archived", AdminNavigationKeys.ArchivedProjects)]
    [InlineData("Admin", "/MasterData/Index", AdminNavigationKeys.MasterDataCentre)]
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
