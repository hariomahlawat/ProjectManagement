using System.Globalization;
using System.Diagnostics;
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
    private static readonly SemaphoreSlim GenerationGate = new(
        CompendiumBuildIdentity.MaximumConcurrentGenerations,
        CompendiumBuildIdentity.MaximumConcurrentGenerations);

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
        var queuedAt = Stopwatch.GetTimestamp();
        await GenerationGate.WaitAsync(cancellationToken);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            _logger.LogInformation(
                "Compendium generation admitted. Build={Build}, QueueMilliseconds={QueueMilliseconds}, SelectedProjects={SelectedProjects}",
                CompendiumBuildIdentity.BuildStamp,
                Stopwatch.GetElapsedTime(queuedAt, startedAt).TotalMilliseconds,
                request.ProjectSelections?.Count ?? request.SelectedProjectIds?.Count ?? 0);

            var result = await GenerateCoreAsync(request, cancellationToken);
            _logger.LogInformation(
                "Compendium generation completed. Build={Build}, DurationMilliseconds={DurationMilliseconds}, PdfBytes={PdfBytes}, PhysicalPages={PhysicalPages}",
                CompendiumBuildIdentity.BuildStamp,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                result.Bytes.Length,
                result.PhysicalPageCount);
            return result;
        }
        finally
        {
            GenerationGate.Release();
        }
    }

    private async Task<CompendiumExportResult> GenerateCoreAsync(
        CompendiumExportRequest request,
        CancellationToken cancellationToken)
    {

        var authoredSelections = ResolveSelections(request);
        CompendiumPdfDataDto data;
        try
        {
            data = authoredSelections.Count > 0
                ? await _readService.GetPublicationAsync(
                    new CompendiumPublicationRequest(
                        authoredSelections,
                        request.Title,
                        request.Subtitle,
                        request.Edition)
                    {
                        NarrativeSource = request.NarrativeSource,
                        DefaultNarrativeAlignment = request.DefaultNarrativeAlignment,
                        ProjectParticularsStyle = request.ProjectParticularsStyle,
                        GroupingMode = request.GroupingMode,
                        SortMode = request.SortMode,
                        Sections = request.Sections,
                        CoverDesign = request.CoverDesign,
                        PhotoPreferences = request.PhotoPreferences
                    },
                    cancellationToken)
                : await _readService.GetProliferationCompendiumAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CompendiumPdfGenerationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CompendiumPdfGenerationException(
                CompendiumPdfGenerationStage.PublicationRead,
                "The Compendium publication snapshot could not be read from PRISM. No PDF was issued.",
                exception);
        }

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
                "Compendium publication image preparation failed. Stage={Stage}. Text-led project layouts will be used where necessary.",
                CompendiumPdfGenerationStage.ImagePreparation);
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
                    .Select(image =>
                    {
                        var rendered = renderedPhotos.GetValueOrDefault(image.PhotoId!.Value);
                        return new CompendiumPdfProjectImage(
                            image.Role,
                            rendered?.Content,
                            image.FitMode,
                            image.PhotoId,
                            rendered?.SourceWidth ?? image.SourceWidth,
                            rendered?.SourceHeight ?? image.SourceHeight);
                    })
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
                    DossierFirstPageNarrativeHeightPoints = project.DossierFirstPageNarrativeHeightPoints,
                    DossierFirstPageSpecificationCount = project.DossierFirstPageSpecificationCount,
                    DossierSpecificationColumns = project.DossierSpecificationColumns,
                    DossierProgrammeColumns = project.DossierProgrammeColumns,
                    ProjectParticularsStyle = project.ProjectParticularsStyle,
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
                    AdditionalNote = CompendiumPublicationTextSanitizer.Sanitize(project.AdditionalNote),
                    TechnicalSpecifications = project.TechnicalSpecifications.Select(CompendiumPublicationTextSanitizer.Sanitize).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray()
                });
            }

            categories.Add(new CompendiumPdfCategorySection(group.TechnicalCategoryName, projects));
        }

        CompendiumPdfCoverDesign? coverDesign;
        try
        {
            coverDesign = await ResolveCoverDesignAsync(request, publicationProjects, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CompendiumPdfGenerationException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            throw new CompendiumPdfGenerationException(
                CompendiumPdfGenerationStage.CoverResolution,
                exception.Message,
                exception);
        }
        catch (Exception exception)
        {
            throw new CompendiumPdfGenerationException(
                CompendiumPdfGenerationStage.CoverResolution,
                "The selected Compendium cover imagery could not be prepared on this server. Re-select the image or use Automatic cover imagery.",
                exception);
        }
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

        CompendiumPagePlan plan;
        try
        {
            plan = _pagePlanner.Plan(context);
        }
        catch (CompendiumPdfGenerationException)
        {
            throw;
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("DM Sans", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("publication font", StringComparison.OrdinalIgnoreCase))
        {
            throw new CompendiumPdfGenerationException(
                CompendiumPdfGenerationStage.FontInitialization,
                "The Compendium cannot be generated because the required DM Sans publication fonts could not be loaded on this server. Ensure the published publication-font package is present and restart PRISM.",
                exception);
        }
        catch (Exception exception)
        {
            throw new CompendiumPdfGenerationException(
                CompendiumPdfGenerationStage.PagePlanning,
                "The Compendium physical page plan could not be calculated from the selected publication content. No PDF was issued.",
                exception);
        }

        context = context with { Plan = plan };
        byte[] pdfBytes;
        try
        {
            pdfBytes = _pdfBuilder.Build(context);
        }
        catch (CompendiumPdfGenerationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CompendiumPdfGenerationException(
                CompendiumPdfGenerationStage.PdfComposition,
                "The Compendium PDF renderer could not complete the publication on this server. No PDF was issued.",
                exception);
        }

        CompendiumPdfVerificationResult verification;
        try
        {
            verification = _compositionVerifier.Verify(pdfBytes, context, plan);
        }
        catch (CompendiumPdfCompositionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CompendiumPdfGenerationException(
                CompendiumPdfGenerationStage.PdfVerification,
                "The generated Compendium could not be reopened and physically verified on this server. No PDF was issued.",
                exception);
        }

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
        var candidates = CompendiumCoverAutomaticImagePolicy.BuildCandidates(projects, preferences);
        var selectedProjectIds = projects.Select(project => project.ProjectId).ToHashSet();
        var allocationReferences = candidates
            .Select(candidate => new BrochurePhotoReference(candidate.ProjectId, candidate.PhotoId))
            .Concat(configured.Images
                .Where(slot => slot.ImageMode != CompendiumCoverImageMode.None
                               && slot.ProjectId is > 0
                               && slot.PhotoId is > 0
                               && selectedProjectIds.Contains(slot.ProjectId.Value))
                .Select(slot => new BrochurePhotoReference(slot.ProjectId!.Value, slot.PhotoId!.Value)))
            .Distinct()
            .ToArray();
        HashSet<(int ProjectId, int PhotoId)> usableForAllocation;
        if (strictQuartet && allocationReferences.Length > 0)
        {
            var probes = await _photoService.ProbeAsync(
                allocationReferences,
                cancellationToken);
            usableForAllocation = allocationReferences
                .Where(reference => probes.TryGetValue(reference.PhotoId, out var probe)
                                    && probe.ProjectId == reference.ProjectId
                                    && probe.IsReady)
                .Select(reference => (reference.ProjectId, reference.PhotoId))
                .ToHashSet();
            candidates = candidates
                .Where(item => usableForAllocation.Contains((item.ProjectId, item.PhotoId)))
                .ToArray();
        }
        else
        {
            // Non-Quartet export keeps the saved automatic identity first and
            // lets image rendering perform its normal per-slot fallback if a
            // source becomes unavailable after the cover was saved.
            usableForAllocation = allocationReferences
                .Select(reference => (reference.ProjectId, reference.PhotoId))
                .ToHashSet();
        }

        configured = configured with
        {
            Images = CompendiumCoverSlotAssignmentPolicy.Resolve(
                configured.FrontTemplate,
                configured.BackTemplate,
                configured.Images,
                candidates,
                usableForAllocation)
        };
        var configuredBySlot = configured.Images
            .GroupBy(item => (item.Surface, Slot: item.SlotKey.ToUpperInvariant()))
            .ToDictionary(group => group.Key, group => group.First());
        var reservedExplicitPhotos = requiredSlots
            .Select(requirement => configuredBySlot.GetValueOrDefault(
                (requirement.Surface, requirement.SlotKey.ToUpperInvariant())))
            .Where(slot => slot?.ImageMode == CompendiumCoverImageMode.Explicit
                           && slot.ProjectId is > 0
                           && slot.PhotoId is > 0)
            .Select(slot => (
                Surface: slot!.Surface,
                ProjectId: slot.ProjectId!.Value,
                PhotoId: slot.PhotoId!.Value))
            .ToHashSet();
        var reservedExplicitProjects = reservedExplicitPhotos
            .Select(item => (item.Surface, item.ProjectId))
            .ToHashSet();
        // Front and back covers are curated independently. Keeping the surface in the
        // allocation key prevents a front-cover edit from silently changing an automatic
        // back-cover selection (and vice versa). Portfolio Quartet uniqueness is therefore
        // enforced across its four front slots, not across an unrelated back-cover slot.
        var used = new HashSet<(CompendiumCoverSurface Surface, int ProjectId, int PhotoId)>();
        var usedProjects = new HashSet<(CompendiumCoverSurface Surface, int ProjectId)>();
        var rendered = new List<CompendiumPdfCoverImage>();

        foreach (var required in requiredSlots)
        {
            var strictDistinctSurface = strictQuartet
                                        && required.Surface == CompendiumCoverSurface.Front;
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

            var effectiveFitMode = CompendiumCoverTemplatePolicy.NormalizeFitMode(
                required.Surface,
                configured.FrontTemplate,
                slot.FitMode);

            if (slot.ImageMode == CompendiumCoverImageMode.None)
            {
                if (required.Required)
                {
                    throw new InvalidOperationException(
                        $"{CoverSlotDisplay(required.Surface, required.SlotKey)} is required by the selected cover template.");
                }

                rendered.Add(new CompendiumPdfCoverImage(required.Surface, required.SlotKey, null, effectiveFitMode));
                continue;
            }

            var geometry = CompendiumCoverTemplatePolicy.ResolveGeometry(
                configured.FrontTemplate,
                configured.BackTemplate,
                required.Surface,
                required.SlotKey);

            if (slot.ImageMode == CompendiumCoverImageMode.Explicit)
            {
                if (slot.ProjectId is not int explicitProject
                    || slot.PhotoId is not int explicitPhoto
                    || !projects.Any(project => project.ProjectId == explicitProject))
                {
                    throw new InvalidOperationException(
                        $"The selected {required.Surface.ToString().ToLowerInvariant()} cover image for slot '{required.SlotKey}' is no longer available in this Compendium.");
                }

                if (strictDistinctSurface
                    && used.Contains((required.Surface, explicitProject, explicitPhoto)))
                {
                    throw new InvalidOperationException(
                        "Portfolio Quartet requires four different photographs; the same photograph is assigned to more than one slot.");
                }

                BrochurePublicationImage? image;
                try
                {
                    image = await _photoService.RenderAsync(
                        new BrochurePhotoRenderRequest(
                            explicitProject,
                            explicitPhoto,
                            ClampFocal(slot.FocalX),
                            ClampFocal(slot.FocalY),
                            geometry.Width,
                            geometry.Height)
                        {
                            FitMode = effectiveFitMode == CompendiumImageFitMode.Fit
                                ? BrochurePhotoFitMode.Fit
                                : BrochurePhotoFitMode.Fill,
                            PadFitToTarget = false
                        },
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Explicit Compendium cover image failed to render. Surface={Surface}, Slot={Slot}, ProjectId={ProjectId}, PhotoId={PhotoId}.",
                        required.Surface,
                        required.SlotKey,
                        explicitProject,
                        explicitPhoto);
                    throw new InvalidOperationException(
                        $"The selected {required.Surface.ToString().ToLowerInvariant()} cover image for slot '{required.SlotKey}' could not be rendered on this server. Re-select the image or use Automatic cover imagery.",
                        exception);
                }

                if (image?.Content is not { Length: > 0 })
                {
                    throw new InvalidOperationException(
                        $"The selected {required.Surface.ToString().ToLowerInvariant()} cover image for slot '{required.SlotKey}' could not be rendered. Choose another image.");
                }

                rendered.Add(new CompendiumPdfCoverImage(
                    required.Surface,
                    required.SlotKey,
                    image.Content,
                    effectiveFitMode,
                    explicitProject,
                    explicitPhoto));
                used.Add((required.Surface, explicitProject, explicitPhoto));
                usedProjects.Add((required.Surface, explicitProject));
                continue;
            }

            var sticky = slot.ProjectId is > 0 && slot.PhotoId is > 0
                ? new CompendiumCoverAutomaticImagePolicy.Candidate(
                    slot.ProjectId.Value,
                    slot.PhotoId.Value,
                    ClampFocal(slot.FocalX),
                    ClampFocal(slot.FocalY),
                    int.MaxValue)
                : null;
            var stickyConflicts = sticky is not null
                                  && (reservedExplicitPhotos.Contains((required.Surface, sticky.ProjectId, sticky.PhotoId))
                                      || (strictDistinctSurface
                                          && used.Contains((required.Surface, sticky.ProjectId, sticky.PhotoId))));
            IEnumerable<CompendiumCoverAutomaticImagePolicy.Candidate> stickySequence =
                sticky is not null && !stickyConflicts
                    ? new[] { sticky }
                    : Array.Empty<CompendiumCoverAutomaticImagePolicy.Candidate>();
            var automaticSequence = stickySequence
                .Concat(candidates.Where(candidate =>
                    !used.Contains((required.Surface, candidate.ProjectId, candidate.PhotoId))
                    && !usedProjects.Contains((required.Surface, candidate.ProjectId))
                    && !reservedExplicitPhotos.Contains((required.Surface, candidate.ProjectId, candidate.PhotoId))
                    && !reservedExplicitProjects.Contains((required.Surface, candidate.ProjectId))))
                .Concat(candidates.Where(candidate =>
                    !used.Contains((required.Surface, candidate.ProjectId, candidate.PhotoId))
                    && !reservedExplicitPhotos.Contains((required.Surface, candidate.ProjectId, candidate.PhotoId))))
                .Concat(strictDistinctSurface
                    ? Array.Empty<CompendiumCoverAutomaticImagePolicy.Candidate>()
                    : candidates.Where(candidate =>
                        !reservedExplicitPhotos.Contains((required.Surface, candidate.ProjectId, candidate.PhotoId))))
                .GroupBy(candidate => (candidate.ProjectId, candidate.PhotoId))
                .Select(group => group.First())
                .ToArray();

            CompendiumPdfCoverImage? resolvedAutomatic = null;
            foreach (var candidate in automaticSequence)
            {
                try
                {
                    var image = await _photoService.RenderAsync(
                        new BrochurePhotoRenderRequest(
                            candidate.ProjectId,
                            candidate.PhotoId,
                            candidate.FocalX,
                            candidate.FocalY,
                            geometry.Width,
                            geometry.Height)
                        {
                            FitMode = effectiveFitMode == CompendiumImageFitMode.Fit
                                ? BrochurePhotoFitMode.Fit
                                : BrochurePhotoFitMode.Fill,
                            PadFitToTarget = false
                        },
                        cancellationToken);

                    if (image?.Content is not { Length: > 0 })
                    {
                        continue;
                    }

                    resolvedAutomatic = new CompendiumPdfCoverImage(
                        required.Surface,
                        required.SlotKey,
                        image.Content,
                        effectiveFitMode,
                        candidate.ProjectId,
                        candidate.PhotoId);
                    used.Add((required.Surface, candidate.ProjectId, candidate.PhotoId));
                    usedProjects.Add((required.Surface, candidate.ProjectId));
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Automatic Compendium cover candidate failed; trying the next ranked candidate. Surface={Surface}, Slot={Slot}, ProjectId={ProjectId}, PhotoId={PhotoId}.",
                        required.Surface,
                        required.SlotKey,
                        candidate.ProjectId,
                        candidate.PhotoId);
                }
            }

            if (resolvedAutomatic is not null)
            {
                rendered.Add(resolvedAutomatic);
                continue;
            }

            if (required.Required)
            {
                throw new InvalidOperationException(
                    $"{CoverSlotDisplay(required.Surface, required.SlotKey)} could not be resolved from the available publication photography.");
            }

            rendered.Add(new CompendiumPdfCoverImage(required.Surface, required.SlotKey, null, effectiveFitMode));
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
            PublicationTheme = configured.PublicationTheme,
            BackgroundTreatment = configured.BackgroundTreatment,
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

    private static string CoverSlotDisplay(CompendiumCoverSurface surface, string slotKey)
    {
        var surfaceLabel = surface == CompendiumCoverSurface.Front ? "Front cover" : "Back cover";
        var slotLabel = string.Equals(slotKey, "Hero", StringComparison.OrdinalIgnoreCase)
            ? "hero image"
            : string.Equals(slotKey, "Secondary1", StringComparison.OrdinalIgnoreCase)
                ? "supporting image 1"
                : string.Equals(slotKey, "Secondary2", StringComparison.OrdinalIgnoreCase)
                    ? "supporting image 2"
                    : string.Equals(slotKey, "Secondary3", StringComparison.OrdinalIgnoreCase)
                        ? "supporting image 3"
                        : $"image slot '{slotKey}'";
        return $"{surfaceLabel} {slotLabel}";
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
