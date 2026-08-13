using System.Globalization;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Publications;
using ProjectManagement.Utilities;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Phase 24 export orchestration. The selected live publication snapshot is converted once into a
/// physical page plan, composed by QuestPDF, reopened with PdfPig and verified before any bytes are
/// released for preview or final issue.
/// </summary>
public sealed class CompendiumExportService : ICompendiumExportService
{
    private readonly ICompendiumReadService _readService;
    private readonly IBrochurePhotoService _photoService;
    private readonly ICompendiumPdfReportBuilder _pdfBuilder;
    private readonly ICompendiumPagePlanner _pagePlanner;
    private readonly ICompendiumPdfCompositionVerifier _compositionVerifier;
    private readonly CompendiumPdfOptions _options;
    private readonly ILogger<CompendiumExportService> _logger;

    public CompendiumExportService(
        ICompendiumReadService readService,
        IBrochurePhotoService photoService,
        ICompendiumPdfReportBuilder pdfBuilder,
        ICompendiumPagePlanner pagePlanner,
        ICompendiumPdfCompositionVerifier compositionVerifier,
        IOptions<CompendiumPdfOptions> options,
        ILogger<CompendiumExportService> logger)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _pdfBuilder = pdfBuilder ?? throw new ArgumentNullException(nameof(pdfBuilder));
        _pagePlanner = pagePlanner ?? throw new ArgumentNullException(nameof(pagePlanner));
        _compositionVerifier = compositionVerifier ?? throw new ArgumentNullException(nameof(compositionVerifier));
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

