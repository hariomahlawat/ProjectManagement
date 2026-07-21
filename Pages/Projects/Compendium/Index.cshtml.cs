using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Compendiums;

namespace ProjectManagement.Pages.Projects.Compendium;

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
    public GenerateCompendiumInput Input { get; set; } = new();

    public CompendiumPreflightDto Preflight { get; private set; } = CompendiumPreflightDto.Empty;

    public IReadOnlyList<CompendiumProjectReadinessDto> WarningProjects { get; private set; }
        = Array.Empty<CompendiumProjectReadinessDto>();

    public bool CanMaintainProjectData { get; private set; }
    public bool CanManageProjectPhotos { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken cancellationToken)
    {
        Input.HandlingMarking = NormalizeHandlingMarking(Input.HandlingMarking);

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
            _logger.LogError(exception, "Simulators Compendium PDF generation failed.");
            ModelState.AddModelError(
                string.Empty,
                "The compendium could not be generated. Review the project data and try again.");

            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public static string GetIssueLabel(CompendiumPublicationIssue issue)
        => issue switch
        {
            CompendiumPublicationIssue.MissingPhoto => "Photograph missing",
            CompendiumPublicationIssue.MissingArmService => "Arm/Service missing",
            CompendiumPublicationIssue.MissingProliferationCost => "Cost missing",
            CompendiumPublicationIssue.ZeroProliferationCost => "Zero cost—verify basis",
            CompendiumPublicationIssue.MissingDescription => "Description missing",
            CompendiumPublicationIssue.MissingCompletionYear => "Completion year missing",
            CompendiumPublicationIssue.PossibleTitleTypo => "Possible AI/Al title typo",
            _ => "Review required"
        };

    public static string GetIssueCssClass(CompendiumPublicationIssue issue)
        => issue switch
        {
            CompendiumPublicationIssue.MissingPhoto => "compendium-issue--photo",
            CompendiumPublicationIssue.MissingProliferationCost => "compendium-issue--cost",
            CompendiumPublicationIssue.ZeroProliferationCost => "compendium-issue--cost",
            _ => ""
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

        CanManageProjectPhotos =
            CanMaintainProjectData
            || User.IsInRole(RoleNames.ProjectOfficer);
    }

    private static string? NormalizeHandlingMarking(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public sealed class GenerateCompendiumInput
    {
        [Display(Name = "Handling/classification marking")]
        [StringLength(80, ErrorMessage = "The marking cannot exceed 80 characters.")]
        public string? HandlingMarking { get; set; }
    }
}
