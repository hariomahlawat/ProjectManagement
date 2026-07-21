using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Compendiums;

// SECTION: PDF export orchestration for the Simulators Compendium.
public sealed class CompendiumExportService : ICompendiumExportService
{
    private static readonly HashSet<string> SupportedPhotoFormats = new(StringComparer.OrdinalIgnoreCase)
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

    public Task<CompendiumExportResult> GenerateAsync(CancellationToken cancellationToken = default)
        => GenerateAsync(new CompendiumExportRequest(), cancellationToken);

    public async Task<CompendiumExportResult> GenerateAsync(
        CompendiumExportRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new CompendiumExportRequest();

        var data = await _readService.GetProliferationCompendiumAsync(cancellationToken);
        var categories = new List<CompendiumPdfCategorySection>(data.Groups.Count);
        var missingDerivativeCount = 0;

        // Photo access is intentionally sequential because the underlying scoped
        // ProjectPhotoService uses the request-scoped EF Core DbContext.
        foreach (var group in data.Groups)
        {
            var projects = new List<CompendiumPdfProjectSection>(group.Projects.Count);

            foreach (var project in group.Projects)
            {
                var photoBytes = await TryLoadCoverPhotoAsync(
                    project.ProjectId,
                    project.CoverPhotoId,
                    cancellationToken);

                if (project.CoverPhotoId.HasValue && photoBytes is null)
                {
                    missingDerivativeCount++;
                }

                projects.Add(new CompendiumPdfProjectSection(
                    ProjectId: project.ProjectId,
                    ProjectName: project.ProjectName,
                    CaseFileNumber: project.CaseFileNumber,
                    CategoryName: group.TechnicalCategoryName,
                    CompletionYearDisplay: project.CompletionYearDisplay,
                    ArmServiceDisplay: project.ArmServiceDisplay,
                    ProliferationCostDisplay: FormatCost(project.ProliferationCostLakhs),
                    ProliferationCostRemarks: project.ProliferationCostRemarks,
                    DescriptionMarkdown: project.DescriptionMarkdown,
                    CoverPhoto: photoBytes,
                    PhotoWasSelected: project.CoverPhotoId.HasValue));
            }

            categories.Add(new CompendiumPdfCategorySection(
                group.TechnicalCategoryName,
                projects));
        }

        if (missingDerivativeCount > 0)
        {
            _logger.LogWarning(
                "Compendium PDF generated with {MissingDerivativeCount} selected project photos whose derivative files could not be opened.",
                missingDerivativeCount);
        }

        var context = new CompendiumPdfReportContext(
            Title: data.Title,
            Subtitle: data.Subtitle,
            UnitDisplayName: data.UnitDisplayName,
            IssuerDisplayName: data.IssuerDisplayName,
            HandlingMarking: NormalizeHandlingMarking(request.HandlingMarking),
            GeneratedAtUtc: data.GeneratedAtUtc,
            Categories: categories,
            ShowMissingPhotoPlaceholder: _options.ShowMissingPhotoPlaceholder);

        var bytes = _pdfBuilder.Build(context);
        var generatedAtIst = TimeZoneInfo.ConvertTime(data.GeneratedAtUtc, TimeZoneHelper.GetIst());
        var safeDate = generatedAtIst.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var prefix = SanitizeFileNamePrefix(_options.FileNamePrefix);
        var fileName = $"{prefix}_{safeDate}.pdf";

        _logger.LogInformation(
            "Generated compendium PDF with {ProjectCount} projects across {CategoryCount} categories. Publication warnings: {WarningCount}.",
            data.Preflight.EligibleProjectCount,
            data.Preflight.CategoryCount,
            data.Preflight.TotalWarningCount);

        return new CompendiumExportResult(bytes, fileName);
    }

    private async Task<byte[]?> TryLoadCoverPhotoAsync(
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

            // Fallback searches the original raster format, PNG/JPEG alternatives,
            // and finally WebP. This is deliberately PDF-friendly even when legacy
            // configuration says PreferWebp=true.
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
                "No usable compendium photo derivative was found for project {ProjectId}, photo {PhotoId}, derivative {DerivativeKey}.",
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
                "Unable to load the compendium photo for project {ProjectId}, photo {PhotoId}.",
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

    private static string? NormalizePhotoFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().TrimStart('.').ToLowerInvariant();
        return SupportedPhotoFormats.Contains(normalized) ? normalized : null;
    }

    private static string? NormalizeHandlingMarking(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static string SanitizeFileNamePrefix(string? value)
    {
        var candidate = string.IsNullOrWhiteSpace(value)
            ? "SDD_Simulators_Compendium"
            : value.Trim();

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = candidate
            .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character)
            .ToArray();

        var sanitized = new string(characters).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized)
            ? "SDD_Simulators_Compendium"
            : sanitized;
    }
}
