using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Publications;

public static class PublicationServiceCollectionExtensions
{
    public static IServiceCollection AddProjectPublications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPublicationFontService, PublicationFontService>();
        services.AddHostedService<PublicationFontWarmupHostedService>();
        services.AddScoped<IBrochurePhotoService, BrochurePhotoService>();
        services.AddScoped<IBrochurePublicationService, BrochurePublicationService>();
        services.AddScoped<IBrochurePdfReportBuilder, BrochurePdfReportBuilder>();
        return services;
    }
}
