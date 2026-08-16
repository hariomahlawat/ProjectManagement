using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Services.Publications;

namespace ProjectManagement.Pages.Projects.Publications.Compendium;

/// <summary>
/// Dedicated cover authoring surface. The editor operates on the same saved Compendium preset
/// used by the normal publication workspace; it introduces no parallel publication model.
/// </summary>
[Authorize]
public sealed class CoverModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICompendiumPresetService _presetService;
    private readonly ICompendiumReadService _readService;
    private readonly ILogger<CoverModel> _logger;

    public CoverModel(
        ICompendiumPresetService presetService,
        ICompendiumReadService readService,
        ILogger<CoverModel> logger)
    {
        _presetService = presetService ?? throw new ArgumentNullException(nameof(presetService));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public long PresetId { get; set; }

    public CompendiumPresetSummaryVm? Preset { get; private set; }
    public string BootstrapJson { get; private set; } = "{}";
    public IReadOnlyList<CompendiumPresetDiagnostic> Diagnostics { get; private set; }
        = Array.Empty<CompendiumPresetDiagnostic>();

    public bool CanManagePresets
        => User.IsInRole(RoleNames.HoD) || User.IsInRole(RoleNames.Comdt);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (PresetId <= 0)
        {
            return RedirectToPage("/Projects/Publications/Compendium/Index");
        }

        try
        {
            var loaded = await _presetService.LoadAsync(PresetId, cancellationToken);
            var candidates = await _readService.GetCandidateProjectsAsync(cancellationToken);
            var candidateById = candidates.ToDictionary(item => item.ProjectId);

            Preset = loaded.Preset;
            Diagnostics = loaded.Diagnostics;

            var projects = loaded.Configuration.Projects
                .Select((project, index) =>
                {
                    candidateById.TryGetValue(project.ProjectId, out var candidate);
                    return new
                    {
                        projectId = project.ProjectId,
                        projectName = candidate?.ProjectName ?? $"Project {project.ProjectId}",
                        lifecycle = candidate?.Lifecycle ?? "Unknown",
                        technicalCategory = candidate?.TechnicalCategory ?? "Not recorded",
                        publicationYear = candidate?.PublicationYear ?? 0,
                        photoCount = candidate?.PhotoCount ?? 0,
                        primaryPhotoId = project.PrimaryPhotoId,
                        imageSelectionMode = project.ImageSelectionMode.ToString(),
                        imageFitMode = project.ImageFitMode.ToString(),
                        sortOrder = index
                    };
                })
                .ToArray();

            var quartetUsablePhotos = await ResolveUsableCoverPhotosAsync(loaded.Configuration, cancellationToken, 4);

            BootstrapJson = JsonSerializer.Serialize(new
            {
                preset = new
                {
                    id = loaded.Preset.Id,
                    name = loaded.Preset.Name,
                    rowVersion = loaded.Preset.RowVersion,
                    projectCount = loaded.Preset.ProjectCount
                },
                canManage = CanManagePresets,
                publication = new
                {
                    title = loaded.Configuration.Title,
                    subtitle = loaded.Configuration.Subtitle,
                    edition = loaded.Configuration.Edition
                },
                coverDesign = ToClientCoverDesign(loaded.Configuration.CoverDesign, loaded.Configuration.Cover),
                coverPolicy = CompendiumCoverTemplatePolicy.BuildClientContract(),
                portfolioQuartetEligible = quartetUsablePhotos.Count >= 4,
                portfolioQuartetUsablePhotoCount = quartetUsablePhotos.Count,
                photoPreferences = loaded.Configuration.PhotoPreferences,
                projects,
                returnUrl = (Url.Page("/Projects/Publications/Compendium/Index", new { presetId = PresetId, resumeCover = 1 })
                             ?? "/Projects/Publications/Compendium") + "#compendium-settings",
                saveUrl = Url.Page("/Projects/Publications/Compendium/Cover", "Save", new { presetId = PresetId }),
                photosUrl = Url.Page("/Projects/Publications/Compendium/Cover", "ProjectPhotos", new { presetId = PresetId }),
                photoUrl = Url.Page("/Projects/Publications/Compendium/Index", "Photo")
            }, JsonOptions);

            return Page();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    public async Task<IActionResult> OnGetProjectPhotosAsync(
        long presetId,
        int projectId,
        CancellationToken cancellationToken)
    {
        if (presetId <= 0 || projectId <= 0)
        {
            return JsonError(StatusCodes.Status400BadRequest, "The project could not be resolved.");
        }

        try
        {
            var loaded = await _presetService.LoadAsync(presetId, cancellationToken);
            var project = loaded.Configuration.Projects.FirstOrDefault(item => item.ProjectId == projectId);
            if (project is null)
            {
                return JsonError(StatusCodes.Status404NotFound, "The project is not part of this Compendium.");
            }

            var selection = new CompendiumProjectSelection(
                project.ProjectId,
                project.PrimaryPhotoId,
                project.PrimaryFocalX,
                project.PrimaryFocalY,
                project.ImageSelectionMode)
            {
                CustomSectionKey = project.CustomSectionKey,
                CustomSectionName = project.CustomSectionName,
                NarrativeSourceOverride = project.NarrativeSourceOverride,
                ImageFitMode = project.ImageFitMode,
                DossierLayout = project.DossierLayout,
                BalancedTextFlowMode = project.BalancedTextFlowMode,
                DossierImageCount = project.DossierImageCount
            };

            var review = await _readService.GetReviewProjectAsync(
                selection,
                project.NarrativeSourceOverride ?? loaded.Configuration.NarrativeSource,
                cancellationToken);
            if (review is null)
            {
                return JsonError(StatusCodes.Status404NotFound, "Project photography is no longer available.");
            }

            var preferences = loaded.Configuration.PhotoPreferences
                .Where(item => item.ProjectId == projectId)
                .ToDictionary(item => item.PhotoId);

            return new JsonResult(new
            {
                projectId,
                projectName = review.ProjectName,
                photos = review.Photos.Select(photo =>
                {
                    preferences.TryGetValue(photo.PhotoId, out var preference);
                    return new
                    {
                        photoId = photo.PhotoId,
                        photo.Caption,
                        photo.Width,
                        photo.Height,
                        photo.IsCover,
                        photo.IsLowResolution,
                        photo.Version,
                        photo.IsUsable,
                        quality = photo.Quality.ToString().ToLowerInvariant(),
                        preferredForPublication = preference?.PreferredForPublication ?? false,
                        suitableForCoverHero = preference?.SuitableForCoverHero ?? false,
                        thumbnailUrl = Url.Page(
                            "/Projects/Publications/Compendium/Index",
                            "Photo",
                            new { projectId, photoId = photo.PhotoId, mode = "thumb", v = photo.Version }),
                        previewUrl = Url.Page(
                            "/Projects/Publications/Compendium/Index",
                            "Photo",
                            new { projectId, photoId = photo.PhotoId, mode = "source", v = photo.Version })
                    };
                })
            });
        }
        catch (KeyNotFoundException)
        {
            return JsonError(StatusCodes.Status404NotFound, "The saved Compendium no longer exists.");
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(
        long presetId,
        string rowVersion,
        string coverJson,
        string? photoPreferencesJson,
        CancellationToken cancellationToken)
    {
        if (!CanManagePresets)
        {
            return JsonError(StatusCodes.Status403Forbidden,
                "Only HoD or Comdt may save shared Compendium cover changes.");
        }

        if (presetId <= 0 || string.IsNullOrWhiteSpace(rowVersion))
        {
            return JsonError(StatusCodes.Status400BadRequest,
                "The saved Compendium could not be resolved. Reload the Cover Editor.");
        }

        try
        {
            var loaded = await _presetService.LoadAsync(presetId, cancellationToken);
            var design = ParseCoverDesign(coverJson, loaded.Configuration.Cover);
            var preferences = ParsePreferences(photoPreferencesJson);
            if (design.FrontTemplate == CompendiumFrontCoverTemplate.PortfolioQuartet)
            {
                var usable = await ResolveUsableCoverPhotosAsync(loaded.Configuration, cancellationToken, int.MaxValue);
                ValidatePortfolioQuartet(design, usable);
            }
            var legacyCover = DeriveLegacyCover(design);

            var configuration = loaded.Configuration with
            {
                Cover = legacyCover,
                CoverDesign = design,
                PhotoPreferences = preferences
            };

            var result = await _presetService.UpdateAsync(
                presetId,
                ActorUserId(),
                rowVersion,
                configuration,
                cancellationToken);

            return new JsonResult(new
            {
                message = "Cover design saved.",
                preset = result.Preset,
                coverDesign = ToClientCoverDesign(design, legacyCover),
                photoPreferences = preferences
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
            _logger.LogWarning(exception, "Compendium cover save failed. PresetId={PresetId}", presetId);
            return JsonError(StatusCodes.Status400BadRequest, exception.Message);
        }
    }

    private static object ToClientCoverDesign(
        CompendiumCoverDesignConfiguration design,
        CompendiumCoverConfiguration legacyCover)
    {
        var images = design.Images.ToList();
        if (!images.Any(item => item.Surface == CompendiumCoverSurface.Front
                                && string.Equals(item.SlotKey, "Hero", StringComparison.OrdinalIgnoreCase)))
        {
            images.Insert(0, new CompendiumPresetCoverImageConfiguration(
                CompendiumCoverSurface.Front,
                "Hero",
                legacyCover.ImageMode,
                legacyCover.HeroProjectId,
                legacyCover.HeroPhotoId,
                legacyCover.FocalX,
                legacyCover.FocalY,
                CompendiumImageFitMode.Fill,
                0));
        }

        return new
        {
            frontTemplate = design.FrontTemplate.ToString(),
            backTemplate = design.BackTemplate.ToString(),
            design.FrontTitle,
            design.FrontSubtitle,
            design.FrontEdition,
            design.FrontEyebrow,
            design.BackTitle,
            design.BackSubtitle,
            design.BackEdition,
            design.BackEyebrow,
            design.ShowFrontTitle,
            design.ShowFrontSubtitle,
            design.ShowFrontEdition,
            design.ShowFrontLeftLogo,
            design.ShowFrontRightLogo,
            frontLogoPlacement = design.FrontLogoPlacement.ToString(),
            design.ShowBackTitle,
            design.ShowBackSubtitle,
            design.ShowBackEdition,
            design.ShowBackLeftLogo,
            design.ShowBackRightLogo,
            backLogoPlacement = design.BackLogoPlacement.ToString(),
            images = images.OrderBy(item => item.SortOrder).Select(item => new
            {
                surface = item.Surface.ToString(),
                item.SlotKey,
                imageMode = item.ImageMode.ToString(),
                item.ProjectId,
                item.PhotoId,
                item.FocalX,
                item.FocalY,
                fitMode = item.FitMode.ToString(),
                item.SortOrder
            })
        };
    }

    private static CompendiumCoverDesignConfiguration ParseCoverDesign(
        string json,
        CompendiumCoverConfiguration legacyCover)
    {
        CoverSavePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CoverSavePayload>(json ?? string.Empty, JsonOptions);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            throw new InvalidOperationException("The cover design payload is invalid.");
        }

        var images = (payload.Images ?? Array.Empty<CoverImageSavePayload>())
            .Where(item => !string.IsNullOrWhiteSpace(item.SlotKey))
            .Select((item, index) => new CompendiumPresetCoverImageConfiguration(
                ParseEnum(item.Surface, CompendiumCoverSurface.Front),
                Clean(item.SlotKey, 32) ?? $"Slot{index + 1}",
                ParseEnum(item.ImageMode, CompendiumCoverImageMode.Automatic),
                item.ProjectId is > 0 ? item.ProjectId : null,
                item.PhotoId is > 0 ? item.PhotoId : null,
                ClampFocal(item.FocalX),
                ClampFocal(item.FocalY),
                ParseEnum(item.FitMode, CompendiumImageFitMode.Fill),
                index))
            .GroupBy(item => $"{item.Surface}:{item.SlotKey}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(12)
            .ToArray();

        if (images.Length == 0)
        {
            images = new[]
            {
                new CompendiumPresetCoverImageConfiguration(
                    CompendiumCoverSurface.Front,
                    "Hero",
                    legacyCover.ImageMode,
                    legacyCover.HeroProjectId,
                    legacyCover.HeroPhotoId,
                    legacyCover.FocalX,
                    legacyCover.FocalY,
                    CompendiumImageFitMode.Fill,
                    0)
            };
        }

        var frontTemplate = ParseEnum(payload.FrontTemplate, CompendiumFrontCoverTemplate.InstitutionalHero);
        if (frontTemplate == CompendiumFrontCoverTemplate.PortfolioQuartet)
        {
            images = images.Select(item => item.Surface == CompendiumCoverSurface.Front
                ? item with { FitMode = CompendiumImageFitMode.Fill }
                : item).ToArray();
        }

        return new CompendiumCoverDesignConfiguration
        {
            FrontTemplate = frontTemplate,
            BackTemplate = ParseEnum(payload.BackTemplate, CompendiumBackCoverTemplate.MinimalInstitutional),
            FrontTitle = Clean(payload.FrontTitle, 120),
            FrontSubtitle = Clean(payload.FrontSubtitle, 160),
            FrontEdition = Clean(payload.FrontEdition, 80),
            FrontEyebrow = Clean(payload.FrontEyebrow, 80),
            BackTitle = Clean(payload.BackTitle, 120),
            BackSubtitle = Clean(payload.BackSubtitle, 160),
            BackEdition = Clean(payload.BackEdition, 80),
            BackEyebrow = Clean(payload.BackEyebrow, 80),
            ShowFrontTitle = payload.ShowFrontTitle,
            ShowFrontSubtitle = payload.ShowFrontSubtitle,
            ShowFrontEdition = payload.ShowFrontEdition,
            ShowFrontLeftLogo = payload.ShowFrontLeftLogo,
            ShowFrontRightLogo = payload.ShowFrontRightLogo,
            FrontLogoPlacement = ParseEnum(payload.FrontLogoPlacement, CompendiumCoverLogoPlacement.TopCorners),
            ShowBackTitle = payload.ShowBackTitle,
            ShowBackSubtitle = payload.ShowBackSubtitle,
            ShowBackEdition = payload.ShowBackEdition,
            ShowBackLeftLogo = payload.ShowBackLeftLogo,
            ShowBackRightLogo = payload.ShowBackRightLogo,
            BackLogoPlacement = ParseEnum(payload.BackLogoPlacement, CompendiumCoverLogoPlacement.TopCorners),
            Images = images
        };
    }

    private static IReadOnlyList<CompendiumPresetPhotoPreferenceConfiguration> ParsePreferences(string? json)
    {
        PreferenceSavePayload[] payloads;
        try
        {
            payloads = string.IsNullOrWhiteSpace(json)
                ? Array.Empty<PreferenceSavePayload>()
                : JsonSerializer.Deserialize<PreferenceSavePayload[]>(json, JsonOptions) ?? Array.Empty<PreferenceSavePayload>();
        }
        catch (JsonException)
        {
            payloads = Array.Empty<PreferenceSavePayload>();
        }

        return payloads
            .Where(item => item.ProjectId > 0 && item.PhotoId > 0)
            .GroupBy(item => (item.ProjectId, item.PhotoId))
            .Select(group => group.Last())
            .Where(item => item.PreferredForPublication || item.SuitableForCoverHero)
            .Select(item => new CompendiumPresetPhotoPreferenceConfiguration(
                item.ProjectId,
                item.PhotoId,
                item.PreferredForPublication,
                item.SuitableForCoverHero))
            .ToArray();
    }

    private static CompendiumCoverConfiguration DeriveLegacyCover(CompendiumCoverDesignConfiguration design)
    {
        if (design.FrontTemplate == CompendiumFrontCoverTemplate.Minimal)
        {
            return new CompendiumCoverConfiguration(CompendiumCoverImageMode.None);
        }

        var hero = design.Images.FirstOrDefault(item => item.Surface == CompendiumCoverSurface.Front
                                                        && string.Equals(item.SlotKey, "Hero", StringComparison.OrdinalIgnoreCase));
        return hero is null
            ? new CompendiumCoverConfiguration(CompendiumCoverImageMode.Automatic)
            : new CompendiumCoverConfiguration(
                hero.ImageMode,
                hero.ProjectId,
                hero.PhotoId,
                hero.FocalX,
                hero.FocalY);
    }

    private async Task<HashSet<(int ProjectId, int PhotoId)>> ResolveUsableCoverPhotosAsync(
        CompendiumPresetConfiguration configuration,
        CancellationToken cancellationToken,
        int maximumCount)
    {
        var result = new HashSet<(int ProjectId, int PhotoId)>();
        maximumCount = Math.Max(1, maximumCount);
        foreach (var project in configuration.Projects)
        {
            if (result.Count >= maximumCount) break;
            var selection = new CompendiumProjectSelection(
                project.ProjectId, project.PrimaryPhotoId, project.PrimaryFocalX, project.PrimaryFocalY, project.ImageSelectionMode)
            {
                NarrativeSourceOverride = project.NarrativeSourceOverride,
                ImageFitMode = project.ImageFitMode,
                DossierLayout = project.DossierLayout,
                BalancedTextFlowMode = project.BalancedTextFlowMode,
                DossierImageCount = project.DossierImageCount
            };
            var review = await _readService.GetReviewProjectAsync(
                selection, project.NarrativeSourceOverride ?? configuration.NarrativeSource, cancellationToken);
            if (review is null) continue;
            foreach (var photo in review.Photos.Where(photo => photo.IsUsable))
            {
                result.Add((project.ProjectId, photo.PhotoId));
                if (result.Count >= maximumCount) break;
            }
        }
        return result;
    }

    private static void ValidatePortfolioQuartet(
        CompendiumCoverDesignConfiguration design,
        IReadOnlySet<(int ProjectId, int PhotoId)> usable)
    {
        var required = CompendiumCoverTemplatePolicy.RequiredSlotKeys(
            CompendiumCoverSurface.Front, design.FrontTemplate, design.BackTemplate);
        var slots = design.Images.Where(item => item.Surface == CompendiumCoverSurface.Front)
            .ToDictionary(item => item.SlotKey, StringComparer.OrdinalIgnoreCase);
        var explicitPhotos = new HashSet<(int ProjectId, int PhotoId)>();
        foreach (var key in required)
        {
            if (!slots.TryGetValue(key, out var slot) || slot.ImageMode == CompendiumCoverImageMode.None)
                throw new InvalidOperationException("Portfolio Quartet requires all four image slots to remain active.");
            if (slot.ImageMode != CompendiumCoverImageMode.Explicit) continue;
            if (slot.ProjectId is not int projectId || slot.PhotoId is not int photoId || !usable.Contains((projectId, photoId)))
                throw new InvalidOperationException($"Portfolio Quartet image '{key}' is no longer usable. Choose another photograph.");
            if (!explicitPhotos.Add((projectId, photoId)))
                throw new InvalidOperationException("Portfolio Quartet cannot repeat the same photograph in more than one slot.");
        }
        if (usable.Count < 4)
            throw new InvalidOperationException("Portfolio Quartet requires at least four distinct usable photographs before it can be saved.");
    }

    private string ActorUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new UnauthorizedAccessException("The current user account could not be resolved.");

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private static string? Clean(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= maximumLength ? clean : clean[..maximumLength].TrimEnd();
    }

    private static JsonResult JsonError(int statusCode, string message, string? code = null)
        => new(new { message, code }) { StatusCode = statusCode };

    public sealed class CoverSavePayload
    {
        public string? FrontTemplate { get; set; }
        public string? BackTemplate { get; set; }
        public string? FrontTitle { get; set; }
        public string? FrontSubtitle { get; set; }
        public string? FrontEdition { get; set; }
        public string? FrontEyebrow { get; set; }
        public string? BackTitle { get; set; }
        public string? BackSubtitle { get; set; }
        public string? BackEdition { get; set; }
        public string? BackEyebrow { get; set; }
        public bool ShowFrontTitle { get; set; } = true;
        public bool ShowFrontSubtitle { get; set; } = true;
        public bool ShowFrontEdition { get; set; } = true;
        public bool ShowFrontLeftLogo { get; set; } = true;
        public bool ShowFrontRightLogo { get; set; } = true;
        public string? FrontLogoPlacement { get; set; }
        public bool ShowBackTitle { get; set; } = true;
        public bool ShowBackSubtitle { get; set; } = true;
        public bool ShowBackEdition { get; set; } = true;
        public bool ShowBackLeftLogo { get; set; } = true;
        public bool ShowBackRightLogo { get; set; } = true;
        public string? BackLogoPlacement { get; set; }
        public CoverImageSavePayload[] Images { get; set; } = Array.Empty<CoverImageSavePayload>();
    }

    public sealed class CoverImageSavePayload
    {
        public string? Surface { get; set; }
        public string? SlotKey { get; set; }
        public string? ImageMode { get; set; }
        public int? ProjectId { get; set; }
        public int? PhotoId { get; set; }
        public double FocalX { get; set; } = .5d;
        public double FocalY { get; set; } = .5d;
        public string? FitMode { get; set; }
    }

    public sealed class PreferenceSavePayload
    {
        public int ProjectId { get; set; }
        public int PhotoId { get; set; }
        public bool PreferredForPublication { get; set; }
        public bool SuitableForCoverHero { get; set; }
    }
}
