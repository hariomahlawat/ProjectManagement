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
