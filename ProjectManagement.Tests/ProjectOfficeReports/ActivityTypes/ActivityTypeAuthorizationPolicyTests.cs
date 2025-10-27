using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports.ActivityTypes;

public class ActivityTypeAuthorizationPolicyTests
{
    [Theory]
    [InlineData("Project Officer")]
    [InlineData("TA")]
    public async Task RequireActivityTypeViewer_AllowsStaffRoles(string role)
    {
        var (authorizationService, policy) = BuildPolicy(builder => builder.RequireActivityTypeViewer());
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, role)
        }, "Test"));

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Main Office")]
    [InlineData("MCO")]
    public async Task RequireActivityTypeViewer_AllowsViewOnlyRoles(string role)
    {
        var (authorizationService, policy) = BuildPolicy(builder => builder.RequireActivityTypeViewer());
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, role)
        }, "Test"));

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task RequireActivityTypeViewer_DeniesUserWithoutRole()
    {
        var (authorizationService, policy) = BuildPolicy(builder => builder.RequireActivityTypeViewer());
        var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test"));

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("HoD")]
    public async Task RequireActivityTypeManager_AllowsManagers(string role)
    {
        var (authorizationService, policy) = BuildPolicy(builder => builder.RequireActivityTypeManager());
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, role)
        }, "Test"));

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Project Officer")]
    [InlineData("Main Office")]
    public async Task RequireActivityTypeManager_DeniesNonManagers(string role)
    {
        var (authorizationService, policy) = BuildPolicy(builder => builder.RequireActivityTypeManager());
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, role)
        }, "Test"));

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.False(result.Succeeded);
    }

    private static (IAuthorizationService AuthorizationService, AuthorizationPolicy Policy) BuildPolicy(
        Func<AuthorizationPolicyBuilder, AuthorizationPolicyBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAuthorization();

        var provider = services.BuildServiceProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var policy = configure(new AuthorizationPolicyBuilder()).Build();
        return (authorizationService, policy);
    }
}
