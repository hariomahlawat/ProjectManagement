using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Services.SearchV2.Analytics;
using ProjectManagement.Services.SearchV2.Indexing;
using ProjectManagement.Services.SearchV2.Query;
using ProjectManagement.Services.SearchV2.Security;

namespace ProjectManagement.Services.SearchV2;

public static class SearchV2ServiceCollectionExtensions
{
    public static IServiceCollection AddSearchV2(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SearchV2Options>()
            .Bind(configuration.GetSection(SearchV2Options.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.MaxPageSize >= options.PageSize, "Search:V2:MaxPageSize must be greater than or equal to PageSize.")
            .ValidateOnStart();

        services.AddSingleton<ISearchQueryNormalizer, SearchQueryNormalizer>();
        services.AddSingleton<ISearchCursorCodec, SearchCursorCodec>();
        services.AddScoped<ISearchHighlightService, SearchHighlightService>();
        services.AddScoped<ISearchAccessContextFactory, SearchAccessContextFactory>();
        services.AddScoped<ISearchIndexStore, SearchIndexStore>();
        services.AddScoped<ISearchProjectionBuilder, SearchProjectionBuilder>();
        services.AddScoped<ISearchV2Engine, SearchEngine>();
        services.AddScoped<ISearchAnalyticsService, SearchAnalyticsService>();
        services.AddScoped<ISearchGateway, SearchGateway>();
        services.AddHostedService<SearchIndexWorker>();
        services.AddHostedService<SearchTelemetryRetentionWorker>();
        return services;
    }
}
