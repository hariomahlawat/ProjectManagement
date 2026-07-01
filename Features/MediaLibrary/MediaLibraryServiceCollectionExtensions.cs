using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Hosted;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Outbox;
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

        var configuredOptions = configuration
            .GetSection(MediaLibraryOptions.SectionName)
            .Get<MediaLibraryOptions>() ?? new MediaLibraryOptions();

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
        services.AddSingleton<IMediaProcessingRuntimeState>(_ =>
        {
            var state = new MediaProcessingRuntimeState();
            state.MarkConfigured(configuredOptions.IsAnyProcessingWorkerEnabled);
            return state;
        });
        services.AddScoped<IMediaCacheHealthService, MediaCacheHealthService>();
        services.AddScoped<SafeFileEnumerator>();
        services.AddScoped<IFileSystemSourceHealthService, FileSystemSourceHealthService>();
        services.AddScoped<IMediaSourceBootstrapper, MediaSourceBootstrapper>();
        services.AddScoped<IPrismMediaSourceSnapshotService, PrismMediaSourceSnapshotService>();
        services.AddSingleton<IPrismMediaSynchronizationGate, PrismMediaSynchronizationGate>();
        services.AddSingleton<IPrismMediaOutboxSignal, PrismMediaOutboxSignal>();
        services.AddSingleton<IPrismMediaOutboxRuntimeState, PrismMediaOutboxRuntimeState>();
        services.AddScoped<IPrismMediaCatalogueSynchronizer, PrismMediaCatalogueSynchronizer>();
        services.AddScoped<IPrismActivityMediaIngestionService, PrismActivityMediaIngestionService>();
        services.AddScoped<IPrismMediaIngestionCoordinator, PrismMediaIngestionCoordinator>();
        services.AddScoped<IExternalMediaSourceScanner, FileSystemMediaSourceScanner>();
        services.AddScoped<IExternalMediaLibraryReader, ExternalMediaLibraryReader>();
        services.AddSingleton<IMediaLibraryDiagnostics, MediaLibraryDiagnostics>();
        services.AddScoped<IMediaLibraryHealthService, MediaLibraryHealthService>();
        services.AddScoped<IMediaCatalogueConsistencyService, MediaCatalogueConsistencyService>();
        services.AddScoped<IMediaAvailabilityRecoveryService, MediaAvailabilityRecoveryService>();
        services.AddScoped<IMediaLibraryQueryService, MediaLibraryQueryService>();
        services.AddScoped<IMediaPeopleQueryService, MediaPeopleQueryService>();
        services.AddScoped<IMediaContentProvider, FileSystemMediaContentProvider>();
        services.AddScoped<IMediaContentProvider, ProjectPhotoMediaContentProvider>();
        services.AddScoped<IMediaContentProvider, ProjectVideoMediaContentProvider>();
        services.AddScoped<IMediaContentProvider, VisitPhotoMediaContentProvider>();
        services.AddScoped<IMediaContentProvider, SocialMediaPhotoMediaContentProvider>();
        services.AddScoped<IMediaContentProvider, ActivityPhotoMediaContentProvider>();
        services.AddScoped<IMediaContentProviderResolver, MediaContentProviderResolver>();
        services.AddScoped<IMediaMetadataReader, MediaMetadataReader>();
        services.AddScoped<IFacePresenceProbe, YuNetFacePresenceProbe>();
        services.AddScoped<IMediaClassificationDecisionPolicy, MediaClassificationDecisionPolicy>();
        services.AddScoped<IMediaClassifier, MediaClassifier>();
        services.AddScoped<IMediaClassificationEligibilityService, MediaClassificationEligibilityService>();
        services.AddScoped<IMediaClassificationOverrideService, MediaClassificationOverrideService>();
        services.AddScoped<IMediaContentChangeInvalidationService, MediaContentChangeInvalidationService>();
        services.AddScoped<IMediaDerivativeService, MediaDerivativeService>();
        services.AddScoped<IMediaAssetProcessor, MediaAssetProcessor>();
        services.AddSingleton<MediaLibrarySchemaStatusCache>();
        services.AddScoped<IMediaLibrarySchemaService, MediaLibrarySchemaService>();
        services.AddSingleton<IFaceModelReadinessService, FaceModelReadinessService>();
        services.AddSingleton<OnnxFaceAnalysisEngine>();
        services.AddSingleton<IFaceAnalysisEngine>(provider =>
            provider.GetRequiredService<OnnxFaceAnalysisEngine>());
        services.AddSingleton<IFacePresenceAnalysisEngine>(provider =>
            provider.GetRequiredService<OnnxFaceAnalysisEngine>());
        services.AddScoped<IFaceCandidateSearchService, FaceCandidateSearchService>();
        services.AddScoped<IFaceCandidateSuggestionService, FaceCandidateSuggestionService>();
        services.AddScoped<IFaceCandidateRefreshQueueService, FaceCandidateRefreshQueueService>();
        services.AddScoped<IFaceIdentityGroupingService, FaceIdentityGroupingService>();
        services.AddSingleton<IFaceIdentityGroupingRuntimeState, FaceIdentityGroupingRuntimeState>();
        services.AddScoped<IFaceIntelligenceService, FaceIntelligenceService>();
        services.AddScoped<IFaceEligibilityPolicy, FaceEligibilityPolicy>();
        services.AddScoped<IFaceQueueService, FaceQueueService>();
        services.AddScoped<IFaceReviewService, FaceReviewService>();

        if (configuredOptions.IsCatalogueEnabled
            && configuredOptions.Catalogue.SynchronizePrismMedia)
        {
            services.AddHostedService<PrismMediaOutboxWorker>();
        }

        if (configuredOptions.IsCatalogueEnabled
            && (configuredOptions.Catalogue.SynchronizePrismMedia
                || configuredOptions.IsScannerWorkerEnabled))
        {
            services.AddHostedService<MediaSourceScannerWorker>();
        }

        if (configuredOptions.IsAnyProcessingWorkerEnabled)
        {
            services.AddHostedService<MediaProcessingWorker>();
            services.AddHostedService<MediaAvailabilityReconciliationWorker>();
        }

        if (configuredOptions.IsPeopleWorkerEnabled)
        {
            services.AddHostedService<FaceAnalysisQueueWorker>();
            if (configuredOptions.People.CandidateSearchEnabled)
            {
                services.AddHostedService<FaceCandidateRefreshWorker>();
            }
            if (configuredOptions.People.GroupingEnabled)
            {
                services.AddHostedService<FaceIdentityGroupingRefreshWorker>();
            }
        }

        return services;
    }
}
