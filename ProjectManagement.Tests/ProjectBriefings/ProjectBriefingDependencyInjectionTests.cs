using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingDependencyInjectionTests
{
    [Fact]
    public void Program_RegistersUpdateSheetFactsResolverBeforeDataService()
    {
        var program = ReadRepoFile("Program.cs");

        const string resolverRegistration =
            "builder.Services.AddScoped<IProjectBriefingUpdateSheetFactsResolver, ProjectBriefingUpdateSheetFactsResolver>();";
        const string dataServiceRegistration =
            "builder.Services.AddScoped<IProjectBriefingDataService, ProjectBriefingDataService>();";

        var resolverIndex = program.IndexOf(resolverRegistration, StringComparison.Ordinal);
        var dataServiceIndex = program.IndexOf(dataServiceRegistration, StringComparison.Ordinal);

        Assert.True(resolverIndex >= 0,
            "The Project Update Sheet facts resolver must be registered in dependency injection.");
        Assert.True(dataServiceIndex > resolverIndex,
            "Register the facts resolver before ProjectBriefingDataService for a clear dependency order.");
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(relativePath));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file: {Path.Combine(relativePath)}");
    }
}
