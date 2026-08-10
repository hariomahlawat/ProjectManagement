using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Compendiums;

namespace ProjectManagement.Pages.Projects.Publications.Compendium;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ICompendiumReadService _readService;
    private readonly ICompendiumExportService _exportService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ICompendiumReadService readService,
        ICompendiumExportService exportService,
        ILogger<IndexModel> logger)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public GenerateInput Input { get; set; } = new();

    public CompendiumPreflightDto Preflight { get; private set; } = CompendiumPreflightDto.Empty;
    public IReadOnlyList<CompendiumProjectReadinessDto> WarningProjects { get; private set; }
        = Array.Empty<CompendiumProjectReadinessDto>();
    public bool CanMaintainProjectData { get; private set; }
    public bool CanManageProjectPhotos { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
    {
        Input.HandlingMarking = NormalizeOptional(Input.HandlingMarking);
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var result = await _exportService.GenerateAsync(
                new CompendiumExportRequest(Input.HandlingMarking),
                cancellationToken);
            return File(result.Bytes, "application/pdf", result.FileName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Simulators Compendium PDF generation failed from Publications workspace.");
            ModelState.AddModelError(string.Empty, "The compendium could not be generated. Review publication readiness and try again.");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public static string IssueLabel(CompendiumPublicationIssue issue)
        => issue switch
        {
            CompendiumPublicationIssue.MissingPhoto => "Photo missing",
            CompendiumPublicationIssue.MissingArmService => "Arm/Service missing",
            CompendiumPublicationIssue.MissingProliferationCost => "Cost missing",
            CompendiumPublicationIssue.ZeroProliferationCost => "Zero cost — verify",
            CompendiumPublicationIssue.MissingDescription => "Description missing",
            CompendiumPublicationIssue.MissingCompletionYear => "Completion year missing",
            CompendiumPublicationIssue.PossibleTitleTypo => "Possible title typo",
            _ => "Review required"
        };

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var data = await _readService.GetProliferationCompendiumAsync(cancellationToken);
        Preflight = data.Preflight;
        WarningProjects = data.Preflight.Projects
            .Where(project => project.HasWarnings)
            .ToArray();

        CanMaintainProjectData =
            User.IsInRole(RoleNames.Admin)
            || User.IsInRole(RoleNames.HoD)
            || User.IsInRole(RoleNames.ProjectOffice);
        CanManageProjectPhotos = CanMaintainProjectData || User.IsInRole(RoleNames.ProjectOfficer);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 80 ? normalized : normalized[..80].TrimEnd();
    }

    public sealed class GenerateInput
    {
        [Display(Name = "Handling/classification marking")]
        [StringLength(80)]
        public string? HandlingMarking { get; set; }
    }
}
