using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;

namespace ProjectManagement.Tests.Infrastructure;

/// <summary>
/// Isolates WebApplicationFactory tests from developer and production PostgreSQL databases.
/// Both PRISM DbContexts are replaced because production startup treats them as one migration
/// boundary. Media hosted workers are removed so unit/API tests cannot touch the filesystem,
/// queues or catalogue in the background.
/// </summary>
public static class PrismTestWebHostBuilderExtensions
{
    public static IWebHostBuilder UsePrismTestInfrastructure(
        this IWebHostBuilder builder,
        string databaseNamePrefix)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseNamePrefix);

        var applicationDatabaseName = $"{databaseNamePrefix}-application-{Guid.NewGuid():N}";
        var mediaDatabaseName = $"{databaseNamePrefix}-media-{Guid.NewGuid():N}";

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Database=prism_test_bootstrap;Username=test;Password=test",
                ["DP_KEYS_DIR"] = Path.Combine(
                    Path.GetTempPath(),
                    "PRISM-ERP-Tests",
                    databaseNamePrefix,
                    Guid.NewGuid().ToString("N")),
                ["Database:RunSeedersOnStartup"] = "false",
                ["Audit:Retention:Enabled"] = "false",
                ["MediaLibrary:Enabled"] = "false",
                ["MediaLibrary:AutoMigrate"] = "false",
                ["MediaLibrary:Catalogue:Enabled"] = "false",
                ["MediaLibrary:Catalogue:SynchronizePrismMedia"] = "false",
                ["MediaLibrary:ExternalSources:Enabled"] = "false",
                ["MediaLibrary:ExternalSources:ScannerWorkerEnabled"] = "false",
                ["MediaLibrary:Processing:WorkerEnabled"] = "false",
                ["MediaLibrary:People:Enabled"] = "false",
                ["MediaLibrary:People:WorkerEnabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            RemoveDbContextRegistrations<ApplicationDbContext>(services);
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(applicationDatabaseName));

            RemoveDbContextRegistrations<MediaLibraryDbContext>(services);
            services.AddDbContext<MediaLibraryDbContext>(options =>
                options.UseInMemoryDatabase(mediaDatabaseName));

            var mediaHostedServices = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType?.Namespace?.StartsWith(
                        "ProjectManagement.Features.MediaLibrary.Hosted",
                        StringComparison.Ordinal) == true)
                .ToArray();

            foreach (var descriptor in mediaHostedServices)
            {
                services.Remove(descriptor);
            }
        });

        return builder;
    }

    private static void RemoveDbContextRegistrations<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        services.RemoveAll<TContext>();
        services.RemoveAll<DbContextOptions<TContext>>();

        // AddDbContext stores provider configuration in a generic infrastructure service.
        // Remove it without binding the test project to an EF implementation detail whose
        // public type location has changed between servicing releases.
        var optionConfigurationDescriptors = services
            .Where(descriptor =>
                descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GenericTypeArguments.Length == 1
                && descriptor.ServiceType.GenericTypeArguments[0] == typeof(TContext)
                && string.Equals(
                    descriptor.ServiceType.GetGenericTypeDefinition().Name,
                    "IDbContextOptionsConfiguration`1",
                    StringComparison.Ordinal))
            .ToArray();

        foreach (var descriptor in optionConfigurationDescriptors)
        {
            services.Remove(descriptor);
        }
    }
}
