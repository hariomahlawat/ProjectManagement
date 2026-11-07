using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Tests.Admin.AccessGovernance;

public sealed class AdminCapabilityCatalogTests
{
    [Fact]
    public void Catalogue_HasUniqueKeysPoliciesAndValidRoleMappings()
    {
        var catalogue = new AdminCapabilityCatalog();
        var capabilities = catalogue.Capabilities;

        Assert.NotEmpty(capabilities);
        Assert.Equal(capabilities.Count, capabilities.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(capabilities.Count, capabilities.Select(item => item.Policy).Distinct(StringComparer.Ordinal).Count());
        Assert.All(capabilities, capability =>
        {
            Assert.False(string.IsNullOrWhiteSpace(capability.Title));
            Assert.False(string.IsNullOrWhiteSpace(capability.Description));
            Assert.False(string.IsNullOrWhiteSpace(capability.Policy));
            Assert.NotEmpty(capability.PermittedRoles);
            Assert.Equal(
                capability.PermittedRoles.Count,
                capability.PermittedRoles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });
    }

    [Fact]
    public void RegisteredCapabilities_CreateRolePolicies()
    {
        var options = new AuthorizationOptions();
        AdminCapabilityCatalog.RegisterPolicies(options);
        var catalogue = new AdminCapabilityCatalog();

        foreach (var capability in catalogue.Capabilities.Where(item => item.IsRegisteredByCatalog))
        {
            var policy = options.GetPolicy(capability.Policy);
            Assert.NotNull(policy);
            var roleRequirement = Assert.Single(policy!.Requirements.OfType<RolesAuthorizationRequirement>());
            Assert.Equal(
                capability.PermittedRoles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase),
                roleRequirement.AllowedRoles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void NavigationPolicies_AreRepresentedByTheGovernanceCatalogue()
    {
        var catalogue = new AdminCapabilityCatalog();

        foreach (var entry in AdminNavigationCatalog.Entries)
        {
            Assert.NotNull(catalogue.FindByPolicy(entry.Item.AuthorizationPolicy!));
        }
    }

    // Who has which role (docs/ux/admin-review-2-fixes.md, PR 7) rewrote the matrix's words. This
    // pins what each row grants, so wording changes can never move a permission.
    public static IEnumerable<object[]> ExpectedGrants() => new[]
    {
        Row("administration-access", AdminPolicies.Access, "Admin"),
        Row("users-manage", AdminPolicies.UsersManage, "Admin"),
        Row("access-governance", AdminPolicies.AccessGovernanceView, "Admin"),
        Row("security-view", AdminPolicies.SecurityView, "Admin"),
        Row("erp-usage-view", Policies.Usage.View, "Admin", "Comdt", "HoD"),
        Row("logs-view", AdminPolicies.LogsView, "Admin"),
        Row("recovery-manage", AdminPolicies.RecoveryManage, "Admin"),
        Row("master-data-manage", AdminPolicies.MasterDataManage, "Admin"),
        Row("integrity-manage", AdminPolicies.IntegrityManage, "Admin"),
        Row("activity-types-manage", AdminPolicies.ActivityTypesManage, "Admin", "HoD"),
        Row("holidays-manage", AdminPolicies.HolidaysManage, "Admin", "HoD"),
        Row("social-media-lists-manage", AdminPolicies.SocialMediaListsManage, "Admin", "HoD"),
        Row("celebrations-manage", Policies.Calendar.ManageCelebrations, "Admin", "TA", "Main_Office_Clerk", "Main Office"),
        Row("ingestion-manage", AdminPolicies.IngestionManage, "Admin"),
        Row("media-manage", AdminPolicies.MediaManage, "Admin", "HoD"),
        Row("media-view", AdminPolicies.MediaView, "Admin", "HoD"),
        Row("media-configure", AdminPolicies.MediaConfigure, "Admin", "HoD"),
        Row("media-operate", AdminPolicies.MediaOperateQueue, "Admin", "HoD"),
        Row("media-recover", AdminPolicies.MediaRecover, "Admin", "HoD"),
        Row("media-classification", AdminPolicies.MediaClassificationManage, "Admin", "HoD"),
        Row("docrepo-delete-approve", Policies.Documents.DeleteApprove, "Admin", "HoD"),
        Row("docrepo-categories", Policies.Documents.ManageCategories, "Admin"),
        Row("docrepo-purge", Policies.Documents.Purge, "Admin"),
    };

    [Theory]
    [MemberData(nameof(ExpectedGrants))]
    public void EveryMatrixRow_KeepsItsPolicyAndRoles(string key, string policy, string[] roles)
    {
        var capability = Assert.Single(new AdminCapabilityCatalog().Capabilities, item => item.Key == key);

        Assert.Equal(policy, capability.Policy);
        Assert.Equal(
            roles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase),
            capability.PermittedRoles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExpectedGrants_CoverEveryMatrixRow()
    {
        var pinned = ExpectedGrants().Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            pinned.OrderBy(key => key, StringComparer.Ordinal),
            new AdminCapabilityCatalog().Capabilities.Select(item => item.Key).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void SocialMediaListsPolicy_AllowsExactlyAdminAndHoD()
    {
        // Before PR 7 the pages said [Authorize(Roles = "Admin,HoD")]; the policy must be no wider
        // and no narrower.
        var options = new AuthorizationOptions();
        AdminCapabilityCatalog.RegisterPolicies(options);

        var policy = Assert.IsType<AuthorizationPolicy>(options.GetPolicy(AdminPolicies.SocialMediaListsManage));
        var requirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(new[] { "Admin", "HoD" }, requirement.AllowedRoles.OrderBy(role => role, StringComparer.Ordinal));
        Assert.All(policy.Requirements, item => Assert.True(
            item is RolesAuthorizationRequirement or DenyAnonymousAuthorizationRequirement,
            $"Unexpected requirement {item.GetType().Name}."));
    }

    [Theory]
    [InlineData(typeof(ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.IndexModel))]
    [InlineData(typeof(ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.NewModel))]
    [InlineData(typeof(ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.EditModel))]
    [InlineData(typeof(ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.Platforms.IndexModel))]
    [InlineData(typeof(ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.Platforms.NewModel))]
    [InlineData(typeof(ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.Platforms.EditModel))]
    public void SocialMediaListPages_RequireTheCataloguePolicyAndNoOtherGate(Type pageModel)
    {
        var authorize = Assert.Single(pageModel.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());

        Assert.Equal(AdminPolicies.SocialMediaListsManage, authorize.Policy);
        Assert.Null(authorize.Roles);
        Assert.Empty(pageModel.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    private static object[] Row(string key, string policy, params string[] roles) => new object[] { key, policy, roles };

    [Fact]
    public void CelebrationCapability_UsesTheExistingCalendarPolicyWithoutReregisteringIt()
    {
        var capability = Assert.IsType<AdminCapabilityDescriptor>(
            new AdminCapabilityCatalog().FindByPolicy(Policies.Calendar.ManageCelebrations));

        Assert.False(capability.IsRegisteredByCatalog);
        Assert.Equal(
            Policies.Calendar.CelebrationManagerRoles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase),
            capability.PermittedRoles.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
    }
}
