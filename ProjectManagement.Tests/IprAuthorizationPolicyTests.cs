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
    [InlineData("Project Officer")]
    [InlineData("TA")]
    [InlineData("ITO")]
    [InlineData("Main Office Clerk")]
    [InlineData("MC Cell Clerk")]
    [InlineData("IT Cell Clerk")]
    [InlineData(null)]
    public async Task ViewPolicy_AllowsEveryAuthenticatedUser(string? role)
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        await using var provider = CreateProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var user = CreatePrincipal(role, authenticated: true);

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ViewPolicy_DeniesAnonymousUser()
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        await using var provider = CreateProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var user = CreatePrincipal(role: null, authenticated: false);

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
        var user = CreatePrincipal(role, authenticated: true);

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
        var user = CreatePrincipal(role, authenticated: true);

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.False(result.Succeeded);
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

    private static ClaimsPrincipal CreatePrincipal(string? role, bool authenticated)
    {
        var identity = authenticated
            ? new ClaimsIdentity(authenticationType: "Test")
            : new ClaimsIdentity();

        if (!string.IsNullOrWhiteSpace(role))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

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
