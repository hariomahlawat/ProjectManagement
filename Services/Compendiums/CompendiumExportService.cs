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
                CompendiumPublicationImagePolicy.RenderHeightPixels))
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
                    ProjectCategoryDisplay = project.ProjectCategoryName,
                    IsAvailableForProliferation = project.IsAvailableForProliferation,
                    ProliferationAvailability = project.ProliferationAvailability
                });
            }

            categories.Add(new CompendiumPdfCategorySection(group.TechnicalCategoryName, projects));
        }

        var coverImages = publicationProjects
            .Where(project => project.CoverPhotoId.HasValue)
            .Select(project => project.CoverPhotoId!.Value)
            .Distinct()
            .Select(photoId => renderedPhotos.TryGetValue(photoId, out var image) ? image.Content : null)
            .Where(content => content is { Length: > 0 })
            .Select(content => content!)
            .Take(3)
            .ToArray();

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
            Edition = data.Edition,
            CoverImages = coverImages
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

    private static string FormatCost(decimal? value)
        => value.HasValue
            ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : "Not recorded";

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
