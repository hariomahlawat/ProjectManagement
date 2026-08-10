using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Publications;

public static class PublicationServiceCollectionExtensions
{
    public static IServiceCollection AddProjectPublications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Brochure photo probing uses a small in-memory cache keyed by the resolved
        // source file version/timestamp. Registering it here keeps this feature's DI
        // graph self-contained even if the application-level cache registration moves.
        services.AddMemoryCache();
        services.AddSingleton<IPublicationFontService, PublicationFontService>();
        services.AddScoped<IBrochurePhotoService, BrochurePhotoService>();
        services.AddScoped<IBrochurePublicationService, BrochurePublicationService>();
        services.AddScoped<IBrochurePdfReportBuilder, BrochurePdfReportBuilder>();

        // Fail early if the publication graph or font stack is broken rather than
        // surfacing a DI exception only when the first user opens the Brochure page.
        services.AddHostedService<PublicationFontWarmupHostedService>();
        services.AddHostedService<PublicationRuntimeValidationHostedService>();

        return services;
    }
}
