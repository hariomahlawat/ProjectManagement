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

[Authorize]
public sealed class StructureModel : PageModel
{
    private const int MaximumProjects = 500;
    private const int MaximumSections = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICompendiumPresetService _presetService;
    private readonly ICompendiumReadService _readService;
    private readonly ILogger<StructureModel> _logger;

    public StructureModel(
        ICompendiumPresetService presetService,
        ICompendiumReadService readService,
        ILogger<StructureModel> logger)
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

            Preset = loaded.Preset;
            Diagnostics = loaded.Diagnostics;

            var existingById = loaded.Configuration.Projects
                .Select((project, index) => new { project, index })
                .ToDictionary(item => item.project.ProjectId);
            // Bootstrap the complete candidate lookup so browser handoff can resolve projects
            // that were selected on the main workspace immediately before opening the editor.
            // Only selected publication projects are rendered by the Structure Editor.
            var projects = candidates
                .Select(candidate =>
                {
                    existingById.TryGetValue(candidate.ProjectId, out var existing);
                    var item = existing?.project;
                    return new
                    {
                        projectId = candidate.ProjectId,
                        projectName = candidate.ProjectName,
                        lifecycle = candidate.Lifecycle,
                        projectCategory = candidate.ProjectCategory ?? "Uncategorised",
                        technicalCategory = candidate.TechnicalCategory ?? "Not recorded",
                        technicalCategorySortOrder = candidate.TechnicalCategorySortOrder,
                        publicationYear = candidate.PublicationYear,
                        sponsoringLineDirectorate = candidate.SponsoringLineDirectorateDisplay,
                        selected = item is not null,
                        customSectionKey = item?.CustomSectionKey,
                        customSectionName = item?.CustomSectionName,
                        narrativeSourceOverride = item?.NarrativeSourceOverride?.ToString(),
                        narrativeAlignmentOverride = item?.NarrativeAlignmentOverride?.ToString(),
                        primaryPhotoId = item?.PrimaryPhotoId,
                        primaryFocalX = item?.PrimaryFocalX ?? .5d,
                        primaryFocalY = item?.PrimaryFocalY ?? .5d,
                        imageSelectionMode = item?.ImageSelectionMode.ToString() ?? "Automatic",
                        imageFitMode = item?.ImageFitMode.ToString() ?? "Fill",
                        dossierLayout = item?.DossierLayout.ToString() ?? "Automatic",
                        balancedTextFlowMode = item?.BalancedTextFlowMode.ToString() ?? "FlowBelowImage",
                        dossierImageCount = item?.DossierImageCount ?? 1,
                        supportingPhoto1Id = item?.SupportingPhoto1Id, supportingPhoto1FocalX = item?.SupportingPhoto1FocalX ?? .5d, supportingPhoto1FocalY = item?.SupportingPhoto1FocalY ?? .5d, supportingPhoto1FitMode = item?.SupportingPhoto1FitMode.ToString() ?? "Fill",
                        supportingPhoto2Id = item?.SupportingPhoto2Id, supportingPhoto2FocalX = item?.SupportingPhoto2FocalX ?? .5d, supportingPhoto2FocalY = item?.SupportingPhoto2FocalY ?? .5d, supportingPhoto2FitMode = item?.SupportingPhoto2FitMode.ToString() ?? "Fill",
                        sortOrder = existing?.index ?? int.MaxValue,
                        available = true
                    };
                })
                .OrderBy(project => project.sortOrder)
                .ThenBy(project => project.projectName)
                .ToArray();

            var sections = loaded.Configuration.Sections
                .OrderBy(section => section.SortOrder)
                .Take(MaximumSections)
                .Select((section, index) => new
                {
                    sectionKey = section.SectionKey,
                    name = section.Name,
                    sortOrder = index
                })
                .ToArray();

            BootstrapJson = JsonSerializer.Serialize(new
            {
                preset = new
                {
                    id = loaded.Preset.Id,
                    name = loaded.Preset.Name,
                    rowVersion = loaded.Preset.RowVersion,
                    projectCount = loaded.Preset.ProjectCount,
                    updatedAtUtc = loaded.Preset.UpdatedAtUtc,
                    updatedByDisplay = loaded.Preset.UpdatedByDisplay
                },
                canManage = CanManagePresets,
                narrativeAlignment = loaded.Configuration.DefaultNarrativeAlignment.ToString(),
                groupingMode = loaded.Configuration.GroupingMode.ToString(),
                sortMode = loaded.Configuration.SortMode.ToString(),
                sections,
                projects,
                returnUrl = (Url.Page("/Projects/Publications/Compendium/Index", new { presetId = PresetId, resumeStructure = 1 })
                             ?? "/Projects/Publications/Compendium") + "#compendium-select",
                saveUrl = Url.Page("/Projects/Publications/Compendium/Structure", "Save", new { presetId = PresetId })
            }, JsonOptions);

