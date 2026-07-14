using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminPresentationCatalogTests
{
    [Fact]
    public void RoleCatalog_OrdersKnownRolesAndMarksPrivilegedAccess()
    {
        var catalog = new AdminRoleDescriptorCatalog();

        var roles = catalog.DescribeMany(new[] { RoleNames.ProjectOfficer, RoleNames.Admin, RoleNames.HoD });

        Assert.Equal(RoleNames.Admin, roles[0].Name);
        Assert.True(roles[0].IsPrivileged);
        Assert.True(roles[1].IsPrivileged);
        Assert.False(roles[2].IsPrivileged);
    }

    [Theory]
    [InlineData("Projects.ActualsUpdated", "Project actuals updated")]
    [InlineData("AdminUserPasswordReset", "User password reset")]
    [InlineData("CustomAuditAction", "Custom Audit Action")]
    [InlineData("MasterData.ProjectCategoryCreated", "Project category created")]
    public void AuditCatalog_ReturnsReadableLabels(string action, string expected)
    {
        var catalog = new AuditActionPresentationCatalog();

        var presentation = catalog.Describe(action);

        Assert.Equal(expected, presentation.Label);
    }
    [Fact]
    public void AuditCatalog_KnownRoutineAction_IgnoresBroadWarningLevel()
    {
        var catalog = new AuditActionPresentationCatalog();

        var presentation = catalog.Describe("Projects.MetaChangedDirect", "Warning");

        Assert.Equal("neutral", presentation.Tone);
        Assert.Equal("Project details changed", presentation.Label);
    }

}
