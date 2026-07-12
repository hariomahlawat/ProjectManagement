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
        });
    }

    [Fact]
    public void RootDestinations_ExplicitlyClearTheAmbientAdminArea()
    {
        Assert.Equal(string.Empty, AdminNavigationCatalog.Get(AdminNavigationKeys.Holidays).Area);
        Assert.Equal(string.Empty, AdminNavigationCatalog.Get(AdminNavigationKeys.Celebrations).Area);
        Assert.Equal(string.Empty, AdminNavigationCatalog.Get(AdminNavigationKeys.ArchivedProjects).Area);
    }

    [Fact]
    public void SensitiveDestinations_UseExpectedPolicies()
    {
        Assert.Equal(AdminPolicies.UsersManage, AdminNavigationCatalog.Get(AdminNavigationKeys.Users).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.LogsView, AdminNavigationCatalog.Get(AdminNavigationKeys.Logs).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.ProjectTrash).AuthorizationPolicy);
        Assert.Equal(AdminPolicies.RecoveryManage, AdminNavigationCatalog.Get(AdminNavigationKeys.DocumentRecycle).AuthorizationPolicy);
    }
}
