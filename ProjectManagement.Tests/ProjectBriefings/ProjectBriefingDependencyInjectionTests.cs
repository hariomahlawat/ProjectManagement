using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.Remarks;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingDependencyInjectionTests
{
    [Fact]
    public void ExternalStatusAdapter_HasOneUnambiguousDependencyInjectionConstructor()
    {
        var constructors = typeof(ProjectBriefingExternalStatusService).GetConstructors();
        var constructor = Assert.Single(constructors);
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(IProjectLatestExternalRemarkService), parameter.ParameterType);
    }

    [Fact]
    public void ExternalStatusAdapter_CanBeValidatedAndResolvedByDefaultContainer()
    {
        var services = new ServiceCollection();
        services.AddScoped<IProjectLatestExternalRemarkService, StubLatestExternalRemarkService>();
        services.AddScoped<IProjectBriefingExternalStatusService, ProjectBriefingExternalStatusService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IProjectBriefingExternalStatusService>();
        Assert.IsType<ProjectBriefingExternalStatusService>(resolved);
    }

    private sealed class StubLatestExternalRemarkService : IProjectLatestExternalRemarkService
    {
        public Task<IReadOnlyDictionary<int, ProjectLatestExternalRemark>> GetLatestAsync(
            IReadOnlyCollection<int> projectIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, ProjectLatestExternalRemark>>(
                new Dictionary<int, ProjectLatestExternalRemark>());
    }
}
