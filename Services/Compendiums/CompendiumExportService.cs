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
                    DefaultNarrativeAlignment = request.DefaultNarrativeAlignment,
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
            .SelectMany(project => project.DossierImages
                .Where(image => image.PhotoId.HasValue)
                .Select(image =>
                {
                    var geometry = ResolveDossierSlotGeometry(
                        project.EffectiveDossierLayout,
                        image.Role,
                        project.DossierImageCount,
                        project.DossierPrimaryImageHeightPoints);
                    return new BrochurePhotoRenderRequest(
                        project.ProjectId,
                        image.PhotoId!.Value,
                        image.FocalX,
                        image.FocalY,
                        geometry.Width,
                        geometry.Height)
                    {
                        FitMode = image.FitMode == CompendiumImageFitMode.Fit
                            ? BrochurePhotoFitMode.Fit
                            : BrochurePhotoFitMode.Fill,
                        PadFitToTarget = false
                    };
                }))
            .GroupBy(request => request.PhotoId)
            .Select(group => group.First())
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
                var renderedDossierImages = project.DossierImages
                    .Where(image => image.PhotoId.HasValue)
                    .Select(image => new CompendiumPdfProjectImage(
                        image.Role,
                        renderedPhotos.TryGetValue(image.PhotoId!.Value, out var rendered) ? rendered.Content : null,
                        image.FitMode,
                        image.PhotoId))
                    .ToArray();
                var primaryImage = renderedDossierImages.FirstOrDefault(image => image.Role == CompendiumDossierImageRole.Primary);
                var photoBytes = primaryImage?.Content;

                projects.Add(new CompendiumPdfProjectSection(
                    project.ProjectId,
                    CompendiumPublicationTextSanitizer.Sanitize(project.ProjectName),
                    CompendiumPublicationTextSanitizer.Sanitize(project.CaseFileNumber),
                    CompendiumPublicationTextSanitizer.Sanitize(group.TechnicalCategoryName),
                    CompendiumPublicationTextSanitizer.Sanitize(project.CompletionYearDisplay),
                    CompendiumPublicationTextSanitizer.Sanitize(project.SponsoringLineDirectorateDisplay),
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
                    ImageFitMode = project.ImageFitMode,
                    DossierLayoutRequested = project.DossierLayoutOverride,
                    DossierLayout = project.EffectiveDossierLayout,
                    DossierLayoutReason = project.DossierLayoutReason,
                    DossierPrimaryImageHeightPoints = project.DossierPrimaryImageHeightPoints,
                    DossierNarrativeFontScale = project.DossierNarrativeFontScale,
                    DossierFirstPageNarrativeBudget = project.DossierFirstPageNarrativeBudget,
                    DossierFirstPageSpecificationCount = project.DossierFirstPageSpecificationCount,
                    DossierSpecificationColumns = project.DossierSpecificationColumns,
                    DossierProgrammeColumns = project.DossierProgrammeColumns,
                    BalancedTextFlowMode = project.BalancedTextFlowMode,
                    NarrativeAlignment = project.NarrativeAlignment,
                    NarrativeFlow = project.NarrativeFlow,
                    EstimatedDossierPageCount = project.EstimatedDossierPageCount,
                    DossierPaginationNote = project.DossierPaginationNote,
                    DossierPaginationReason = project.DossierPaginationReason,
                    Images = renderedDossierImages,
                    ProgrammeModules = project.ProgrammeModules
                        .Select(module => module with
                        {
                            Label = CompendiumPublicationTextSanitizer.Sanitize(module.Label),
                            Value = CompendiumPublicationTextSanitizer.Sanitize(module.Value)
                        })
                        .ToArray(),
                    IprCredentials = project.IprCredentials,
                    TechnologyTransfer = project.TechnologyTransfer,
                    TechnicalSpecifications = project.TechnicalSpecifications.Select(CompendiumPublicationTextSanitizer.Sanitize).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray()
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
        var requiredSlots = CompendiumCoverTemplatePolicy.ResolveSlots(configured.FrontTemplate, configured.BackTemplate);
        var strictQuartet = configured.FrontTemplate == CompendiumFrontCoverTemplate.PortfolioQuartet;
        var configuredBySlot = configured.Images
            .GroupBy(item => (item.Surface, Slot: item.SlotKey.ToUpperInvariant()))
            .ToDictionary(group => group.Key, group => group.First());
        var candidates = BuildAutomaticCoverCandidates(projects, preferences);
        if (strictQuartet && candidates.Count > 0)
        {
            var probes = await _photoService.ProbeAsync(
                candidates.Select(item => new BrochurePhotoReference(item.ProjectId, item.PhotoId)).Distinct().ToArray(),
                cancellationToken);
            candidates = candidates
                .Where(item => probes.TryGetValue(item.PhotoId, out var probe)
                               && probe.ProjectId == item.ProjectId
                               && probe.IsReady)
                .ToArray();
        }
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

            var effectiveFitMode = CompendiumCoverTemplatePolicy.NormalizeFitMode(required.Surface, configured.FrontTemplate, slot.FitMode);
            if (slot.ImageMode == CompendiumCoverImageMode.None)
            {
                if (strictQuartet && required.Surface == CompendiumCoverSurface.Front)
                    throw new InvalidOperationException("Portfolio Quartet requires four valid photographs; image slots cannot be disabled.");
                rendered.Add(new CompendiumPdfCoverImage(required.Surface, required.SlotKey, null, effectiveFitMode));
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
                if (strictQuartet && used.Contains((explicitProject, explicitPhoto)))
                    throw new InvalidOperationException("Portfolio Quartet requires four different photographs; the same photograph is assigned to more than one slot.");
            }
            else
            {
                candidate = candidates.FirstOrDefault(item =>
                                !used.Contains((item.ProjectId, item.PhotoId))
                                && !usedProjects.Contains(item.ProjectId))
                            ?? candidates.FirstOrDefault(item => !used.Contains((item.ProjectId, item.PhotoId)));
                if (!strictQuartet) candidate ??= candidates.FirstOrDefault();
            }

            if (candidate is null)
            {
                if (strictQuartet && required.Surface == CompendiumCoverSurface.Front)
                    throw new InvalidOperationException("Portfolio Quartet requires four distinct usable photographs, but fewer than four can currently be resolved.");
                rendered.Add(new CompendiumPdfCoverImage(required.Surface, required.SlotKey, null, effectiveFitMode));
                continue;
            }

            var geometry = CompendiumCoverTemplatePolicy.ResolveGeometry(configured.FrontTemplate, configured.BackTemplate, required.Surface, required.SlotKey);
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
                        FitMode = effectiveFitMode == CompendiumImageFitMode.Fit ? BrochurePhotoFitMode.Fit : BrochurePhotoFitMode.Fill,
                        PadFitToTarget = false
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
                    effectiveFitMode,
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
                if (strictQuartet && required.Surface == CompendiumCoverSurface.Front)
                    throw new InvalidOperationException($"Portfolio Quartet image '{required.SlotKey}' could not be rendered. Choose another photograph.");
                rendered.Add(new CompendiumPdfCoverImage(required.Surface, required.SlotKey, null, effectiveFitMode));
            }
        }

        if (strictQuartet)
        {
            var quartet = rendered.Where(image => image.Surface == CompendiumCoverSurface.Front).ToArray();
            if (quartet.Length < 4 || quartet.Any(image => image.Content is not { Length: > 0 })
                || quartet.Where(image => image.ProjectId.HasValue && image.PhotoId.HasValue)
                    .Select(image => (image.ProjectId!.Value, image.PhotoId!.Value)).Distinct().Count() < 4)
                throw new InvalidOperationException("Portfolio Quartet requires four distinct, resolvable photographs before final issue.");
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
                    var priority = preference.SuitableForCoverHero ? 800 : 550;
                    var focalX = project.CoverPhotoId == preference.PhotoId ? project.PrimaryFocalX : .5d;
                    var focalY = project.CoverPhotoId == preference.PhotoId ? project.PrimaryFocalY : .5d;
                    result.Add(new CoverCandidate(project.ProjectId, preference.PhotoId, focalX, focalY,
                        priority + (project.IsReviewed ? 25 : 0) + Math.Min(40, (project.EffectiveDpi ?? 0) / 10)));
                }
            }

            if (project.CoverPhotoId is int photoId)
            {
                result.Add(new CoverCandidate(
                    project.ProjectId,
                    photoId,
                    project.PrimaryFocalX,
                    project.PrimaryFocalY,
                    220 + CoverHeroSourcePriority(project.CoverPhotoSource) * 25 + (project.IsReviewed ? 15 : 0) + Math.Min(30, (project.EffectiveDpi ?? 0) / 10) - ((project.EffectiveDpi ?? 200) < CompendiumImageQualityPolicy.MinimumLargeImageDpi ? 90 : 0)));
            }
        }
        return result
            .GroupBy(item => (item.ProjectId, item.PhotoId))
            .Select(group => group.OrderByDescending(item => item.Priority).First())
            .OrderByDescending(item => item.Priority)
            .ToArray();
    }


    private static (int Width, int Height) ResolveDossierSlotGeometry(
        CompendiumDossierLayout layout,
        CompendiumDossierImageRole role,
        int imageCount,
        float primaryImageHeightPoints)
    {
        imageCount = Math.Clamp(imageCount, 1, 3);
        primaryImageHeightPoints = Math.Max(1f, primaryImageHeightPoints);

        static int HeightFor(int pixelWidth, float frameWidthPoints, float frameHeightPoints)
            => Math.Max(320, (int)Math.Round(pixelWidth * frameHeightPoints / Math.Max(1f, frameWidthPoints)));

        if (layout == CompendiumDossierLayout.MultiImageEditorial)
        {
            const float mosaicWidth = CompendiumLayoutMetrics.ContentWidthPoints;
            const float gap = 7f;
            var usableWidth = mosaicWidth - gap;
            var primaryWidthPoints = usableWidth * 1.55f / 2.55f;
            var supportingWidthPoints = usableWidth - primaryWidthPoints;
            if (role == CompendiumDossierImageRole.Primary)
            {
                return (1500, HeightFor(1500, primaryWidthPoints, primaryImageHeightPoints));
            }

            var supportingHeight = imageCount >= 3
                ? Math.Max(80f, (primaryImageHeightPoints - gap) / 2f)
                : primaryImageHeightPoints;
            return (1100, HeightFor(1100, supportingWidthPoints, supportingHeight));
        }

        if (layout == CompendiumDossierLayout.Balanced)
        {
            const float interColumnGap = 13f;
            var usableWidth = CompendiumLayoutMetrics.ContentWidthPoints - interColumnGap;
            var frameWidth = usableWidth * 1.12f / 2f;
            return (1350, HeightFor(1350, frameWidth, primaryImageHeightPoints));
        }

        return (1800, HeightFor(1800, CompendiumLayoutMetrics.ContentWidthPoints, primaryImageHeightPoints));
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
