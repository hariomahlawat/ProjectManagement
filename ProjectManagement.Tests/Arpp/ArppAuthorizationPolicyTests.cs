using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppAuthorizationPolicyTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("HoD")]
    [InlineData("Comdt")]
    [InlineData("ProjectOffice")]
    [InlineData("Project Office")]
    [InlineData("MCO")]
    [InlineData("Project Officer")]
    public async Task ViewerPolicy_AllowsAuthorisedRoles(string role)
    {
        var result = await AuthorizeAsync(
            new AuthorizationPolicyBuilder().RequireArppViewer().Build(),
            role);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("HoD")]
    [InlineData("ProjectOffice")]
    [InlineData("Project Office")]
    public async Task ManagerPolicy_AllowsArppManagers(string role)
    {
        var result = await AuthorizeAsync(
            new AuthorizationPolicyBuilder().RequireArppManager().Build(),
            role);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Comdt")]
    [InlineData("MCO")]
    [InlineData("Project Officer")]
    public async Task ManagerPolicy_DeniesViewOnlyRoles(string role)
    {
        var result = await AuthorizeAsync(
            new AuthorizationPolicyBuilder().RequireArppManager().Build(),
            role);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ViewerPolicy_DeniesUnauthorisedAuthenticatedUser()
    {
        var result = await AuthorizeAsync(
            new AuthorizationPolicyBuilder().RequireArppViewer().Build(),
            role: null);

        Assert.False(result.Succeeded);
    }

    private static async Task<AuthorizationResult> AuthorizeAsync(
        AuthorizationPolicy policy,
        string? role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAuthorization();

        await using var provider = services.BuildServiceProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        Claim[] claims = role is null
            ? []
            : [new Claim(ClaimTypes.Role, role)];
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        return await authorizationService.AuthorizeAsync(user, resource: null, policy);
    }
}
