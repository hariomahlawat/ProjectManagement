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

    private static readonly string[] DmSansFiles =
    [
        "DMSans-Regular.ttf",
        "DMSans-Medium.ttf",
        "DMSans-SemiBold.ttf",
        "DMSans-Bold.ttf",
        "DMSans-Italic.ttf",
        "DMSans-BoldItalic.ttf"
    ];

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

            var roots = CandidateRoots().ToArray();
            var dmDirectory = roots
                .Select(root => Path.Combine(root, "dm-sans"))
                .FirstOrDefault(directory => DmSansFiles.All(file => File.Exists(Path.Combine(directory, file))));
            var missingDmSans = dmDirectory is null
                ? DmSansFiles
                : DmSansFiles.Where(file => !File.Exists(Path.Combine(dmDirectory, file))).ToArray();
            var dmSansAvailable = dmDirectory is not null && missingDmSans.Length == 0;

            if (dmSansAvailable)
            {
                try
                {
                    foreach (var file in DmSansFiles)
                    {
                        using var stream = File.OpenRead(Path.Combine(dmDirectory!, file));
                        FontManager.RegisterFontWithCustomName(PrimaryFamilyName, stream);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unable to register DM Sans publication fonts. Falling back to Lato.");
                    dmSansAvailable = false;
                    missingDmSans = DmSansFiles;
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

            var source = dmDirectory is not null
                ? DescribeRoot(Path.GetDirectoryName(dmDirectory)!)
                : "QuestPDF bundled fallback";

            _status = new PublicationFontStatus(
                dmSansAvailable ? PrimaryFamilyName : FallbackFamilyName,
                alatsiAvailable ? DisplayFamilyName : dmSansAvailable ? PrimaryFamilyName : FallbackFamilyName,
                dmSansAvailable,
                alatsiAvailable,
                missingDmSans,
                source);

            _logger.LogInformation(
                "Publication fonts initialised. Primary={PrimaryFamily}, Display={DisplayFamily}, Source={Source}",
                _status.PrimaryFamily,
                _status.DisplayFamily,
                _status.SourceDescription);
            return _status;
        }
    }

    private IEnumerable<string> CandidateRoots()
    {
        if (!string.IsNullOrWhiteSpace(_environment.ContentRootPath))
        {
            yield return Path.Combine(
                _environment.ContentRootPath,
                "Resources",
                "Publications",
                "Fonts");
        }

        if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
        {
            yield return Path.Combine(
                _environment.WebRootPath,
                "fonts",
                "publications");
        }
    }

    private string DescribeRoot(string root)
    {
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