            return Page();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(
        long presetId,
        string rowVersion,
        string structureJson,
        CancellationToken cancellationToken)
    {
        if (!CanManagePresets)
        {
            return JsonError(StatusCodes.Status403Forbidden,
                "Only HoD or Comdt may save shared Compendium structure changes.");
        }

        if (presetId <= 0 || string.IsNullOrWhiteSpace(rowVersion))
        {
            return JsonError(StatusCodes.Status400BadRequest,
                "The saved Compendium could not be resolved. Reload the Structure Editor.");
        }

        StructureSavePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StructureSavePayload>(structureJson ?? string.Empty, JsonOptions);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            return JsonError(StatusCodes.Status400BadRequest,
                "The publication structure payload is invalid.");
        }

        try
        {
            var loaded = await _presetService.LoadAsync(presetId, cancellationToken);
            var existingById = loaded.Configuration.Projects.ToDictionary(project => project.ProjectId);

            var sections = NormalizeSections(payload.Sections);
            var sectionByKey = sections.ToDictionary(section => section.SectionKey, StringComparer.OrdinalIgnoreCase);

            var seenProjects = new HashSet<int>();
            var ordered = new List<CompendiumPresetProjectConfiguration>();
            foreach (var item in payload.Projects ?? Array.Empty<StructureProjectPayload>())
            {
                if (item.ProjectId <= 0 || !seenProjects.Add(item.ProjectId))
                {
                    continue;
                }

                existingById.TryGetValue(item.ProjectId, out var existing);
                var sectionKey = CleanSectionKey(item.CustomSectionKey);
                CompendiumPresetSectionConfiguration? section = null;
                if (!string.IsNullOrWhiteSpace(sectionKey))
                {
                    sectionByKey.TryGetValue(sectionKey, out section);
                }

                var requestedMode = string.IsNullOrWhiteSpace(item.ImageSelectionMode)
                    ? existing?.ImageSelectionMode ?? CompendiumImageSelectionMode.Automatic
                    : string.Equals(item.ImageSelectionMode, "Explicit", StringComparison.OrdinalIgnoreCase)
                      && item.PrimaryPhotoId is > 0
                        ? CompendiumImageSelectionMode.Explicit
                        : CompendiumImageSelectionMode.Automatic;
                var mode = requestedMode;
                var baseConfiguration = existing ?? new CompendiumPresetProjectConfiguration(
                    item.ProjectId,
                    mode == CompendiumImageSelectionMode.Explicit ? item.PrimaryPhotoId : null,
                    ClampFocal(item.FocalX),
                    ClampFocal(item.FocalY),
                    mode)
                {
                    NarrativeSourceOverride = ParseNarrativeOverride(item.NarrativeSourceOverride)
                };

                ordered.Add(baseConfiguration with
                {
                    PrimaryPhotoId = mode == CompendiumImageSelectionMode.Explicit ? item.PrimaryPhotoId : baseConfiguration.PrimaryPhotoId,
                    PrimaryFocalX = ClampFocal(item.FocalX ?? baseConfiguration.PrimaryFocalX),
                    PrimaryFocalY = ClampFocal(item.FocalY ?? baseConfiguration.PrimaryFocalY),
                    ImageSelectionMode = mode,
                    ImageFitMode = ParseImageFitMode(item.ImageFitMode, baseConfiguration.ImageFitMode),
                    DossierLayout = ParseDossierLayout(item.DossierLayout, baseConfiguration.DossierLayout),
                    BalancedTextFlowMode = ParseBalancedTextFlowMode(item.BalancedTextFlowMode, baseConfiguration.BalancedTextFlowMode),
                    NarrativeAlignmentOverride = ParseNarrativeAlignmentOverride(item.NarrativeAlignmentOverride) ?? baseConfiguration.NarrativeAlignmentOverride,
                    DossierImageCount = Math.Clamp(item.DossierImageCount ?? baseConfiguration.DossierImageCount, 1, 3),
                    SupportingPhoto1Id = item.SupportingPhoto1Id ?? baseConfiguration.SupportingPhoto1Id,
                    SupportingPhoto1FocalX = ClampFocal(item.SupportingPhoto1FocalX ?? baseConfiguration.SupportingPhoto1FocalX),
                    SupportingPhoto1FocalY = ClampFocal(item.SupportingPhoto1FocalY ?? baseConfiguration.SupportingPhoto1FocalY),
                    SupportingPhoto1FitMode = ParseImageFitMode(item.SupportingPhoto1FitMode, baseConfiguration.SupportingPhoto1FitMode),
                    SupportingPhoto2Id = item.SupportingPhoto2Id ?? baseConfiguration.SupportingPhoto2Id,
                    SupportingPhoto2FocalX = ClampFocal(item.SupportingPhoto2FocalX ?? baseConfiguration.SupportingPhoto2FocalX),
                    SupportingPhoto2FocalY = ClampFocal(item.SupportingPhoto2FocalY ?? baseConfiguration.SupportingPhoto2FocalY),
                    SupportingPhoto2FitMode = ParseImageFitMode(item.SupportingPhoto2FitMode, baseConfiguration.SupportingPhoto2FitMode),
                    NarrativeSourceOverride = ParseNarrativeOverride(item.NarrativeSourceOverride) ?? baseConfiguration.NarrativeSourceOverride,
                    CustomSectionKey = section?.SectionKey,
                    CustomSectionName = section?.Name
                });

                if (ordered.Count >= MaximumProjects)
                {
                    break;
                }
            }

            if (ordered.Count == 0)
            {
                return JsonError(StatusCodes.Status400BadRequest,
                    "Keep at least one project in the Compendium before saving its structure.");
            }

            var cover = loaded.Configuration.Cover;
            if (cover.ImageMode == CompendiumCoverImageMode.Explicit
                && cover.HeroProjectId is int heroProjectId
                && ordered.All(project => project.ProjectId != heroProjectId))
            {
                cover = new CompendiumCoverConfiguration();
            }

            var merged = loaded.Configuration with
            {
                Projects = ordered,
                Sections = sections,
                Cover = cover
            };

            var result = await _presetService.UpdateAsync(
                presetId,
                ActorUserId(),
                rowVersion,
                merged,
                cancellationToken);

            return new JsonResult(new
            {
                message = "Publication structure saved.",
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
            _logger.LogWarning(exception, "Compendium Structure Editor save failed for preset {PresetId}.", presetId);
            return JsonError(StatusCodes.Status400BadRequest, exception.Message);
        }
    }

    private string ActorUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new UnauthorizedAccessException("The current user account could not be resolved.");

    private static IReadOnlyList<CompendiumPresetSectionConfiguration> NormalizeSections(
        IReadOnlyList<StructureSectionPayload>? sections)
    {
        var result = new List<CompendiumPresetSectionConfiguration>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in (sections ?? Array.Empty<StructureSectionPayload>())
                     .OrderBy(section => section.SortOrder))
        {
            var key = CleanSectionKey(item.SectionKey);
            var name = CleanSectionName(item.Name);
            if (key is null || name is null || !keys.Add(key) || !names.Add(name))
            {
                continue;
            }

            result.Add(new CompendiumPresetSectionConfiguration(key, name, result.Count));
            if (result.Count >= MaximumSections)
            {
                break;
            }
        }

        return result;
    }

