using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Services.Publications;

namespace ProjectManagement.Pages.Projects.Publications.Compendium;

[Authorize]
public sealed class IndexModel : PageModel
{
    private const int MaximumSelectedProjects = 500;

    private readonly ICompendiumReadService _readService;
    private readonly ICompendiumExportService _exportService;
    private readonly ICompendiumPresetService _presetService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ICompendiumReadService readService,
        ICompendiumExportService exportService,
        ICompendiumPresetService presetService,
        ILogger<IndexModel> logger)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _presetService = presetService ?? throw new ArgumentNullException(nameof(presetService));
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

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApplyDefaultSettings();
        await LoadWorkspaceAsync(loadPreset: PresetId is > 0, cancellationToken);
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
            canGenerate = data.Preflight.CanGenerate,
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
        var projectIds = ParseSelectedIds();

        if (!ModelState.IsValid || projectIds.Count == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                "Select at least one project before generating the Compendium.");
            await LoadWorkspaceAsync(loadPreset: false, cancellationToken);
            return Page();
        }

        try
        {
            var result = await _exportService.GenerateAsync(
                new CompendiumExportRequest(
                    Input.HandlingMarking,
                    projectIds,
                    Input.Title,
                    Input.Subtitle,
                    Input.Edition),
                cancellationToken);

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
            ModelState.AddModelError(
                string.Empty,
                preview
                    ? "The Compendium preview could not be generated. Review publication readiness and try again."
                    : "The Compendium could not be generated. Review publication readiness and try again.");
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
                Input.SelectedProjectIdsCsv = string.Join(',', loaded.Configuration.ProjectIds);
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

        var projectIds = ParseSelectedIds();
        if (projectIds.Count == 0)
        {
            Preflight = CompendiumPreflightDto.Empty with
            {
                CandidateProjectCount = Projects.Count,
                SelectedProjectCount = 0,
                BlockerCount = 1,
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
    }

    private void NormalizeInput()
    {
        ApplyDefaultSettings();
        Input.Title = Clean(Input.Title, 120) ?? "SDD Simulators Compendium";
        Input.Subtitle = Clean(Input.Subtitle, 160) ?? "Detailed Project Reference";
        Input.Edition = Clean(Input.Edition, 80) ?? $"Capability Edition · {DateTime.Today.Year}";
        Input.HandlingMarking = Clean(Input.HandlingMarking, 80);
        Input.SelectedProjectIdsCsv = string.Join(',', ParseSelectedIds());
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

    private CompendiumPublicationRequest ToPublicationRequest()
        => new(ParseSelectedIds(), Input.Title, Input.Subtitle, Input.Edition);

    private CompendiumPresetConfiguration ToPresetConfiguration()
        => new(
            Input.Title,
            Input.Subtitle,
            Input.Edition,
            Input.HandlingMarking,
            ParseSelectedIds());

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

        public string? SelectedProjectIdsCsv { get; set; }
    }
}