        var authoredSelections = ResolveSelections(request);
        var data = authoredSelections.Count > 0
            ? await _readService.GetPublicationAsync(
                new CompendiumPublicationRequest(
                    authoredSelections,
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

        var publicationProjects = data.Groups.SelectMany(group => group.Projects).ToArray();
        if (request.RequireAllReviewed && publicationProjects.Any(project => !project.IsReviewed))
        {
            var outstanding = publicationProjects.Count(project => !project.IsReviewed);
            throw new InvalidOperationException(
                $"Review all selected projects before final issue. {outstanding} project{(outstanding == 1 ? string.Empty : "s")} still require review.");
        }

        var renderRequests = publicationProjects
            .Where(project => project.CoverPhotoId.HasValue)
            .Select(project => new BrochurePhotoRenderRequest(
                project.ProjectId,
                project.CoverPhotoId!.Value,
                project.PrimaryFocalX,
                project.PrimaryFocalY,
                CompendiumPublicationImagePolicy.RenderWidthPixels,
                CompendiumPublicationImagePolicy.ResolveRenderHeightPixels(CompendiumPublicationTextSanitizer.Sanitize(project.DescriptionMarkdown))))
            .ToArray();

        IReadOnlyDictionary<int, BrochurePublicationImage> renderedPhotos;
        try
        {
            renderedPhotos = renderRequests.Length == 0
                ? new Dictionary<int, BrochurePublicationImage>()
                : await _photoService.RenderAsync(renderRequests, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Compendium publication image preparation failed. Text-led project layouts will be used where necessary.");
            renderedPhotos = new Dictionary<int, BrochurePublicationImage>();
        }

        var categories = new List<CompendiumPdfCategorySection>(data.Groups.Count);
        foreach (var group in data.Groups)
        {
            var projects = new List<CompendiumPdfProjectSection>(group.Projects.Count);
            foreach (var project in group.Projects)
            {
                var photoBytes = project.CoverPhotoId.HasValue
                                 && renderedPhotos.TryGetValue(project.CoverPhotoId.Value, out var rendered)
                    ? rendered.Content
                    : null;

                if (project.CoverPhotoId.HasValue && photoBytes is null)
                {
                    _logger.LogWarning(
                        "Compendium publication photo could not be rendered. ProjectId={ProjectId}, PhotoId={PhotoId}.",
                        project.ProjectId,
                        project.CoverPhotoId.Value);
                }

                projects.Add(new CompendiumPdfProjectSection(
                    project.ProjectId,
                    CompendiumPublicationTextSanitizer.Sanitize(project.ProjectName),
                    CompendiumPublicationTextSanitizer.Sanitize(project.CaseFileNumber),
                    CompendiumPublicationTextSanitizer.Sanitize(group.TechnicalCategoryName),
                    CompendiumPublicationTextSanitizer.Sanitize(project.CompletionYearDisplay),
                    CompendiumPublicationTextSanitizer.Sanitize(project.ArmServiceDisplay),
                    CompendiumPublicationImagePolicy.FormatCost(project.ProliferationCostLakhs),
                    CompendiumPublicationTextSanitizer.Sanitize(project.ProliferationCostRemarks),
                    CompendiumPublicationTextSanitizer.Sanitize(project.DescriptionMarkdown),
                    photoBytes,
                    project.CoverPhotoId.HasValue)
                {
                    LifecycleDisplay = CompendiumPublicationTextSanitizer.Sanitize(project.LifecycleDisplay),
                    ProjectCategoryDisplay = CompendiumPublicationTextSanitizer.Sanitize(project.ProjectCategoryName),
                    IsAvailableForProliferation = project.IsAvailableForProliferation,
                    ProliferationAvailability = project.ProliferationAvailability
                });
            }

            categories.Add(new CompendiumPdfCategorySection(group.TechnicalCategoryName, projects));
        }

        var coverHero = await ResolveCoverHeroAsync(request, publicationProjects, cancellationToken);

        var context = new CompendiumPdfReportContext(
            CompendiumPublicationTextSanitizer.Sanitize(data.Title),
            CompendiumPublicationTextSanitizer.Sanitize(data.Subtitle),
            CompendiumPublicationTextSanitizer.Sanitize(data.UnitDisplayName),
            CompendiumPublicationTextSanitizer.Sanitize(data.IssuerDisplayName),
            NormalizeMarking(request.HandlingMarking),
            data.GeneratedAtUtc,
            categories,
            _options.ShowMissingPhotoPlaceholder)
        {
            Edition = CompendiumPublicationTextSanitizer.Sanitize(data.Edition),
            CoverHero = coverHero
        };

        var plan = _pagePlanner.Plan(context);
        context = context with { Plan = plan };
        var pdfBytes = _pdfBuilder.Build(context);
        var verification = _compositionVerifier.Verify(pdfBytes, context, plan);

        var dateStamp = TimeZoneInfo.ConvertTime(data.GeneratedAtUtc, TimeZoneHelper.GetIst())
            .ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var prefix = SanitizeFileNamePrefix(_options.FileNamePrefix);
        var projectCount = data.Preflight.SelectedProjectCount > 0
            ? data.Preflight.SelectedProjectCount
            : data.Preflight.EligibleProjectCount;

        return new CompendiumExportResult(
            pdfBytes,
            $"{prefix}_{dateStamp}.pdf",
            projectCount,
            data.Groups.Count)
        {
            IsCompositionVerified = verification.IsVerified,
            PhysicalPageCount = verification.PageCount
        };
    }

    private async Task<byte[]?> ResolveCoverHeroAsync(
        CompendiumExportRequest request,
        IReadOnlyList<CompendiumProjectDto> projects,
        CancellationToken cancellationToken)
    {
        if (request.CoverImageMode == CompendiumCoverImageMode.None)
        {
            return null;
        }

        int? projectId = null;
        int? photoId = null;
        double focalX = .5d;
        double focalY = .5d;

        if (request.CoverImageMode == CompendiumCoverImageMode.Explicit
            && request.CoverHeroProjectId is int explicitProjectId
            && request.CoverHeroPhotoId is int explicitPhotoId
            && projects.Any(project => project.ProjectId == explicitProjectId))
        {
            projectId = explicitProjectId;
            photoId = explicitPhotoId;
            focalX = ClampFocal(request.CoverFocalX);
            focalY = ClampFocal(request.CoverFocalY);
        }
        else
        {
            var candidate = projects
                .Where(project => project.CoverPhotoId.HasValue)
                .OrderByDescending(project => project.IsReviewed)
                .ThenByDescending(project => project.ImageQuality)
                .ThenBy(project => project.SortOrder)
                .FirstOrDefault();
            if (candidate is not null)
            {
                projectId = candidate.ProjectId;
                photoId = candidate.CoverPhotoId;
                focalX = candidate.PrimaryFocalX;
                focalY = candidate.PrimaryFocalY;
            }
        }

        if (projectId is not int resolvedProjectId || photoId is not int resolvedPhotoId)
        {
            return null;
        }

        try
        {
            var rendered = await _photoService.RenderAsync(
                new[]
                {
                    new BrochurePhotoRenderRequest(
                        resolvedProjectId,
                        resolvedPhotoId,
                        focalX,
                        focalY,
                        CompendiumCoverImagePolicy.RenderWidthPixels,
                        CompendiumCoverImagePolicy.RenderHeightPixels)
                },
                cancellationToken);
            return rendered.TryGetValue(resolvedPhotoId, out var image) && image.Content is { Length: > 0 }
                ? image.Content
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Compendium cover hero could not be rendered; graphic cover fallback will be used.");
            return null;
        }
    }

    private static IReadOnlyList<CompendiumProjectSelection> ResolveSelections(CompendiumExportRequest request)
    {
        if (request.ProjectSelections is { Count: > 0 })
        {
            return request.ProjectSelections;
        }

        return request.SelectedProjectIds is { Count: > 0 }
            ? request.SelectedProjectIds
                .Where(projectId => projectId > 0)
                .Select(projectId => new CompendiumProjectSelection(projectId))
                .ToArray()
            : Array.Empty<CompendiumProjectSelection>();
    }

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private static string? NormalizeMarking(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
