using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Publications;

/// <summary>
/// Validates the complete Publications service graph at application startup.
/// This turns missing or broken DI registrations into an immediate startup error
/// instead of an HTTP 500 only when a user first opens the Brochure page.
/// </summary>
public sealed class PublicationRuntimeValidationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPublicationFontService _fontService;
    private readonly ILogger<PublicationRuntimeValidationHostedService> _logger;

    public PublicationRuntimeValidationHostedService(
        IServiceScopeFactory scopeFactory,
        IPublicationFontService fontService,
        ILogger<PublicationRuntimeValidationHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _fontService.EnsureRegistered();

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        _ = services.GetRequiredService<IBrochurePhotoService>();
        _ = services.GetRequiredService<IBrochurePrintMeasurementService>();
        _ = services.GetRequiredService<IBrochurePrintPagePlanner>();
        _ = services.GetRequiredService<IBrochurePublicationService>();
        _ = services.GetRequiredService<IBrochurePdfReportBuilder>();

        // Publications is a common workspace. Validate the retained Compendium graph too.
        _ = services.GetRequiredService<ICompendiumReadService>();
        _ = services.GetRequiredService<ICompendiumExportService>();
        _ = services.GetRequiredService<ICompendiumPdfReportBuilder>();

        _logger.LogInformation(
            "Project Publications runtime validation passed. Brochure and Compendium service graphs are resolvable.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
