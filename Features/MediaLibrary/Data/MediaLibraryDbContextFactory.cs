using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ProjectManagement.Features.MediaLibrary.Data;

public sealed class MediaLibraryDbContextFactory : IDesignTimeDbContextFactory<MediaLibraryDbContext>
{
    public MediaLibraryDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is required. Set ConnectionStrings__DefaultConnection for the selected environment.");
        }

        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable))
            .Options;

        return new MediaLibraryDbContext(options);
    }
}
