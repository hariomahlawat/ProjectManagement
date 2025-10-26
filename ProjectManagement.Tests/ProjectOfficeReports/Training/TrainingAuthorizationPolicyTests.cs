using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports.Training;

public class TrainingAuthorizationPolicyTests
{
    [Fact]
    public async Task RequireTrainingTrackerViewer_AllowsAuthenticatedUserWithoutRoles()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAuthorization();

        await using var provider = services.BuildServiceProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var policy = new AuthorizationPolicyBuilder()
            .RequireTrainingTrackerViewer()
            .Build();
        var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test"));

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task RequireTrainingTrackerManager_DeniesAuthenticatedUserWithoutRoles()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAuthorization();

        await using var provider = services.BuildServiceProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var policy = new AuthorizationPolicyBuilder()
            .RequireTrainingTrackerManager()
            .Build();
        var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test"));

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RequireTrainingTrackerManager_AllowsUserWithProjectOfficeRole()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAuthorization();

        await using var provider = services.BuildServiceProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var policy = new AuthorizationPolicyBuilder()
            .RequireTrainingTrackerManager()
            .Build();
        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim(identity.RoleClaimType, "ProjectOffice"));
        var user = new ClaimsPrincipal(identity);

        var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);

        Assert.True(result.Succeeded);
    }
}
