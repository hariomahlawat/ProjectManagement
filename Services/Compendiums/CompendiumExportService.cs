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
                    request.Edition)
                {
                    NarrativeSource = request.NarrativeSource,
                    GroupingMode = request.GroupingMode,
                    SortMode = request.SortMode,
                    Sections = request.Sections,
                    CoverDesign = request.CoverDesign,
                    PhotoPreferences = request.PhotoPreferences
                },
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
                CompendiumPublicationImagePolicy.ResolveRenderHeightPixels(CompendiumPublicationTextSanitizer.Sanitize(project.DescriptionMarkdown)))
            {
                FitMode = project.ImageFitMode == CompendiumImageFitMode.Fit
                    ? BrochurePhotoFitMode.Fit
                    : BrochurePhotoFitMode.Fill
            })
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
                    ProliferationAvailability = project.ProliferationAvailability,
                    TechnicalCategoryDisplay = CompendiumPublicationTextSanitizer.Sanitize(project.TechnicalCategoryName),
                    NarrativeLabel = CompendiumPublicationTextSanitizer.Sanitize(project.NarrativeLabel),
                    ImageFitMode = project.ImageFitMode
                });
            }

            categories.Add(new CompendiumPdfCategorySection(group.TechnicalCategoryName, projects));
        }

        var coverDesign = await ResolveCoverDesignAsync(request, publicationProjects, cancellationToken);
        var coverHero = coverDesign?.Images.FirstOrDefault(image => image.Surface == CompendiumCoverSurface.Front
                                                                    && string.Equals(image.SlotKey, "Hero", StringComparison.OrdinalIgnoreCase))?.Content;

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
            CoverHero = coverHero,
            CoverDesign = coverDesign
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

    private async Task<CompendiumPdfCoverDesign?> ResolveCoverDesignAsync(
        CompendiumExportRequest request,
        IReadOnlyList<CompendiumProjectDto> projects,
        CancellationToken cancellationToken)
    {
        var configured = request.CoverDesign ?? CreateLegacyCoverDesign(request);
        var preferences = (request.PhotoPreferences ?? Array.Empty<CompendiumPhotoPreference>())
            .GroupBy(item => (item.ProjectId, item.PhotoId))
            .Select(group => group.Last())
            .ToArray();
        var requiredSlots = RequiredCoverSlots(configured.FrontTemplate, configured.BackTemplate);
        var configuredBySlot = configured.Images
            .GroupBy(item => (item.Surface, Slot: item.SlotKey.ToUpperInvariant()))
            .ToDictionary(group => group.Key, group => group.First());
        var candidates = BuildAutomaticCoverCandidates(projects, preferences);
        var used = new HashSet<(int ProjectId, int PhotoId)>();
        var usedProjects = new HashSet<int>();
        var rendered = new List<CompendiumPdfCoverImage>();

        foreach (var required in requiredSlots)
        {
            configuredBySlot.TryGetValue((required.Surface, required.SlotKey.ToUpperInvariant()), out var slot);
            slot ??= new CompendiumCoverImageSlot(
                required.Surface,
                required.SlotKey,
                CompendiumCoverImageMode.Automatic,
                null,
                null,
                .5d,
                .5d,
                CompendiumImageFitMode.Fill);

            if (slot.ImageMode == CompendiumCoverImageMode.None)
            {
                rendered.Add(new CompendiumPdfCoverImage(required.Surface, required.SlotKey, null, slot.FitMode));
                continue;
            }

            CoverCandidate? candidate = null;
            if (slot.ImageMode == CompendiumCoverImageMode.Explicit)
            {
                if (slot.ProjectId is not int explicitProject
                    || slot.PhotoId is not int explicitPhoto
                    || !projects.Any(project => project.ProjectId == explicitProject))
                {
                    throw new InvalidOperationException(
                        $"The selected {required.Surface.ToString().ToLowerInvariant()} cover image for slot '{required.SlotKey}' is no longer available in this Compendium.");
                }

                candidate = new CoverCandidate(explicitProject, explicitPhoto, slot.FocalX, slot.FocalY, 1000);
            }
            else
            {
                candidate = candidates.FirstOrDefault(item =>
                                !used.Contains((item.ProjectId, item.PhotoId))
                                && !usedProjects.Contains(item.ProjectId))
                            ?? candidates.FirstOrDefault(item => !used.Contains((item.ProjectId, item.PhotoId)))
                            ?? candidates.FirstOrDefault();
            }

            if (candidate is null)
            {
                rendered.Add(new CompendiumPdfCoverImage(required.Surface, required.SlotKey, null, slot.FitMode));
                continue;
            }

            var geometry = ResolveCoverSlotGeometry(configured.FrontTemplate, configured.BackTemplate, required.Surface, required.SlotKey);
            try
            {
                var image = await _photoService.RenderAsync(
                    new BrochurePhotoRenderRequest(
                        candidate.ProjectId,
                        candidate.PhotoId,
                        slot.ImageMode == CompendiumCoverImageMode.Explicit ? ClampFocal(slot.FocalX) : candidate.FocalX,
                        slot.ImageMode == CompendiumCoverImageMode.Explicit ? ClampFocal(slot.FocalY) : candidate.FocalY,
                        geometry.Width,
                        geometry.Height)
                    {
                        FitMode = slot.FitMode == CompendiumImageFitMode.Fit ? BrochurePhotoFitMode.Fit : BrochurePhotoFitMode.Fill
                    },
                    cancellationToken);
                if (slot.ImageMode == CompendiumCoverImageMode.Explicit
                    && image?.Content is not { Length: > 0 })
                {
                    throw new InvalidOperationException(
                        $"The selected {required.Surface.ToString().ToLowerInvariant()} cover image for slot '{required.SlotKey}' could not be rendered. Choose another image.");
                }

                rendered.Add(new CompendiumPdfCoverImage(
                    required.Surface,
                    required.SlotKey,
                    image?.Content,
                    slot.FitMode,
                    candidate.ProjectId,
                    candidate.PhotoId));
                if (image?.Content is { Length: > 0 })
                {
                    used.Add((candidate.ProjectId, candidate.PhotoId));
                    usedProjects.Add(candidate.ProjectId);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (slot.ImageMode != CompendiumCoverImageMode.Explicit)
            {
                _logger.LogWarning(exception,
                    "Compendium cover image could not be rendered. Surface={Surface}, Slot={Slot}, ProjectId={ProjectId}, PhotoId={PhotoId}.",
                    required.Surface, required.SlotKey, candidate.ProjectId, candidate.PhotoId);
                rendered.Add(new CompendiumPdfCoverImage(required.Surface, required.SlotKey, null, slot.FitMode));
            }
        }

        return new CompendiumPdfCoverDesign(configured.FrontTemplate, configured.BackTemplate, rendered)
        {
            FrontTitle = configured.FrontTitle,
            FrontSubtitle = configured.FrontSubtitle,
            FrontEdition = configured.FrontEdition,
            FrontEyebrow = configured.FrontEyebrow,
            BackTitle = configured.BackTitle,
            BackSubtitle = configured.BackSubtitle,
            BackEdition = configured.BackEdition,
            BackEyebrow = configured.BackEyebrow,
            ShowFrontTitle = configured.ShowFrontTitle,
            ShowFrontSubtitle = configured.ShowFrontSubtitle,
            ShowFrontEdition = configured.ShowFrontEdition,
            ShowFrontLeftLogo = configured.ShowFrontLeftLogo,
            ShowFrontRightLogo = configured.ShowFrontRightLogo,
            FrontLogoPlacement = configured.FrontLogoPlacement,
            ShowBackTitle = configured.ShowBackTitle,
            ShowBackSubtitle = configured.ShowBackSubtitle,
            ShowBackEdition = configured.ShowBackEdition,
            ShowBackLeftLogo = configured.ShowBackLeftLogo,
            ShowBackRightLogo = configured.ShowBackRightLogo,
            BackLogoPlacement = configured.BackLogoPlacement
        };
    }

    private static CompendiumCoverDesign CreateLegacyCoverDesign(CompendiumExportRequest request)
        => new(
            CompendiumFrontCoverTemplate.InstitutionalHero,
            CompendiumBackCoverTemplate.MinimalInstitutional,
            new[]
            {
                new CompendiumCoverImageSlot(
                    CompendiumCoverSurface.Front,
                    "Hero",
                    request.CoverImageMode,
                    request.CoverHeroProjectId,
                    request.CoverHeroPhotoId,
                    request.CoverFocalX,
                    request.CoverFocalY,
                    CompendiumImageFitMode.Fill)
            });

    private static IReadOnlyList<(CompendiumCoverSurface Surface, string SlotKey)> RequiredCoverSlots(
        CompendiumFrontCoverTemplate front,
        CompendiumBackCoverTemplate back)
    {
        var result = new List<(CompendiumCoverSurface, string)>();
        if (front is not CompendiumFrontCoverTemplate.Minimal)
        {
            result.Add((CompendiumCoverSurface.Front, "Hero"));
            if (front is CompendiumFrontCoverTemplate.EditorialSplit or CompendiumFrontCoverTemplate.Triptych)
                result.Add((CompendiumCoverSurface.Front, "Secondary1"));
            if (front is CompendiumFrontCoverTemplate.Triptych)
                result.Add((CompendiumCoverSurface.Front, "Secondary2"));
        }
        if (back is CompendiumBackCoverTemplate.ImageEcho or CompendiumBackCoverTemplate.PortfolioStrip)
        {
            result.Add((CompendiumCoverSurface.Back, "Hero"));
            if (back is CompendiumBackCoverTemplate.PortfolioStrip)
            {
                result.Add((CompendiumCoverSurface.Back, "Secondary1"));
                result.Add((CompendiumCoverSurface.Back, "Secondary2"));
            }
        }
        return result;
    }

    private static IReadOnlyList<CoverCandidate> BuildAutomaticCoverCandidates(
        IReadOnlyList<CompendiumProjectDto> projects,
        IReadOnlyList<CompendiumPhotoPreference> preferences)
    {
        var preferencesByProject = preferences
            .GroupBy(item => item.ProjectId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var result = new List<CoverCandidate>();
        foreach (var project in projects)
        {
            if (preferencesByProject.TryGetValue(project.ProjectId, out var projectPreferences))
            {
                foreach (var preference in projectPreferences.Where(item => item.SuitableForCoverHero || item.PreferredForPublication))
                {
                    var priority = preference.SuitableForCoverHero ? 500 : 350;
                    var focalX = project.CoverPhotoId == preference.PhotoId ? project.PrimaryFocalX : .5d;
                    var focalY = project.CoverPhotoId == preference.PhotoId ? project.PrimaryFocalY : .5d;
                    result.Add(new CoverCandidate(project.ProjectId, preference.PhotoId, focalX, focalY,
                        priority + (project.IsReviewed ? 20 : 0) + (project.EffectiveDpi ?? 0) / 20));
                }
            }

            if (project.CoverPhotoId is int photoId)
            {
                result.Add(new CoverCandidate(
                    project.ProjectId,
                    photoId,
                    project.PrimaryFocalX,
                    project.PrimaryFocalY,
                    100 + CoverHeroSourcePriority(project.CoverPhotoSource) * 20 + (project.IsReviewed ? 10 : 0) + (project.EffectiveDpi ?? 0) / 30));
            }
        }
        return result
            .GroupBy(item => (item.ProjectId, item.PhotoId))
            .Select(group => group.OrderByDescending(item => item.Priority).First())
            .OrderByDescending(item => item.Priority)
            .ToArray();
    }

    private static (int Width, int Height) ResolveCoverSlotGeometry(
        CompendiumFrontCoverTemplate front,
        CompendiumBackCoverTemplate back,
        CompendiumCoverSurface surface,
        string slot)
    {
        if (surface == CompendiumCoverSurface.Front)
        {
            return front switch
            {
                CompendiumFrontCoverTemplate.FullBleedHero => (1800, 2546),
                CompendiumFrontCoverTemplate.EditorialSplit when string.Equals(slot, "Hero", StringComparison.OrdinalIgnoreCase) => (1400, 1700),
                CompendiumFrontCoverTemplate.EditorialSplit => (700, 1700),
                CompendiumFrontCoverTemplate.Triptych => (700, 1500),
                _ => (CompendiumCoverImagePolicy.RenderWidthPixels, CompendiumCoverImagePolicy.RenderHeightPixels)
            };
        }
        return back switch
        {
            CompendiumBackCoverTemplate.ImageEcho => (1800, 1800),
            CompendiumBackCoverTemplate.PortfolioStrip => (700, 1100),
            _ => (CompendiumCoverImagePolicy.RenderWidthPixels, CompendiumCoverImagePolicy.RenderHeightPixels)
        };
    }

    private sealed record CoverCandidate(int ProjectId, int PhotoId, double FocalX, double FocalY, int Priority);

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

    private static int CoverHeroSourcePriority(CompendiumPhotoSelectionSource source)
        => source switch
        {
            CompendiumPhotoSelectionSource.ProjectCover => 4,
            CompendiumPhotoSelectionSource.MarkedCover => 3,
            CompendiumPhotoSelectionSource.ExplicitPublication => 2,
            CompendiumPhotoSelectionSource.FirstAvailable => 1,
            _ => 0
        };

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
