using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using QuestPDF.Drawing;
using ProjectManagement.Services.Publications;

namespace ProjectManagement.Utilities.Reporting;

public interface IPublicationFontService
{
    PublicationFontStatus EnsureRegistered();
    PublicationFontStatus CurrentStatus { get; }
}

/// <summary>
/// Registers PRISM publication fonts once for the application process. Resources/Publications/Fonts
/// is preferred for server-side deployment; the Phase-1 wwwroot path remains supported so existing
/// air-gapped installations do not have to move their font package immediately.
/// </summary>
public sealed class PublicationFontService : IPublicationFontService
{
    public const string PrimaryFamilyName = "PRISM DM Sans";
    public const string DisplayFamilyName = "PRISM Alatsi";
    public const string FallbackFamilyName = "Lato";

    private readonly object _gate = new();
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PublicationFontService> _logger;
    private PublicationFontStatus? _status;

    public PublicationFontService(
        IWebHostEnvironment environment,
        ILogger<PublicationFontService> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PublicationFontStatus CurrentStatus => _status ?? EnsureRegistered();

    public PublicationFontStatus EnsureRegistered()
    {
        if (_status is not null)
        {
            return _status;
        }

        lock (_gate)
        {
            if (_status is not null)
            {
                return _status;
            }

            var roots = PublicationFontContract.CandidatePublicationRoots(
                _environment.ContentRootPath,
                _environment.WebRootPath);
            var dmResolution = PublicationFontContract.InspectDmSans(
                _environment.ContentRootPath,
                _environment.WebRootPath);
            var dmDirectory = dmResolution.DirectoryPath;
            var missingDmSans = dmResolution.MissingFiles.ToArray();
            var dmSansAvailable = dmResolution.IsAvailable;

            if (dmSansAvailable)
            {
                try
                {
                    foreach (var file in PublicationFontContract.RequiredDmSansFiles)
                    {
                        using var stream = File.OpenRead(Path.Combine(dmDirectory!, file));
                        FontManager.RegisterFontWithCustomName(PrimaryFamilyName, stream);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Unable to register the bundled DM Sans publication fonts from {FontDirectory}. Compendium generation will be unavailable; non-Compendium reports may use Lato.",
                        dmDirectory);
                    dmSansAvailable = false;
                    missingDmSans = PublicationFontContract.RequiredDmSansFiles.ToArray();
                }
            }

            var alatsiPath = roots
                .Select(root => Path.Combine(root, "alatsi", "Alatsi-Regular.ttf"))
                .FirstOrDefault(File.Exists);
            var alatsiAvailable = alatsiPath is not null;
            if (alatsiAvailable)
            {
                try
                {
                    using var stream = File.OpenRead(alatsiPath!);
                    FontManager.RegisterFontWithCustomName(DisplayFamilyName, stream);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Unable to register Alatsi publication font. Cover A will use the primary family.");
                    alatsiAvailable = false;
                }
            }

            var source = dmSansAvailable && dmDirectory is not null
                ? DescribeRoot(Path.GetDirectoryName(dmDirectory)!)
                : "DM Sans unavailable; non-Compendium fallback only";

            _status = new PublicationFontStatus(
                dmSansAvailable ? PrimaryFamilyName : FallbackFamilyName,
                alatsiAvailable ? DisplayFamilyName : dmSansAvailable ? PrimaryFamilyName : FallbackFamilyName,
                dmSansAvailable,
                alatsiAvailable,
                missingDmSans,
                source);

            _logger.LogInformation(
                "Publication fonts initialised. Primary={PrimaryFamily}, Display={DisplayFamily}, DmSansAvailable={DmSansAvailable}, Source={Source}",
                _status.PrimaryFamily,
                _status.DisplayFamily,
                _status.DmSansAvailable,
                _status.SourceDescription);
            return _status;
        }
    }

    private string DescribeRoot(string root)
    {
        var externalRoot = Environment.GetEnvironmentVariable(
            PublicationFontContract.ExternalFontRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(externalRoot))
        {
            var configured = Path.GetFullPath(Environment.ExpandEnvironmentVariables(externalRoot.Trim()));
            var publicationRoot = string.Equals(Path.GetFileName(configured), "dm-sans", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(configured)?.FullName
                : configured;
            if (!string.IsNullOrWhiteSpace(publicationRoot)
                && string.Equals(Path.GetFullPath(root), Path.GetFullPath(publicationRoot), StringComparison.OrdinalIgnoreCase))
            {
                return PublicationFontContract.ExternalFontRootEnvironmentVariable;
            }
        }

        if (!string.IsNullOrWhiteSpace(_environment.ContentRootPath))
        {
            var resourceRoot = Path.GetFullPath(Path.Combine(
                _environment.ContentRootPath,
                "Resources",
                "Publications",
                "Fonts"));
            if (string.Equals(Path.GetFullPath(root), resourceRoot, StringComparison.OrdinalIgnoreCase))
            {
                return "Resources/Publications/Fonts";
            }
        }

        return "wwwroot/fonts/publications";
    }
}

/// <summary>
/// Warms QuestPDF font registration as part of application startup so the first publication
/// request does not become the font-initialisation boundary.
/// </summary>
public sealed class PublicationFontWarmupHostedService : IHostedService
{
    private readonly IPublicationFontService _fontService;

    public PublicationFontWarmupHostedService(IPublicationFontService fontService)
    {
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _fontService.EnsureRegistered();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Compatibility constants retained for code/tests that referenced the Phase-1 registry name.
/// New code should depend on IPublicationFontService.
/// </summary>
public static class PublicationFontRegistry
{
    public const string PrimaryFamilyName = PublicationFontService.PrimaryFamilyName;
    public const string DisplayFamilyName = PublicationFontService.DisplayFamilyName;
    public const string FallbackFamilyName = PublicationFontService.FallbackFamilyName;
}
