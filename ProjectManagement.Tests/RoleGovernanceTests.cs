using System.Security.Claims;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class RoleGovernanceTests
{
    [Fact]
    public void AssignableRoleCatalog_ContainsElevenCanonicalRolesAndNoCompatibilityAliases()
    {
        Assert.Equal(11, RoleNames.AssignableRoles.Count);
        Assert.Contains(RoleNames.Ito, RoleNames.AssignableRoles);
        Assert.Contains(RoleNames.ProjectOffice, RoleNames.AssignableRoles);
        Assert.Contains(RoleNames.MainOfficeClerk, RoleNames.AssignableRoles);
        Assert.DoesNotContain(RoleNames.ProjectOfficeAlternate, RoleNames.AssignableRoles);
        Assert.DoesNotContain(RoleNames.MainOfficeAlternate, RoleNames.AssignableRoles);
        Assert.Equal(
            RoleNames.AssignableRoles.Count,
            RoleNames.AssignableRoles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(RoleNames.ProjectOfficeAlternate, RoleNames.ProjectOffice)]
    [InlineData(RoleNames.MainOfficeAlternate, RoleNames.MainOfficeClerk)]
    [InlineData(RoleNames.Ito, RoleNames.Ito)]
    public void Canonicalize_MapsOnlyKnownLegacyAliases(string supplied, string expected)
    {
        Assert.Equal(expected, RoleNames.Canonicalize(supplied));
    }

    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.Ito, true)]
    [InlineData(RoleNames.Admin, false)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    [InlineData(RoleNames.ProjectOffice, false)]
    public void SharedPublicationGovernance_IsLimitedToCommandantHodAndIto(string role, bool expected)
    {
        var principal = Principal(role);

        Assert.Equal(expected, Policies.Publications.CanManageSharedPublications(principal));
    }

    [Fact]
    public void ItoEffectivePermissions_IncludeSharedPublicationsWithoutCommandGovernance()
    {
        var catalog = new AdminRoleAccessCatalog();
        var access = catalog.ForRoles(
            new[] { RoleNames.Ito },
            RoleNames.AssignableRoles);

        Assert.Contains(access, item => item.Key == "publications-shared-manage");
        Assert.Contains(access, item => item.Key == "command-action-tracker");
        Assert.DoesNotContain(access, item => item.Key == "projects-govern");
        Assert.DoesNotContain(access, item => item.Key == "command-briefing-decks");
        Assert.DoesNotContain(access, item => item.Key == "command-conference");
        Assert.DoesNotContain(access, item => item.Key == "admin-users");
    }

    [Fact]
    public void TrainingTrackerViewerRoles_IncludeCanonicalMainOfficeClerkAndCompatibilityAlias()
    {
        Assert.Contains(RoleNames.MainOfficeClerk, ProjectOfficeReportsPolicies.TrainingTrackerViewerRoles);
        Assert.Contains(RoleNames.MainOfficeAlternate, ProjectOfficeReportsPolicies.TrainingTrackerViewerRoles);
    }

    [Fact]
    public void TrainingTrackerApproverRoles_AreCanonicalAdminAndHodOnly()
    {
        Assert.Equal(
            new[] { RoleNames.Admin, RoleNames.HoD },
            ProjectOfficeReportsPolicies.TrainingTrackerApproverRoles);
    }

    [Fact]
    public void IprRoleList_UsesCanonicalConstantsAndRetainsProjectOfficeCompatibility()
    {
        Assert.Contains(RoleNames.Admin, Policies.Ipr.EditAllowedRoles);
        Assert.Contains(RoleNames.HoD, Policies.Ipr.EditAllowedRoles);
        Assert.Contains(RoleNames.ProjectOffice, Policies.Ipr.EditAllowedRoles);
        Assert.Contains(RoleNames.ProjectOfficeAlternate, Policies.Ipr.EditAllowedRoles);
    }

    [Fact]
    public void ItoDescriptor_IsNonPrivilegedAndExplainsPublicationResponsibility()
    {
        var descriptor = new AdminRoleDescriptorCatalog().Describe(RoleNames.Ito);

        Assert.False(descriptor.IsPrivileged);
        Assert.Contains("Brochure", descriptor.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Compendium", descriptor.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimsPrincipal Principal(string role)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Role, role)
            },
            authenticationType: "TestAuth");

        return new ClaimsPrincipal(identity);
    }
}
