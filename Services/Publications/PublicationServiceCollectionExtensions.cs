using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Publications;

public static class PublicationServiceCollectionExtensions
{
    public static IServiceCollection AddProjectPublications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPublicationFontService, PublicationFontService>();
        services.AddScoped<IBrochurePhotoService, BrochurePhotoService>();
        services.AddScoped<IBrochurePrintMeasurementService, BrochurePrintMeasurementService>();
        services.AddScoped<IBrochurePrintPagePlanner, BrochurePrintPagePlanner>();
        services.AddScoped<IBrochurePublicationService, BrochurePublicationService>();
        services.AddScoped<IBrochurePdfReportBuilder, BrochurePdfReportBuilder>();
        services.AddScoped<IBrochurePresetService, BrochurePresetService>();
        services.AddSingleton<ICompendiumReadinessPolicy, CompendiumReadinessPolicy>();
        services.AddSingleton<ICompendiumPagePlanner, CompendiumPagePlanner>();
        services.AddSingleton<ICompendiumPdfCompositionVerifier, CompendiumPdfCompositionVerifier>();
        services.AddScoped<ICompendiumPresetService, CompendiumPresetService>();
        services.AddHostedService<PublicationFontWarmupHostedService>();
        services.AddHostedService<PublicationRuntimeValidationHostedService>();
        return services;
    }
}
