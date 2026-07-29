using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminRoleAccessCatalogTests
{
    [Fact]
    public void ProjectOfficer_AccessReflectsAssignedProjectAndOperationalPermissions()
    {
        var items = ItemsFor(RoleNames.ProjectOfficer);

        Assert.Contains(items, item => item.Key == "projects-maintain");
        Assert.Contains(items, item => item.Key == "projects-request-change");
        Assert.Contains(items, item => item.Key == "command-action-tracker");
        Assert.Contains(items, item => item.Key == "industry-create");
        Assert.Contains(items, item => item.Key == "industry-edit-own");
        Assert.DoesNotContain(items, item => item.Key == "industry-edit-any");
        Assert.DoesNotContain(items, item => item.Key == "admin-users");
    }

    [Fact]
    public void Commandant_AccessIncludesCommandBriefingAndDirectoryOverrideButNotDeletion()
    {
        var items = ItemsFor(RoleNames.Comdt);

        Assert.Contains(items, item => item.Key == "command-briefing-decks");
        Assert.Contains(items, item => item.Key == "command-conference");
        Assert.Contains(items, item => item.Key == "industry-create");
        Assert.Contains(items, item => item.Key == "industry-edit-any");
        Assert.DoesNotContain(items, item => item.Key == "industry-delete");
        Assert.DoesNotContain(items, item => item.Key == "reports-arpp-unlock");
    }

    [Fact]
    public void ProjectOffice_AccessIncludesReportManagementAndRepositoryUpload()
    {
        var items = ItemsFor(RoleNames.ProjectOffice);

        Assert.Contains(items, item => item.Key == "reports-visits-social");
        Assert.Contains(items, item => item.Key == "reports-training-manage");
        Assert.Contains(items, item => item.Key == "reports-arpp-manage");
        Assert.Contains(items, item => item.Key == "documents-upload-delete-request");
        Assert.DoesNotContain(items, item => item.Key == "documents-metadata");
    }

    [Fact]
    public void UnknownConfiguredRole_ReceivesOnlyCommonAuthenticatedCapabilities()
    {
        const string customRole = "Custom Observer";
        var catalog = new AdminRoleAccessCatalog();

        var items = catalog.ForRoles(new[] { customRole }, new[] { customRole });

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.StartsWith("core-", item.Key));
    }

    private static IReadOnlyList<AdminRoleAccessItem> ItemsFor(string roleName)
    {
        var catalog = new AdminRoleAccessCatalog();
        return catalog.ForRoles(new[] { roleName }, new[] { roleName });
    }
}
