using System.Globalization;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Utilities;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Compendium export orchestration. The authored Publications path renders only the selected,
/// ordered project snapshot. The parameterless overload intentionally retains the historic
/// automatic proliferation catalogue for legacy integrations/bookmarks.
/// </summary>
public sealed class CompendiumExportService : ICompendiumExportService
{
    private static readonly HashSet<string> SupportedPhotoFormats = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "jpg",
        "jpeg",
        "png",
        "webp"
    };

    private readonly ICompendiumReadService _readService;
    private readonly IProjectPhotoService _projectPhotoService;
    private readonly ICompendiumPdfReportBuilder _pdfBuilder;
    private readonly CompendiumPdfOptions _options;
    private readonly ILogger<CompendiumExportService> _logger;

    public CompendiumExportService(
        ICompendiumReadService readService,
        IProjectPhotoService projectPhotoService,
        ICompendiumPdfReportBuilder pdfBuilder,
        IOptions<CompendiumPdfOptions> options,
        ILogger<CompendiumExportService> logger)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _projectPhotoService = projectPhotoService ?? throw new ArgumentNullException(nameof(projectPhotoService));
        _pdfBuilder = pdfBuilder ?? throw new ArgumentNullException(nameof(pdfBuilder));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CompendiumExportResult> GenerateAsync(
        CancellationToken cancellationToken = default)
        => GenerateAsync(new CompendiumExportRequest(), cancellationToken);

    public async Task<CompendiumExportResult> GenerateAsync(
        CompendiumExportRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new CompendiumExportRequest();

        var data = request.SelectedProjectIds is { Count: > 0 }
            ? await _readService.GetPublicationAsync(
                new CompendiumPublicationRequest(
                    request.SelectedProjectIds,
                    request.Title,
                    request.Subtitle,
                    request.Edition),
                cancellationToken)
            : await _readService.GetProliferationCompendiumAsync(cancellationToken);

        if (!data.Preflight.CanGenerate)
        {
            throw new InvalidOperationException(
                "The Compendium cannot be generated while publication blockers remain.");
        }

        var categories = new List<CompendiumPdfCategorySection>(data.Groups.Count);
        foreach (var group in data.Groups)
        {
            var projects = new List<CompendiumPdfProjectSection>(group.Projects.Count);
            foreach (var project in group.Projects)
            {
                // ProjectPhotoService uses the request-scoped DbContext. Keep image reads
                // sequential and deterministic rather than running concurrent EF operations.
                var photoBytes = await TryLoadPhotoAsync(
                    project.ProjectId,
                    project.CoverPhotoId,
                    cancellationToken);

                projects.Add(new CompendiumPdfProjectSection(
                    project.ProjectId,
                    project.ProjectName,
                    project.CaseFileNumber,
                    group.TechnicalCategoryName,
                    project.CompletionYearDisplay,
                    project.ArmServiceDisplay,
                    FormatCost(project.ProliferationCostLakhs),
                    project.ProliferationCostRemarks,
                    project.DescriptionMarkdown,
                    photoBytes,
                    project.CoverPhotoId.HasValue)
                {
                    LifecycleDisplay = project.LifecycleDisplay,
                    IsAvailableForProliferation = project.IsAvailableForProliferation
                });
            }

            categories.Add(new CompendiumPdfCategorySection(
                group.TechnicalCategoryName,
                projects));
        }

        var context = new CompendiumPdfReportContext(
            data.Title,
            data.Subtitle,
            data.UnitDisplayName,
            data.IssuerDisplayName,
            NormalizeMarking(request.HandlingMarking),
            data.GeneratedAtUtc,
            categories,
            _options.ShowMissingPhotoPlaceholder)
        {
            Edition = data.Edition
        };

        var pdfBytes = _pdfBuilder.Build(context);
        var dateStamp = TimeZoneInfo.ConvertTime(
                data.GeneratedAtUtc,
                TimeZoneHelper.GetIst())
            .ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var prefix = SanitizeFileNamePrefix(_options.FileNamePrefix);
        var projectCount = data.Preflight.SelectedProjectCount > 0
            ? data.Preflight.SelectedProjectCount
            : data.Preflight.EligibleProjectCount;

        return new CompendiumExportResult(
            pdfBytes,
            $"{prefix}_{dateStamp}.pdf",
            projectCount,
            data.Groups.Count);
    }

    private async Task<byte[]?> TryLoadPhotoAsync(
        int projectId,
        int? photoId,
        CancellationToken cancellationToken)
    {
        if (!photoId.HasValue)
        {
            return null;
        }

        try
        {
            var derivativeKey = string.IsNullOrWhiteSpace(_options.CoverPhotoDerivativeKey)
                ? "md"
                : _options.CoverPhotoDerivativeKey.Trim();
            var preferredFormat = NormalizePhotoFormat(_options.PreferredPhotoFormat);

            if (preferredFormat is not null)
            {
                var preferred = await _projectPhotoService.OpenDerivativeAsync(
                    projectId,
                    photoId.Value,
                    derivativeKey,
                    preferredFormat,
                    cancellationToken);
                var preferredBytes = await CopyOpenedPhotoAsync(preferred, cancellationToken);
                if (preferredBytes is not null)
                {
                    return preferredBytes;
                }
            }

            var fallback = await _projectPhotoService.OpenDerivativeAsync(
                projectId,
                photoId.Value,
                derivativeKey,
                preferWebp: false,
                cancellationToken: cancellationToken);
            var fallbackBytes = await CopyOpenedPhotoAsync(fallback, cancellationToken);
            if (fallbackBytes is not null)
            {
                return fallbackBytes;
            }

            if (_options.PreferWebp)
            {
                var webp = await _projectPhotoService.OpenDerivativeAsync(
                    projectId,
                    photoId.Value,
                    derivativeKey,
                    requestedFormat: "webp",
                    cancellationToken: cancellationToken);
                var webpBytes = await CopyOpenedPhotoAsync(webp, cancellationToken);
                if (webpBytes is not null)
                {
                    return webpBytes;
                }
            }

            _logger.LogWarning(
                "No usable Compendium photo derivative was found for project {ProjectId}, photo {PhotoId}, derivative {DerivativeKey}.",
                projectId,
                photoId.Value,
                derivativeKey);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to load the Compendium photo for project {ProjectId}, photo {PhotoId}.",
                projectId,
                photoId.Value);
            return null;
        }
    }

    private static async Task<byte[]?> CopyOpenedPhotoAsync(
        (Stream Stream, string ContentType)? opened,
        CancellationToken cancellationToken)
    {
        if (opened is null)
        {
            return null;
        }

        await using var stream = opened.Value.Stream;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.Length == 0 ? null : memory.ToArray();
    }

    private static string FormatCost(decimal? value)
        => value.HasValue
            ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : "Not recorded";

    private static string? NormalizeMarking(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizePhotoFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().TrimStart('.').ToLowerInvariant();
        return SupportedPhotoFormats.Contains(normalized) ? normalized : null;
    }

    private static string SanitizeFileNamePrefix(string? value)
    {
        var candidate = string.IsNullOrWhiteSpace(value)
            ? "SDD_Simulators_Compendium"
            : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = candidate
            .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character)
                ? '_'
                : character)
            .ToArray();
        var clean = new string(characters).Trim('_');
        return string.IsNullOrWhiteSpace(clean)
            ? "SDD_Simulators_Compendium"
            : clean;
    }
}
