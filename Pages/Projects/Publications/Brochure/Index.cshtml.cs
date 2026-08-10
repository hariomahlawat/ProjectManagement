using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.Publications;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Pages.Projects.Publications.Brochure;

[Authorize]
public sealed class IndexModel : PageModel
{
    private const int MaximumSelectedProjects = 100;

    private readonly ApplicationDbContext _db;
    private readonly IProjectBriefingPhotoLoader _photoLoader;
    private readonly IClock _clock;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        IProjectBriefingPhotoLoader photoLoader,
        IClock clock,
        IWebHostEnvironment environment,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _photoLoader = photoLoader ?? throw new ArgumentNullException(nameof(photoLoader));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public GenerateBrochureInput Input { get; set; } = new();

    public IReadOnlyList<BrochureProjectListItemVm> Projects { get; private set; }
        = Array.Empty<BrochureProjectListItemVm>();

    public IReadOnlyList<string> ProjectCategories { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> TechnicalCategories { get; private set; } = Array.Empty<string>();
    public PublicationFontStatus FontStatus { get; private set; }
        = new(PublicationFontRegistry.FallbackFamilyName, PublicationFontRegistry.FallbackFamilyName, false, false, Array.Empty<string>());

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApplyDefaults();
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
    {
        ApplyDefaults();
        NormalizeInput();

        if (Input.ProjectIds.Count == 0)
        {
            ModelState.AddModelError(nameof(Input.ProjectIds), "Select at least one project for the brochure.");
        }
        else if (Input.ProjectIds.Count > MaximumSelectedProjects)
        {
            ModelState.AddModelError(nameof(Input.ProjectIds), $"A brochure can contain up to {MaximumSelectedProjects} selected projects.");
        }

        if (!Enum.IsDefined(Input.CoverStyle))
        {
            ModelState.AddModelError(nameof(Input.CoverStyle), "Select a valid cover style.");
        }
        if (!Enum.IsDefined(Input.NarrativeSource))
        {
            ModelState.AddModelError(nameof(Input.NarrativeSource), "Select a valid project narrative source.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var service = new BrochurePublicationService(_db, _photoLoader);
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
                _clock.UtcNow);

            var publication = await service.BuildAsync(Input.ProjectIds, options, cancellationToken);
            var builder = new BrochurePdfReportBuilder(_environment);
            var bytes = builder.Build(publication);
            var fileName = $"{SanitizeFileName(Input.Title, "SDD_Capability_Brochure")}_{_clock.UtcNow:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", fileName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Capability brochure generation failed. SelectedProjects={SelectedProjectCount}, Narrative={NarrativeSource}, Cover={CoverStyle}",
                Input.ProjectIds.Count,
                Input.NarrativeSource,
                Input.CoverStyle);
            ModelState.AddModelError(
                string.Empty,
                "The brochure could not be generated. Review the selected projects and publication warnings, then try again.");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var service = new BrochurePublicationService(_db, _photoLoader);
        Projects = await service.GetProjectOptionsAsync(cancellationToken);
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

        FontStatus = PublicationFontRegistry.EnsureRegistered(_environment.WebRootPath);
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
        Input.ProjectIds = Input.ProjectIds
            .Where(id => id > 0)
            .Distinct()
            .Take(MaximumSelectedProjects + 1)
            .ToList();
    }

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

        public List<int> ProjectIds { get; set; } = new();
    }
}
