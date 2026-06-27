using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Hosted;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary;

public static class MediaLibraryServiceCollectionExtensions
{
    public static IServiceCollection AddMediaLibrary(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services
            .AddOptions<MediaLibraryOptions>()
            .Bind(configuration.GetSection(MediaLibraryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MediaLibraryOptions>, MediaLibraryOptionsValidator>();

        services.AddDbContext<MediaLibraryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable);
            }));

        services.AddSingleton<INetworkSharePathResolver, NetworkSharePathResolver>();
        services.AddSingleton<IMediaCachePathResolver, MediaCachePathResolver>();
        services.AddScoped<SafeFileEnumerator>();
        services.AddScoped<IMediaSourceBootstrapper, MediaSourceBootstrapper>();
        services.AddScoped<IPrismMediaCatalogueSynchronizer, PrismMediaCatalogueSynchronizer>();
        services.AddScoped<INetworkMediaSourceScanner, NetworkMediaSourceScanner>();
        services.AddScoped<IMediaMetadataReader, MediaMetadataReader>();
        services.AddScoped<IMediaClassifier, MediaClassifier>();
        services.AddScoped<IMediaDerivativeService, MediaDerivativeService>();
        services.AddScoped<IMediaAssetProcessor, MediaAssetProcessor>();

        var options = configuration.GetSection(MediaLibraryOptions.SectionName).Get<MediaLibraryOptions>()
            ?? new MediaLibraryOptions();

        if (options.Enabled && options.ScannerWorkerEnabled)
        {
            services.AddHostedService<MediaSourceScannerWorker>();
        }

        if (options.Enabled && options.ProcessingWorkerEnabled)
        {
            services.AddHostedService<MediaProcessingWorker>();
        }

        return services;
    }
}