    private static string? CleanSectionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = new string(value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(40)
            .ToArray());
        return clean.Length == 0 ? null : clean;
    }

    private static string? CleanSectionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = string.Join(' ', value.Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > 120 ? clean[..120] : clean;
    }

    private static double ClampFocal(double? value)
        => Math.Clamp(value ?? .5d, 0d, 1d);

    private static CompendiumImageFitMode ParseImageFitMode(string? value, CompendiumImageFitMode fallback)
        => Enum.TryParse<CompendiumImageFitMode>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static CompendiumDossierLayout ParseDossierLayout(string? value, CompendiumDossierLayout fallback)
        => Enum.TryParse<CompendiumDossierLayout>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    private static CompendiumBalancedTextFlowMode ParseBalancedTextFlowMode(string? value, CompendiumBalancedTextFlowMode fallback)
        => Enum.TryParse<CompendiumBalancedTextFlowMode>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    private static CompendiumNarrativeSource? ParseNarrativeOverride(string? value)
        => Enum.TryParse<CompendiumNarrativeSource>(value, true, out var parsed)
           && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static CompendiumNarrativeAlignment? ParseNarrativeAlignmentOverride(string? value)
        => Enum.TryParse<CompendiumNarrativeAlignment>(value, true, out var parsed)
           && Enum.IsDefined(parsed)
            ? parsed
            : null;


    private JsonResult JsonError(int statusCode, string message, string? code = null)
    {
        Response.StatusCode = statusCode;
        return new JsonResult(new { message, code });
    }

    public sealed class StructureSavePayload
    {
        public IReadOnlyList<StructureSectionPayload>? Sections { get; set; }
        public IReadOnlyList<StructureProjectPayload>? Projects { get; set; }
    }

    public sealed class StructureSectionPayload
    {
        public string? SectionKey { get; set; }
        public string? Name { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class StructureProjectPayload
    {
        public int ProjectId { get; set; }
        public string? CustomSectionKey { get; set; }
        public int? PrimaryPhotoId { get; set; }
        public double? FocalX { get; set; }
        public double? FocalY { get; set; }
        public string? ImageSelectionMode { get; set; }
        public string? ImageFitMode { get; set; }
        public string? DossierLayout { get; set; }
        public string? BalancedTextFlowMode { get; set; }
        public int? DossierImageCount { get; set; }
        public int? SupportingPhoto1Id { get; set; } public double? SupportingPhoto1FocalX { get; set; } public double? SupportingPhoto1FocalY { get; set; } public string? SupportingPhoto1FitMode { get; set; }
        public int? SupportingPhoto2Id { get; set; } public double? SupportingPhoto2FocalX { get; set; } public double? SupportingPhoto2FocalY { get; set; } public string? SupportingPhoto2FitMode { get; set; }
        public string? NarrativeSourceOverride { get; set; }
        public string? NarrativeAlignmentOverride { get; set; }
    }
}
