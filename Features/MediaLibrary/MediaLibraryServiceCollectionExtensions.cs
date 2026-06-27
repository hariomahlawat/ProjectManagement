using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
                npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable)));

        services.AddSingleton<IFileSystemPathResolver, FileSystemPathResolver>();
        services.AddSingleton<IMediaCachePathResolver, MediaCachePathResolver>();
        services.AddScoped<SafeFileEnumerator>();
        services.AddScoped<IFileSystemSourceHealthService, FileSystemSourceHealthService>();
        services.AddScoped<IMediaSourceBootstrapper, MediaSourceBootstrapper>();
        services.AddScoped<IPrismMediaCatalogueSynchronizer, PrismMediaCatalogueSynchronizer>();
        services.AddScoped<IExternalMediaSourceScanner, FileSystemMediaSourceScanner>();
        services.AddScoped<IExternalMediaLibraryReader, ExternalMediaLibraryReader>();
        services.AddScoped<IMediaLibraryQueryService, MediaLibraryQueryService>();
        services.AddScoped<IMediaMetadataReader, MediaMetadataReader>();
        services.AddScoped<IMediaClassifier, MediaClassifier>();
        services.AddScoped<IMediaDerivativeService, MediaDerivativeService>();
        services.AddScoped<IMediaAssetProcessor, MediaAssetProcessor>();

        var options = configuration.GetSection(MediaLibraryOptions.SectionName).Get<MediaLibraryOptions>()
            ?? new MediaLibraryOptions();

        // The workers are optional. Pages and PRISM-owned media remain available even
        // when the catalogue, external folders, or their schema are unavailable.
        if (options.IsCatalogueEnabled
            && (options.Catalogue.SynchronizePrismMedia || options.IsScannerWorkerEnabled))
        {
            services.AddHostedService<MediaSourceScannerWorker>();
        }

        if (options.IsProcessingWorkerEnabled)
        {
            services.AddHostedService<MediaProcessingWorker>();
        }

        return services;
    }
}
