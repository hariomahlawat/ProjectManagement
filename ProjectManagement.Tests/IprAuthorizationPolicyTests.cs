using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Configuration;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class IprAuthorizationPolicyTests
{
    [Theory]
    [MemberData(nameof(ViewAllowedRoles))]
    public async Task ViewPolicy_AllowsExpectedRoles(string role)
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireRole(Policies.Ipr.ViewAllowedRoles)
            .Build();

        await using var provider = CreateProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var user = CreatePrincipalWithRole(role);

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Project Officer")]
    [InlineData("TA")]
    [InlineData(null)]
    public async Task ViewPolicy_DeniesUnauthorizedRoles(string? role)
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireRole(Policies.Ipr.ViewAllowedRoles)
            .Build();

        await using var provider = CreateProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var user = CreatePrincipalWithRole(role);

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(EditAllowedRoles))]
    public async Task EditPolicy_AllowsExpectedRoles(string role)
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireRole(Policies.Ipr.EditAllowedRoles)
            .Build();

        await using var provider = CreateProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var user = CreatePrincipalWithRole(role);

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Comdt")]
    [InlineData("MCO")]
    [InlineData("Project Officer")]
    [InlineData(null)]
    public async Task EditPolicy_DeniesUnauthorizedRoles(string? role)
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireRole(Policies.Ipr.EditAllowedRoles)
            .Build();

        await using var provider = CreateProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var user = CreatePrincipalWithRole(role);

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.False(result.Succeeded);
    }

    public static TheoryData<string> ViewAllowedRoles()
    {
        var data = new TheoryData<string>();
        foreach (var role in Policies.Ipr.ViewAllowedRoles)
        {
            data.Add(role);
        }

        return data;
    }

    public static TheoryData<string> EditAllowedRoles()
    {
        var data = new TheoryData<string>();
        foreach (var role in Policies.Ipr.EditAllowedRoles)
        {
            data.Add(role);
        }

        return data;
    }

    private static ClaimsPrincipal CreatePrincipalWithRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test"));
        }

        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(identity);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAuthorization();
        return services.BuildServiceProvider();
    }
}
