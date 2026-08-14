using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Services.Publications;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Pages.Projects.Publications.Compendium;

[Authorize]
public sealed class IndexModel : PageModel
{
    private const int MaximumSelectedProjects = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICompendiumReadService _readService;
    private readonly ICompendiumExportService _exportService;
    private readonly ICompendiumPresetService _presetService;
    private readonly IBrochurePhotoService _photoService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ICompendiumReadService readService,
        ICompendiumExportService exportService,
        ICompendiumPresetService presetService,
        IBrochurePhotoService photoService,
        ILogger<IndexModel> logger)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _presetService = presetService ?? throw new ArgumentNullException(nameof(presetService));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public GenerateCompendiumInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public long? PresetId { get; set; }

    [BindProperty]
    public string? ActivePresetRowVersion { get; set; }

    public IReadOnlyList<CompendiumCandidateProjectVm> Projects { get; private set; }
        = Array.Empty<CompendiumCandidateProjectVm>();

    public IReadOnlyList<string> ProjectCategories { get; private set; }
        = Array.Empty<string>();

    public IReadOnlyList<string> TechnicalCategories { get; private set; }
        = Array.Empty<string>();

    public CompendiumPreflightDto Preflight { get; private set; } = CompendiumPreflightDto.Empty;

    public IReadOnlyList<CompendiumPresetSummaryVm> SavedCompendiums { get; private set; }
        = Array.Empty<CompendiumPresetSummaryVm>();

    public CompendiumPresetSummaryVm? ActivePreset { get; private set; }
    public IReadOnlyList<CompendiumPresetDiagnostic> PresetDiagnostics { get; private set; }
        = Array.Empty<CompendiumPresetDiagnostic>();

    public bool CanManagePresets
        => User.IsInRole(RoleNames.HoD) || User.IsInRole(RoleNames.Comdt);

    public bool CanMaintainProjectData
        => User.IsInRole(RoleNames.Admin)
           || User.IsInRole(RoleNames.HoD)
           || User.IsInRole(RoleNames.ProjectOffice)
           || User.IsInRole(RoleNames.ProjectOfficeAlternate);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApplyDefaultSettings();
        await LoadWorkspaceAsync(loadPreset: PresetId is > 0, cancellationToken);
    }

    public async Task<IActionResult> OnGetPhotoAsync(
        int projectId,
        int photoId,
        string? mode,
        int v,
        CancellationToken cancellationToken)
    {
        var kind = string.Equals(mode, "source", StringComparison.OrdinalIgnoreCase)
            ? BrochurePhotoPreviewKind.Source
            : BrochurePhotoPreviewKind.Thumbnail;
        var preview = await _photoService.GetPreviewAsync(
            projectId,
            photoId,
            kind,
            cancellationToken);
        if (preview is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private,max-age=86400";
        Response.Headers["X-PRISM-Publication-Photo-Source"] = preview.SourceVariant;
        Response.Headers["X-PRISM-Publication-Photo-Size"] = $"{preview.SourceWidth}x{preview.SourceHeight}";
        Response.Headers["X-PRISM-Publication-Photo-Quality"] = preview.Quality.ToString();
        return File(preview.Content, preview.ContentType);
    }

    public async Task<IActionResult> OnPostPreflightAsync(CancellationToken cancellationToken)
    {
        NormalizeInput();
        var data = await _readService.GetPublicationAsync(
            ToPublicationRequest(),
            cancellationToken);

        return new JsonResult(new
        {
            selected = data.Preflight.SelectedProjectCount,
            blockers = data.Preflight.BlockerCount,
            warnings = data.Preflight.TotalWarningCount,
            info = data.Preflight.InformationCount,
            categories = data.Preflight.CategoryCount,
            groupingMode = data.GroupingMode.ToString(),
            sortMode = data.SortMode.ToString(),
            narrativeSource = data.NarrativeSource.ToString(),
            canGenerate = data.Preflight.CanGenerate,
            reviewed = data.Groups.SelectMany(group => group.Projects).Count(project => project.IsReviewed),
            allReviewed = data.Preflight.SelectedProjectCount > 0
                          && data.Groups.SelectMany(group => group.Projects).All(project => project.IsReviewed),
            projects = data.Groups
                .SelectMany(group => group.Projects.Select(project => new { group.SectionName, Project = project }))
                .OrderBy(item => item.Project.SortOrder)
                .Select(item => new
                {
                    ProjectId = item.Project.ProjectId,
                    sectionName = item.SectionName,
                    publicationYear = item.Project.PublicationYear,
                    item.Project.ReviewFingerprint,
                    item.Project.IsReviewed,
                    item.Project.IsReviewStale,
                    resolvedPhotoId = item.Project.CoverPhotoId,
                    imageSelectionMode = item.Project.ImageSelectionMode.ToString().ToLowerInvariant(),
                    item.Project.EffectiveDpi,
                    imageQuality = item.Project.ImageQuality.ToString().ToLowerInvariant(),
                    item.Project.ExplicitPhotoUnavailable
                }),
            findings = data.Preflight.Findings.Select(finding => new
            {
                severity = finding.Severity.ToString().ToLowerInvariant(),
                finding.Code,
                finding.Message,
                finding.ProjectId,
                finding.ProjectName
            })
        });
    }

    public async Task<IActionResult> OnPostReviewAsync(
        int projectId,
        CancellationToken cancellationToken)
    {
        NormalizeInput();
        var selection = ParseSelections().FirstOrDefault(item => item.ProjectId == projectId);
        if (selection is null)
        {
            return JsonError(
                StatusCodes.Status400BadRequest,
                "Select this project before reviewing it.");
        }

        var review = await _readService.GetReviewProjectAsync(
            selection,
            selection.NarrativeSourceOverride ?? ParseNarrativeSource(Input.NarrativeSource),
            cancellationToken);
        if (review is null)
        {
            return JsonError(
                StatusCodes.Status404NotFound,
                "The selected project is no longer available for publication.");
        }

        var returnUrl = Url.Page("/Projects/Publications/Compendium/Index", new { presetId = PresetId })
                        ?? "/Projects/Publications/Compendium";
        var completedEditUrl = CanMaintainProjectData
                               && string.Equals(review.LifecycleDisplay, "Completed", StringComparison.OrdinalIgnoreCase)
            ? Url.Page("/Projects/CompletedSummary/Edit", new { id = review.ProjectId, returnUrl })
            : null;

        return new JsonResult(new
        {
            review.ProjectId,
            review.ProjectName,
            review.LifecycleDisplay,
            review.ProjectCategoryName,
            review.TechnicalCategoryName,
            review.ArmServiceDisplay,
            review.CompletionDisplay,
            review.ProliferationAvailability,
            review.ProliferationCostLakhs,
            review.ProliferationCostDisplay,
            review.DescriptionMarkdown,
            narrativeSource = review.NarrativeSource.ToString(),
            review.NarrativeLabel,
            review.HasProjectBrief,
            review.HasCapabilityOverview,
            review.HasProjectDescription,
            review.ProjectBriefWordCount,
            review.CapabilityStatementCount,
            review.DescriptionWordCount,
            review.CustomSectionKey,
            review.CustomSectionName,
            review.UsesNarrativeOverride,
            review.ResolvedPhotoId,
            photoSelectionSource = review.PhotoSelectionSource.ToString().ToLowerInvariant(),
            imageSelectionMode = review.ImageSelectionMode.ToString().ToLowerInvariant(),
            review.FocalX,
            review.FocalY,
            review.EffectiveDpi,
            imageQuality = review.ImageQuality.ToString().ToLowerInvariant(),
            review.ReviewFingerprint,
            review.IsReviewed,
            review.IsReviewStale,
            review.ExplicitPhotoUnavailable,
            review.ImageFrameWidthPoints,
            review.ImageFrameHeightPoints,
            projectUrl = Url.Page("/Projects/Overview", new { id = review.ProjectId }),
            photosUrl = Url.Page("/Projects/Photos/Index", new { id = review.ProjectId }),
            completedEditUrl,
            photos = review.Photos.Select(photo => new
            {
                photo.PhotoId,
                photo.Caption,
                photo.Width,
                photo.Height,
                photo.IsCover,
                photo.IsLowResolution,
                photo.Version,
                photo.IsUsable,
                photo.SourceVariant,
                quality = photo.Quality.ToString().ToLowerInvariant(),
                thumbnailUrl = Url.Page(
                    "/Projects/Publications/Compendium/Index",
                    "Photo",
                    new { projectId = review.ProjectId, photoId = photo.PhotoId, mode = "thumb", v = photo.Version }),
                previewUrl = Url.Page(
                    "/Projects/Publications/Compendium/Index",
                    "Photo",
                    new { projectId = review.ProjectId, photoId = photo.PhotoId, mode = "source", v = photo.Version })
            })
        });
    }

    public Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
        => GenerateInternalAsync(preview: true, cancellationToken);

    public Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
        => GenerateInternalAsync(preview: false, cancellationToken);

    public async Task<IActionResult> OnPostSavePresetAsync(
        bool saveAsNew,
        string? presetName,
        string? presetDescription,
        CancellationToken cancellationToken)
    {
        if (!CanManagePresets)
        {
            return JsonError(
                StatusCodes.Status403Forbidden,
                "Only HoD or Comdt may maintain shared Compendium configurations.");
        }

        try
        {
            NormalizeInput();
            var actorUserId = ActorUserId();
            var configuration = ToPresetConfiguration();
            CompendiumPresetMutationResult result;

            if (saveAsNew || PresetId is not > 0)
            {
                result = await _presetService.CreateAsync(
                    actorUserId,
                    presetName ?? Input.Title,
                    presetDescription,
                    configuration,
                    cancellationToken);
            }
            else
            {
                result = await _presetService.UpdateAsync(
                    PresetId.Value,
                    actorUserId,
                    ActivePresetRowVersion ?? string.Empty,
                    configuration,
                    cancellationToken);
            }

            return new JsonResult(new
            {
                message = saveAsNew ? "Shared Compendium saved." : "Shared Compendium updated.",
                preset = result.Preset
            });
        }
        catch (CompendiumPresetConcurrencyException exception)
        {
            return JsonError(
                StatusCodes.Status409Conflict,
                exception.Message,
                "presetConflict");
        }
        catch (UnauthorizedAccessException exception)
        {
            return JsonError(StatusCodes.Status403Forbidden, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Compendium preset save failed.");
            return JsonError(StatusCodes.Status400BadRequest, exception.Message);
        }
    }

    public async Task<IActionResult> OnPostRenamePresetAsync(
        long presetId,
        string rowVersion,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _presetService.RenameAsync(
                presetId,
                ActorUserId(),
                rowVersion,
                name,
                cancellationToken);
            return new JsonResult(new
            {
                message = "Shared Compendium renamed.",
                preset = result.Preset
            });
        }
        catch (CompendiumPresetConcurrencyException exception)
        {
            return JsonError(StatusCodes.Status409Conflict, exception.Message, "presetConflict");
        }
        catch (UnauthorizedAccessException exception)
        {
            return JsonError(StatusCodes.Status403Forbidden, exception.Message);
        }
        catch (Exception exception)
        {
            return JsonError(StatusCodes.Status400BadRequest, exception.Message);
        }
    }

    public async Task<IActionResult> OnPostDuplicatePresetAsync(
        long presetId,
        string rowVersion,
        string name,
        string? description,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _presetService.DuplicateAsync(
                presetId,
                ActorUserId(),
                rowVersion,
                name,
                description,
                cancellationToken);
            return new JsonResult(new
            {
                message = "Shared Compendium duplicated.",
                preset = result.Preset
            });
        }
        catch (CompendiumPresetConcurrencyException exception)
        {
            return JsonError(StatusCodes.Status409Conflict, exception.Message, "presetConflict");
        }
        catch (UnauthorizedAccessException exception)
        {
            return JsonError(StatusCodes.Status403Forbidden, exception.Message);
        }
        catch (Exception exception)
        {
            return JsonError(StatusCodes.Status400BadRequest, exception.Message);
        }
    }

    public async Task<IActionResult> OnPostDeletePresetAsync(
        long presetId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await _presetService.DeleteAsync(
                presetId,
                ActorUserId(),
                rowVersion,
                cancellationToken);
            return new JsonResult(new { message = "Shared Compendium deleted." });
        }
        catch (CompendiumPresetConcurrencyException exception)
        {
            return JsonError(StatusCodes.Status409Conflict, exception.Message, "presetConflict");
        }
        catch (UnauthorizedAccessException exception)
        {
            return JsonError(StatusCodes.Status403Forbidden, exception.Message);
        }
        catch (Exception exception)
        {
            return JsonError(StatusCodes.Status400BadRequest, exception.Message);
        }
    }

    private async Task<IActionResult> GenerateInternalAsync(
        bool preview,
        CancellationToken cancellationToken)
    {
        NormalizeInput();
        var selections = ParseSelections();

        if (!ModelState.IsValid || selections.Count == 0)
        {
            const string message = "Select at least one project before generating the Compendium.";
            if (IsAjaxRequest())
            {
                return JsonError(StatusCodes.Status400BadRequest, message, "noSelection");
            }

            ModelState.AddModelError(string.Empty, message);
            await LoadWorkspaceAsync(loadPreset: false, cancellationToken);
            return Page();
        }

        try
        {
            var result = await _exportService.GenerateAsync(
                new CompendiumExportRequest(
                    HandlingMarking: Input.HandlingMarking,
                    SelectedProjectIds: selections.Select(selection => selection.ProjectId).ToArray(),
                    Title: Input.Title,
                    Subtitle: Input.Subtitle,
                    Edition: Input.Edition,
                    ProjectSelections: selections,
                    RequireAllReviewed: !preview,
                    CoverImageMode: ParseCoverImageMode(Input.CoverImageMode),
                    CoverHeroProjectId: Input.CoverHeroProjectId,
                    CoverHeroPhotoId: Input.CoverHeroPhotoId,
                    CoverFocalX: ClampFocal(Input.CoverFocalX),
                    CoverFocalY: ClampFocal(Input.CoverFocalY))
                {
                    NarrativeSource = ParseNarrativeSource(Input.NarrativeSource),
                    GroupingMode = ParseGroupingMode(Input.GroupingMode),
                    SortMode = ParseSortMode(Input.SortMode),
                    Sections = ParseSections()
                },
                cancellationToken);

            Response.Headers["X-PRISM-Publication-Composition-Verified"] = result.IsCompositionVerified ? "true" : "false";
            Response.Headers["X-PRISM-Publication-Page-Count"] = result.PhysicalPageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-PRISM-Publication-FileName"] = result.FileName;

            if (preview)
            {
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{result.FileName}\"";
                return File(result.Bytes, "application/pdf");
            }

            return File(result.Bytes, "application/pdf", result.FileName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Compendium PDF {Operation} failed.",
                preview ? "preview" : "generation");
            var message = preview
                ? "The Compendium preview could not be generated. Review publication readiness and try again."
                : exception is InvalidOperationException && exception.Message.Contains("Review all selected projects", StringComparison.Ordinal)
                    ? exception.Message
                    : "The Compendium could not be generated. Review publication readiness and try again.";

            if (IsAjaxRequest())
            {
                var code = exception is CompendiumPdfCompositionException
                    ? "compositionVerificationFailed"
                    : (!preview && exception.Message.Contains("Review all selected projects", StringComparison.Ordinal)
                        ? "reviewRequired"
                        : "generationFailed");
                return JsonError(StatusCodes.Status400BadRequest, message, code);
            }

            ModelState.AddModelError(string.Empty, message);
            await LoadWorkspaceAsync(loadPreset: false, cancellationToken);
            return Page();
        }
    }

    private async Task LoadWorkspaceAsync(
        bool loadPreset,
        CancellationToken cancellationToken)
    {
        Projects = await _readService.GetCandidateProjectsAsync(cancellationToken);
        ProjectCategories = DistinctSorted(Projects.Select(project => project.ProjectCategory));
        TechnicalCategories = DistinctSorted(Projects.Select(project => project.TechnicalCategory));
        SavedCompendiums = await _presetService.ListAsync(cancellationToken);

        if (loadPreset && PresetId is > 0)
        {
            try
            {
                var loaded = await _presetService.LoadAsync(PresetId.Value, cancellationToken);
                ActivePreset = loaded.Preset;
                PresetDiagnostics = loaded.Diagnostics;
                ActivePresetRowVersion = loaded.Preset.RowVersion;
                Input.Title = loaded.Configuration.Title;
                Input.Subtitle = loaded.Configuration.Subtitle;
                Input.Edition = loaded.Configuration.Edition;
                Input.HandlingMarking = loaded.Configuration.HandlingMarking;
                Input.CoverImageMode = loaded.Configuration.Cover.ImageMode.ToString();
                Input.CoverHeroProjectId = loaded.Configuration.Cover.HeroProjectId;
                Input.CoverHeroPhotoId = loaded.Configuration.Cover.HeroPhotoId;
                Input.CoverFocalX = loaded.Configuration.Cover.FocalX;
                Input.CoverFocalY = loaded.Configuration.Cover.FocalY;
                Input.NarrativeSource = loaded.Configuration.NarrativeSource.ToString();
                Input.GroupingMode = loaded.Configuration.GroupingMode.ToString();
                Input.SortMode = loaded.Configuration.SortMode.ToString();
                Input.CustomSectionsJson = SerializeSections(loaded.Configuration.Sections.Select(section =>
                    new CompendiumPublicationSection(section.SectionKey, section.Name, section.SortOrder)));
                Input.SelectedProjectIdsCsv = string.Join(',', loaded.Configuration.ProjectIds);
                Input.ProjectSelectionsJson = SerializeSelections(
                    loaded.Configuration.Projects.Select(project => new CompendiumProjectSelection(
                        project.ProjectId,
                        project.PrimaryPhotoId,
                        project.PrimaryFocalX,
                        project.PrimaryFocalY,
                        project.ImageSelectionMode,
                        ReviewFingerprint: null)
                    {
                        CustomSectionKey = project.CustomSectionKey,
                        CustomSectionName = project.CustomSectionName,
                        NarrativeSourceOverride = project.NarrativeSourceOverride
                    }));
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                PresetId = null;
            }
        }
        else if (PresetId is > 0)
        {
            ActivePreset = SavedCompendiums.FirstOrDefault(preset => preset.Id == PresetId.Value);
        }

        var selections = ParseSelections();
        if (selections.Count == 0)
        {
            Preflight = CompendiumPreflightDto.Empty with
            {
                CandidateProjectCount = Projects.Count,
                SelectedProjectCount = 0,
                BlockerCount = 1,
                WarningCount = 0,
                Findings = new[]
                {
                    new CompendiumFindingDto(
                        CompendiumFindingSeverity.Blocker,
                        "noSelection",
                        "Select at least one project to create a Compendium.")
                }
            };
        }
        else
        {
            Preflight = (await _readService.GetPublicationAsync(
                ToPublicationRequest(),
                cancellationToken)).Preflight;
        }
    }

    private void ApplyDefaultSettings()
    {
        if (string.IsNullOrWhiteSpace(Input.Title))
        {
            Input.Title = "SDD Simulators Compendium";
        }
        if (string.IsNullOrWhiteSpace(Input.Subtitle))
        {
            Input.Subtitle = "Detailed Project Reference";
        }
        if (string.IsNullOrWhiteSpace(Input.Edition))
        {
            Input.Edition = $"Capability Edition · {DateTime.Today.Year}";
        }
        if (string.IsNullOrWhiteSpace(Input.NarrativeSource))
        {
            Input.NarrativeSource = nameof(CompendiumNarrativeSource.ProjectBrief);
        }
        if (string.IsNullOrWhiteSpace(Input.GroupingMode))
        {
            Input.GroupingMode = nameof(CompendiumGroupingMode.TechnicalCategory);
        }
        if (string.IsNullOrWhiteSpace(Input.SortMode))
        {
            Input.SortMode = nameof(CompendiumSortMode.Manual);
        }
    }

    private void NormalizeInput()
    {
        ApplyDefaultSettings();
        Input.Title = Clean(Input.Title, 120) ?? "SDD Simulators Compendium";
        Input.Subtitle = Clean(Input.Subtitle, 160) ?? "Detailed Project Reference";
        Input.Edition = Clean(Input.Edition, 80) ?? $"Capability Edition · {DateTime.Today.Year}";
        Input.HandlingMarking = Clean(Input.HandlingMarking, 80);
        Input.NarrativeSource = ParseNarrativeSource(Input.NarrativeSource).ToString();
        Input.GroupingMode = ParseGroupingMode(Input.GroupingMode).ToString();
        Input.SortMode = ParseSortMode(Input.SortMode).ToString();
        Input.CoverImageMode = ParseCoverImageMode(Input.CoverImageMode).ToString();
        Input.CoverFocalX = ClampFocal(Input.CoverFocalX);
        Input.CoverFocalY = ClampFocal(Input.CoverFocalY);
        if (ParseCoverImageMode(Input.CoverImageMode) != CompendiumCoverImageMode.Explicit)
        {
            Input.CoverHeroProjectId = null;
            Input.CoverHeroPhotoId = null;
        }

        var sections = ParseSections();
        Input.CustomSectionsJson = SerializeSections(sections);

        var selections = ParseSelections();
        Input.SelectedProjectIdsCsv = string.Join(',', selections.Select(selection => selection.ProjectId));
        Input.ProjectSelectionsJson = SerializeSelections(selections);
    }

    private IReadOnlyList<int> ParseSelectedIds()
    {
        var seen = new HashSet<int>();
        return (Input.SelectedProjectIdsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var projectId) ? projectId : 0)
            .Where(projectId => projectId > 0 && seen.Add(projectId))
            .Take(MaximumSelectedProjects)
            .ToArray();
    }

    private IReadOnlyList<CompendiumProjectSelection> ParseSelections()
    {
        var orderedIds = ParseSelectedIds();
        if (orderedIds.Count == 0)
        {
            return Array.Empty<CompendiumProjectSelection>();
        }

        IReadOnlyList<CompendiumProjectSelectionPayload> payloads;
        try
        {
            if (string.IsNullOrWhiteSpace(Input.ProjectSelectionsJson))
            {
                payloads = Array.Empty<CompendiumProjectSelectionPayload>();
            }
            else
            {
                payloads = JsonSerializer.Deserialize<List<CompendiumProjectSelectionPayload>>(
                               Input.ProjectSelectionsJson,
                               JsonOptions)
                           ?? new List<CompendiumProjectSelectionPayload>();
            }
        }
        catch (JsonException)
        {
            payloads = Array.Empty<CompendiumProjectSelectionPayload>();
        }

        var payloadById = payloads
            .Where(payload => payload.ProjectId > 0)
            .GroupBy(payload => payload.ProjectId)
            .ToDictionary(group => group.Key, group => group.First());

        return orderedIds
            .Select(projectId =>
            {
                if (!payloadById.TryGetValue(projectId, out var payload))
                {
                    return new CompendiumProjectSelection(projectId);
                }

                var mode = Enum.TryParse<CompendiumImageSelectionMode>(
                               payload.ImageSelectionMode,
                               ignoreCase: true,
                               out var parsedMode)
                           && Enum.IsDefined(parsedMode)
                    ? parsedMode
                    : CompendiumImageSelectionMode.Automatic;

                return new CompendiumProjectSelection(
                    projectId,
                    mode == CompendiumImageSelectionMode.Explicit && payload.PrimaryPhotoId is > 0
                        ? payload.PrimaryPhotoId
                        : null,
                    ClampFocal(payload.FocalX),
                    ClampFocal(payload.FocalY),
                    mode,
                    CleanFingerprint(payload.ReviewFingerprint))
                {
                    CustomSectionKey = NormalizeSectionKey(payload.CustomSectionKey),
                    CustomSectionName = Clean(payload.CustomSectionName, 120),
                    NarrativeSourceOverride = ParseNullableNarrativeSource(payload.NarrativeSourceOverride)
                };
            })
            .ToArray();
    }

    private CompendiumPublicationRequest ToPublicationRequest()
        => new(ParseSelections(), Input.Title, Input.Subtitle, Input.Edition)
        {
            NarrativeSource = ParseNarrativeSource(Input.NarrativeSource),
            GroupingMode = ParseGroupingMode(Input.GroupingMode),
            SortMode = ParseSortMode(Input.SortMode),
            Sections = ParseSections()
        };

    private CompendiumPresetConfiguration ToPresetConfiguration()
        => new(
            Input.Title,
            Input.Subtitle,
            Input.Edition,
            Input.HandlingMarking,
            ParseSelections()
                .Select(selection => new CompendiumPresetProjectConfiguration(
                    selection.ProjectId,
                    selection.PrimaryPhotoId,
                    selection.FocalX,
                    selection.FocalY,
                    selection.ImageSelectionMode)
                {
                    CustomSectionKey = selection.CustomSectionKey,
                    CustomSectionName = selection.CustomSectionName,
                    NarrativeSourceOverride = selection.NarrativeSourceOverride
                })
                .ToArray())
        {
            NarrativeSource = ParseNarrativeSource(Input.NarrativeSource),
            GroupingMode = ParseGroupingMode(Input.GroupingMode),
            SortMode = ParseSortMode(Input.SortMode),
            Sections = ParseSections()
                .Select(section => new CompendiumPresetSectionConfiguration(section.SectionKey, section.Name, section.SortOrder))
                .ToArray(),
            Cover = new CompendiumCoverConfiguration(
                ParseCoverImageMode(Input.CoverImageMode),
                Input.CoverHeroProjectId,
                Input.CoverHeroPhotoId,
                ClampFocal(Input.CoverFocalX),
                ClampFocal(Input.CoverFocalY))
        };

    private string ActorUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new UnauthorizedAccessException("The current user account could not be resolved.");

    private static IReadOnlyList<string> DistinctSorted(IEnumerable<string?> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string SerializeSelections(IEnumerable<CompendiumProjectSelection> selections)
        => JsonSerializer.Serialize(
            selections.Select(selection => new CompendiumProjectSelectionPayload
            {
                ProjectId = selection.ProjectId,
                PrimaryPhotoId = selection.PrimaryPhotoId,
                FocalX = selection.FocalX,
                FocalY = selection.FocalY,
                ImageSelectionMode = selection.ImageSelectionMode.ToString(),
                ReviewFingerprint = selection.ReviewFingerprint,
                CustomSectionKey = selection.CustomSectionKey,
                CustomSectionName = selection.CustomSectionName,
                NarrativeSourceOverride = selection.NarrativeSourceOverride?.ToString()
            }),
            JsonOptions);

    private IReadOnlyList<CompendiumPublicationSection> ParseSections()
    {
        IReadOnlyList<CompendiumSectionPayload> payloads;
        try
        {
            payloads = string.IsNullOrWhiteSpace(Input.CustomSectionsJson)
                ? Array.Empty<CompendiumSectionPayload>()
                : JsonSerializer.Deserialize<List<CompendiumSectionPayload>>(Input.CustomSectionsJson, JsonOptions)
                  ?? new List<CompendiumSectionPayload>();
        }
        catch (JsonException)
        {
            payloads = Array.Empty<CompendiumSectionPayload>();
        }

        var result = new List<CompendiumPublicationSection>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var payload in payloads.OrderBy(section => section.SortOrder))
        {
            var name = Clean(payload.Name, 120);
            if (name is null || !names.Add(name))
            {
                continue;
            }

            var key = NormalizeSectionKey(payload.SectionKey) ?? $"sec-{Guid.NewGuid():N}";
            if (!keys.Add(key))
            {
                key = $"sec-{Guid.NewGuid():N}";
                keys.Add(key);
            }

            result.Add(new CompendiumPublicationSection(key, name, result.Count));
            if (result.Count >= 100)
            {
                break;
            }
        }

        // Backward compatibility for an in-flight Phase 25 browser payload that only contains
        // CustomSectionName on projects. This is intentionally a one-way normalization into v5.
        if (result.Count == 0)
        {
            IReadOnlyList<CompendiumProjectSelectionPayload> legacyProjects;
            try
            {
                legacyProjects = string.IsNullOrWhiteSpace(Input.ProjectSelectionsJson)
                    ? Array.Empty<CompendiumProjectSelectionPayload>()
                    : JsonSerializer.Deserialize<List<CompendiumProjectSelectionPayload>>(Input.ProjectSelectionsJson, JsonOptions)
                      ?? new List<CompendiumProjectSelectionPayload>();
            }
            catch (JsonException)
            {
                legacyProjects = Array.Empty<CompendiumProjectSelectionPayload>();
            }

            foreach (var project in legacyProjects)
            {
                var name = Clean(project.CustomSectionName, 120);
                if (name is null || !names.Add(name))
                {
                    continue;
                }

                var key = NormalizeSectionKey(project.CustomSectionKey) ?? $"sec-{Guid.NewGuid():N}";
                result.Add(new CompendiumPublicationSection(key, name, result.Count));
            }
        }

        return result;
    }

    private static string SerializeSections(IEnumerable<CompendiumPublicationSection> sections)
        => JsonSerializer.Serialize(
            sections.OrderBy(section => section.SortOrder).Select((section, index) => new CompendiumSectionPayload
            {
                SectionKey = NormalizeSectionKey(section.SectionKey) ?? $"sec-{Guid.NewGuid():N}",
                Name = Clean(section.Name, 120),
                SortOrder = index
            }),
            JsonOptions);

    private static CompendiumNarrativeSource? ParseNullableNarrativeSource(string? value)
        => Enum.TryParse<CompendiumNarrativeSource>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static string? NormalizeSectionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = new string(value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(40)
            .ToArray());
        return clean.Length == 0 ? null : clean;
    }

    private static CompendiumNarrativeSource ParseNarrativeSource(string? value)
        => Enum.TryParse<CompendiumNarrativeSource>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumNarrativeSource.ProjectBrief;

    private static CompendiumGroupingMode ParseGroupingMode(string? value)
        => Enum.TryParse<CompendiumGroupingMode>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumGroupingMode.TechnicalCategory;

    private static CompendiumSortMode ParseSortMode(string? value)
        => Enum.TryParse<CompendiumSortMode>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumSortMode.Manual;

    private static CompendiumCoverImageMode ParseCoverImageMode(string? value)
        => Enum.TryParse<CompendiumCoverImageMode>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : CompendiumCoverImageMode.Automatic;

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private static string? CleanFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim();
        return clean.Length <= 128 ? clean : clean[..128];
    }

    private static string? Clean(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd();
    }

    private bool IsAjaxRequest()
        => string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

    private static JsonResult JsonError(int statusCode, string message, string? code = null)
        => new(new { message, code }) { StatusCode = statusCode };

    public sealed class GenerateCompendiumInput
    {
        [Required, StringLength(120)]
        public string Title { get; set; } = "SDD Simulators Compendium";

        [Required, StringLength(160)]
        public string Subtitle { get; set; } = "Detailed Project Reference";

        [Required, StringLength(80)]
        public string Edition { get; set; } = string.Empty;

        [StringLength(80)]
        public string? HandlingMarking { get; set; }

        [StringLength(32)]
        public string NarrativeSource { get; set; } = nameof(CompendiumNarrativeSource.ProjectBrief);

        [StringLength(32)]
        public string GroupingMode { get; set; } = nameof(CompendiumGroupingMode.TechnicalCategory);

        [StringLength(32)]
        public string SortMode { get; set; } = nameof(CompendiumSortMode.Manual);

        [StringLength(32)]
        public string CoverImageMode { get; set; } = nameof(CompendiumCoverImageMode.Automatic);
        public int? CoverHeroProjectId { get; set; }
        public int? CoverHeroPhotoId { get; set; }
        public double CoverFocalX { get; set; } = .5d;
        public double CoverFocalY { get; set; } = .5d;

        public string? SelectedProjectIdsCsv { get; set; }
        public string? ProjectSelectionsJson { get; set; }
        public string? CustomSectionsJson { get; set; }
    }

    public sealed class CompendiumProjectSelectionPayload
    {
        public int ProjectId { get; set; }
        public int? PrimaryPhotoId { get; set; }
        public double FocalX { get; set; } = .5d;
        public double FocalY { get; set; } = .5d;
        public string? ImageSelectionMode { get; set; }
        public string? ReviewFingerprint { get; set; }
        public string? CustomSectionKey { get; set; }
        public string? CustomSectionName { get; set; }
        public string? NarrativeSourceOverride { get; set; }
    }
    public sealed class CompendiumSectionPayload
    {
        public string? SectionKey { get; set; }
        public string? Name { get; set; }
        public int SortOrder { get; set; }
    }

}
