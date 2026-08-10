using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services;
using ProjectManagement.Services.Publications;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Pages.Projects.Publications.Brochure;

[Authorize]
public sealed class IndexModel : PageModel
{
    private const int MaximumSelectedProjects = 100;

    private readonly IBrochurePublicationService _publicationService;
    private readonly IBrochurePdfReportBuilder _pdfBuilder;
    private readonly IPublicationFontService _fontService;
    private readonly IClock _clock;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IBrochurePublicationService publicationService,
        IBrochurePdfReportBuilder pdfBuilder,
        IPublicationFontService fontService,
        IClock clock,
        ILogger<IndexModel> logger)
    {
        _publicationService = publicationService ?? throw new ArgumentNullException(nameof(publicationService));
        _pdfBuilder = pdfBuilder ?? throw new ArgumentNullException(nameof(pdfBuilder));
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public GenerateBrochureInput Input { get; set; } = new();

    public IReadOnlyList<BrochureProjectListItemVm> Projects { get; private set; }
        = Array.Empty<BrochureProjectListItemVm>();

    public IReadOnlyList<string> ProjectCategories { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> TechnicalCategories { get; private set; } = Array.Empty<string>();
    public PublicationFontStatus FontStatus { get; private set; }
        = new(
            PublicationFontService.FallbackFamilyName,
            PublicationFontService.FallbackFamilyName,
            false,
            false,
            Array.Empty<string>(),
            "QuestPDF bundled fallback");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApplyDefaults();
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostPreflightAsync(CancellationToken cancellationToken)
    {
        ApplyDefaults();
        NormalizeInput();

        if (!Enum.IsDefined(Input.NarrativeSource))
        {
            return new JsonResult(new
            {
                selectedProjectCount = Input.Selections.Count,
                blockerCount = 1,
                warningCount = 0,
                informationCount = 0,
                canGenerate = false,
                issues = new[]
                {
                    new
                    {
                        severity = "blocker",
                        code = "invalidNarrativeSource",
                        projectId = (int?)null,
                        projectName = (string?)null,
                        message = "Select a valid project narrative source."
                    }
                }
            });
        }

        var preflight = await _publicationService.PreflightAsync(
            ToSelections(),
            Input.NarrativeSource,
            Input.AllowTextOnlyProjects,
            cancellationToken);
        return new JsonResult(ToClientPreflight(preflight));
    }

    public Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
        => GenerateInternalAsync(preview: true, cancellationToken);

    public Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
        => GenerateInternalAsync(preview: false, cancellationToken);

    private async Task<IActionResult> GenerateInternalAsync(
        bool preview,
        CancellationToken cancellationToken)
    {
        ApplyDefaults();
        NormalizeInput();
        ValidateGenerationInput();

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var generatedAt = _clock.UtcNow;
            var options = new BrochureBuildOptions(
                Input.Title!,
                Input.Subtitle!,
                Input.Edition!,
                Input.Strapline!,
                Input.CoverStyle,
                Input.NarrativeSource,
                NullIfWhiteSpace(Input.IntroductionTitle),
                NullIfWhiteSpace(Input.IntroductionText),
                NullIfWhiteSpace(Input.HandlingMarking),
                "Simulator Development Division",
                Input.AllowTextOnlyProjects,
                generatedAt);

            var publication = await _publicationService.BuildAsync(
                ToSelections(),
                options,
                cancellationToken);
            var bytes = _pdfBuilder.Build(publication);
            var fileName = $"{SanitizeFileName(Input.Title, "SDD_Capability_Brochure")}_{generatedAt:yyyyMMdd}.pdf";

            if (preview)
            {
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
                return File(bytes, "application/pdf");
            }

            return File(bytes, "application/pdf", fileName);
        }
        catch (BrochurePublicationValidationException exception)
        {
            foreach (var issue in exception.Preflight.Issues
                         .Where(issue => issue.Severity == PublicationIssueSeverity.Blocker)
                         .Take(8))
            {
                ModelState.AddModelError(string.Empty, issue.Message);
            }

            await LoadAsync(cancellationToken);
            return Page();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Capability brochure {Operation} failed. SelectedProjects={SelectedProjectCount}, Narrative={NarrativeSource}, Cover={CoverStyle}",
                preview ? "preview" : "generation",
                Input.Selections.Count,
                Input.NarrativeSource,
                Input.CoverStyle);
            ModelState.AddModelError(
                string.Empty,
                preview
                    ? "The brochure preview could not be generated. Review publication preflight and try again."
                    : "The brochure could not be generated. Review publication preflight and try again.");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Projects = await _publicationService.GetProjectOptionsAsync(cancellationToken);
        ProjectCategories = Projects
            .Select(project => project.ProjectCategory)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        TechnicalCategories = Projects
            .Select(project => project.TechnicalCategory)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        FontStatus = _fontService.CurrentStatus;
    }

    private void ApplyDefaults()
    {
        var year = _clock.UtcNow.Year;
        Input.Title = string.IsNullOrWhiteSpace(Input.Title) ? "SDD Capability Brochure" : Input.Title;
        Input.Subtitle = string.IsNullOrWhiteSpace(Input.Subtitle) ? "Simulator Development Division" : Input.Subtitle;
        Input.Edition = string.IsNullOrWhiteSpace(Input.Edition) ? $"Capability Edition · {year}" : Input.Edition;
        Input.Strapline = string.IsNullOrWhiteSpace(Input.Strapline)
            ? "Simulators of the Army, by the Army, for the Army"
            : Input.Strapline;
    }

    private void NormalizeInput()
    {
        Input.Title = Normalize(Input.Title, 120);
        Input.Subtitle = Normalize(Input.Subtitle, 160);
        Input.Edition = Normalize(Input.Edition, 80);
        Input.Strapline = Normalize(Input.Strapline, 180);
        Input.IntroductionTitle = NormalizeOptional(Input.IntroductionTitle, 120);
        Input.IntroductionText = NormalizeOptional(Input.IntroductionText, 3000, preserveLineBreaks: true);
        Input.HandlingMarking = NormalizeOptional(Input.HandlingMarking, 80)?.ToUpperInvariant();

        Input.Selections = Input.Selections
            .Where(selection => selection.ProjectId > 0)
            .GroupBy(selection => selection.ProjectId)
            .Select(group => group.First())
            .Take(MaximumSelectedProjects + 1)
            .Select(selection =>
            {
                selection.PrimaryPhotoId = NormalizePhotoId(selection.PrimaryPhotoId);
                selection.SecondaryPhotoId = NormalizePhotoId(selection.SecondaryPhotoId);
                selection.PrimaryFocalX = ClampFocal(selection.PrimaryFocalX);
                selection.PrimaryFocalY = ClampFocal(selection.PrimaryFocalY);
                selection.SecondaryFocalX = ClampFocal(selection.SecondaryFocalX);
                selection.SecondaryFocalY = ClampFocal(selection.SecondaryFocalY);
                if (!Enum.IsDefined(selection.ImageMode))
                {
                    selection.ImageMode = BrochureImageMode.Automatic;
                }
                return selection;
            })
            .ToList();
    }

    private void ValidateGenerationInput()
    {
        if (Input.Selections.Count == 0)
        {
            ModelState.AddModelError(nameof(Input.Selections), "Select at least one project for the brochure.");
        }
        else if (Input.Selections.Count > MaximumSelectedProjects)
        {
            ModelState.AddModelError(nameof(Input.Selections), $"A brochure can contain up to {MaximumSelectedProjects} selected projects.");
        }

        if (!Enum.IsDefined(Input.CoverStyle))
        {
            ModelState.AddModelError(nameof(Input.CoverStyle), "Select a valid cover style.");
        }
        if (!Enum.IsDefined(Input.NarrativeSource))
        {
            ModelState.AddModelError(nameof(Input.NarrativeSource), "Select a valid project narrative source.");
        }
    }

    private IReadOnlyList<BrochureProjectSelection> ToSelections()
        => Input.Selections
            .Select(selection => new BrochureProjectSelection(
                selection.ProjectId,
                selection.PrimaryPhotoId,
                selection.SecondaryPhotoId,
                selection.PrimaryFocalX,
                selection.PrimaryFocalY,
                selection.SecondaryFocalX,
                selection.SecondaryFocalY,
                selection.ImageMode))
            .ToArray();

    private static object ToClientPreflight(BrochurePreflight preflight)
        => new
        {
            selectedProjectCount = preflight.SelectedProjectCount,
            blockerCount = preflight.BlockerCount,
            warningCount = preflight.WarningCount,
            informationCount = preflight.InformationCount,
            canGenerate = preflight.CanGenerate,
            isPublicationReady = preflight.IsPublicationReady,
            issues = preflight.Issues.Select(issue => new
            {
                severity = issue.Severity.ToString().ToLowerInvariant(),
                code = issue.Code.ToString(),
                issue.ProjectId,
                issue.ProjectName,
                issue.Message
            }).ToArray()
        };

    private static string Normalize(string? value, int maximumLength)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > maximumLength)
        {
            normalized = normalized[..maximumLength].TrimEnd();
        }
        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        bool preserveLineBreaks = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = preserveLineBreaks
            ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Trim()
            : Normalize(value, maximumLength);
        if (normalized.Length > maximumLength)
        {
            normalized = normalized[..maximumLength].TrimEnd();
        }
        return normalized;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? NormalizePhotoId(int? value)
        => value.HasValue && value.Value > 0 ? value : null;

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private static string SanitizeFileName(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = candidate
            .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character)
            .ToArray();
        var normalized = string.Join(
            "_",
            new string(characters).Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 72)
        {
            normalized = normalized[..72].TrimEnd('_');
        }
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    public sealed class GenerateBrochureInput
    {
        [Required]
        [StringLength(120)]
        public string? Title { get; set; }

        [Required]
        [StringLength(160)]
        public string? Subtitle { get; set; }

        [Required]
        [StringLength(80)]
        public string? Edition { get; set; }

        [Required]
        [StringLength(180)]
        public string? Strapline { get; set; }

        [Required]
        public BrochureCoverStyle CoverStyle { get; set; } = BrochureCoverStyle.Contemporary;

        [Required]
        public BrochureNarrativeSource NarrativeSource { get; set; } = BrochureNarrativeSource.ProjectBrief;

        [StringLength(120)]
        public string? IntroductionTitle { get; set; }

        [StringLength(3000)]
        public string? IntroductionText { get; set; }

        [StringLength(80)]
        [Display(Name = "Handling/classification marking")]
        public string? HandlingMarking { get; set; }

        public bool AllowTextOnlyProjects { get; set; }

        public List<BrochureProjectSelectionInput> Selections { get; set; } = new();
    }

    public sealed class BrochureProjectSelectionInput
    {
        public int ProjectId { get; set; }
        public int? PrimaryPhotoId { get; set; }
        public int? SecondaryPhotoId { get; set; }
        public double PrimaryFocalX { get; set; } = .5d;
        public double PrimaryFocalY { get; set; } = .5d;
        public double SecondaryFocalX { get; set; } = .5d;
        public double SecondaryFocalY { get; set; } = .5d;
        public BrochureImageMode ImageMode { get; set; } = BrochureImageMode.Automatic;
    }
}
