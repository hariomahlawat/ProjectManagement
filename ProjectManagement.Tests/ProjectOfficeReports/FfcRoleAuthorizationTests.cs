using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcRoleAuthorizationTests
{
    [Fact]
    public void FfcManagerRoles_AreAdminHodComdtAndItoOnly()
    {
        Assert.Equal(
            new[] { RoleNames.Admin, RoleNames.HoD, RoleNames.Comdt, RoleNames.Ito },
            ProjectOfficeReportsPolicies.FfcManagerRoles);

        Assert.DoesNotContain(RoleNames.ProjectOffice, ProjectOfficeReportsPolicies.FfcManagerRoles);
        Assert.DoesNotContain(RoleNames.ProjectOfficer, ProjectOfficeReportsPolicies.FfcManagerRoles);
    }

    [Fact]
    public void FfcInlineEditors_ExcludeItoButRetainCommandManagers()
    {
        Assert.Equal(
            new[] { RoleNames.Admin, RoleNames.HoD, RoleNames.Comdt },
            ProjectOfficeReportsPolicies.FfcInlineEditorRoles);

        Assert.DoesNotContain(RoleNames.Ito, ProjectOfficeReportsPolicies.FfcInlineEditorRoles);
    }

    [Theory]
    [InlineData(RoleNames.Admin, true, true)]
    [InlineData(RoleNames.HoD, true, true)]
    [InlineData(RoleNames.Comdt, true, true)]
    [InlineData(RoleNames.Ito, true, false)]
    [InlineData(RoleNames.ProjectOffice, false, false)]
    [InlineData(RoleNames.ProjectOfficer, false, false)]
    [InlineData(RoleNames.Mco, false, false)]
    public void FfcAuthorizationHelpers_ApplyTheIntendedRoleMatrix(
        string role,
        bool canManage,
        bool canInlineEdit)
    {
        var principal = Principal(role);

        Assert.Equal(canManage, ProjectOfficeReportsPolicies.CanManageFfc(principal));
        Assert.Equal(canInlineEdit, ProjectOfficeReportsPolicies.CanInlineEditFfc(principal));
    }


    [Fact]
    public void ItoRemarkRole_IsFfcScopedAndNotParsedByGenericProjectRemarkAuthoring()
    {
        var parsed = RemarkActorRoleExtensions.TryParse(RoleNames.Ito, out var role);

        Assert.False(parsed);
        Assert.Equal(RemarkActorRole.Unknown, role);
        Assert.Equal(9, (int)RemarkActorRole.Ito);
    }

    [Fact]
    public void EffectivePermissions_DescribeItoManagementWithoutInlineEditAuthority()
    {
        var catalog = new AdminRoleAccessCatalog();
        var availableRoles = RoleNames.AssignableRoles;
        var itoItems = catalog.ForRoles(new[] { RoleNames.Ito }, availableRoles);

        Assert.Contains(itoItems, item => item.Key == "reports-ffc-manage");
        Assert.DoesNotContain(itoItems, item => item.Key == "reports-ffc-inline-edit");

        var comdtItems = catalog.ForRoles(new[] { RoleNames.Comdt }, availableRoles);
        Assert.Contains(comdtItems, item => item.Key == "reports-ffc-manage");
        Assert.Contains(comdtItems, item => item.Key == "reports-ffc-inline-edit");
    }

    private static ClaimsPrincipal Principal(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "ffc-test-user"),
            new(ClaimTypes.Name, "ffc-test-user")
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
