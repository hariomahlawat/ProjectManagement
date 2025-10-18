using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation.Granular;

[Authorize(Policy = ProjectOfficeReportsPolicies.SubmitProliferationTracker)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ProliferationSubmissionService _submissionService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        ProliferationSubmissionService submissionService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _submissionService = submissionService ?? throw new ArgumentNullException(nameof(submissionService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public int? ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public ProliferationSource? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public ProliferationGranularity? Granularity { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Period { get; set; }

    public IReadOnlyList<ProjectOption> Projects { get; private set; } = Array.Empty<ProjectOption>();

    public IReadOnlyList<GranularRow> ApprovedEntries { get; private set; } = Array.Empty<GranularRow>();

    public IReadOnlyList<GranularRequestRow> PendingRequests { get; private set; } = Array.Empty<GranularRequestRow>();

    public bool CanApprove { get; private set; }

    [BindProperty]
    public GranularInput Input { get; set; } = new();

    [BindProperty]
    public DecisionInput Decision { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateAsync(cancellationToken);

        if (ProjectId.HasValue)
        {
            Input.ProjectId = ProjectId.Value;
        }

        if (Source.HasValue)
        {
            Input.Source = Source.Value;
        }

        if (Year.HasValue)
        {
            Input.Year = Year.Value;
        }

        if (Granularity.HasValue)
        {
            Input.Granularity = Granularity.Value;
        }

        if (Period.HasValue)
        {
            Input.Period = Period.Value;
        }

        if (ProjectId.HasValue && Source.HasValue && Year.HasValue && Granularity.HasValue && Period.HasValue)
        {
            var existing = await _db.ProliferationGranularEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    g => g.ProjectId == ProjectId.Value
                        && g.Source == Source.Value
                        && g.Year == Year.Value
                        && g.Granularity == Granularity.Value
                        && g.Period == Period.Value,
                    cancellationToken);

            if (existing is not null)
            {
                Input.DirectBeneficiaries = existing.Metrics.DirectBeneficiaries;
                Input.IndirectBeneficiaries = existing.Metrics.IndirectBeneficiaries;
                Input.InvestmentValue = existing.Metrics.InvestmentValue;
                Input.Notes = existing.Notes;
                Input.PeriodLabel = existing.PeriodLabel;
            }
        }
    }

    public async Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!ModelState.IsValid)
        {
            await PopulateAsync(cancellationToken);
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var submission = new ProliferationGranularSubmission(
            Input.ProjectId,
            Input.Source,
            Input.Year,
            Input.Granularity,
            Input.Period,
            Input.PeriodLabel,
            Input.DirectBeneficiaries,
            Input.IndirectBeneficiaries,
            Input.InvestmentValue,
            Input.Notes);

        var result = await _submissionService.SubmitGranularAsync(submission, userId, cancellationToken);
        if (!result.IsSuccess)
        {
            switch (result.Status)
            {
                case ProliferationRequestActionStatus.NotFound:
                    ModelState.AddModelError(string.Empty, "We could not find the selected project.");
                    break;
                case ProliferationRequestActionStatus.ValidationFailed:
                    ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to submit the granular metrics.");
                    break;
                case ProliferationRequestActionStatus.Conflict:
                    TempData["ToastError"] = result.ErrorMessage ?? "A granular submission is already pending approval for the selected period.";
                    break;
            }

            await PopulateAsync(cancellationToken);
            return Page();
        }

        TempData["ToastMessage"] = "Granular metrics submitted for approval.";
        if (!string.IsNullOrWhiteSpace(result.WarningMessage))
        {
            TempData["ToastWarning"] = result.WarningMessage;
        }

        return RedirectToPage(new
        {
            ProjectId = Input.ProjectId,
            Source = Input.Source,
            Year = Input.Year,
            Granularity = Input.Granularity,
            Period = Input.Period
        });
    }

    public async Task<IActionResult> OnPostDecideAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanApprove)
        {
            return Forbid();
        }

        if (Decision.RequestId == Guid.Empty)
        {
            ModelState.AddModelError(string.Empty, "We could not verify the approval request.");
            await PopulateAsync(cancellationToken);
            return Page();
        }

        byte[]? rowVersion = null;
        if (!string.IsNullOrWhiteSpace(Decision.RowVersion))
        {
            try
            {
                rowVersion = Convert.FromBase64String(Decision.RowVersion);
            }
            catch (FormatException)
            {
                ModelState.AddModelError(string.Empty, "The approval request could not be processed because the version token was invalid.");
                await PopulateAsync(cancellationToken);
                return Page();
            }
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var result = await _submissionService.DecideGranularAsync(
            Decision.RequestId,
            Decision.Approve,
            userId,
            rowVersion,
            Decision.Notes,
            cancellationToken);

        if (!result.IsSuccess)
        {
            switch (result.Status)
            {
                case ProliferationRequestActionStatus.NotFound:
                    TempData["ToastError"] = "We could not find the submitted granular record.";
                    break;
                case ProliferationRequestActionStatus.ValidationFailed:
                    ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to process the approval.");
                    break;
                case ProliferationRequestActionStatus.Conflict:
                    TempData["ToastError"] = result.ErrorMessage ?? "The submission was updated by someone else. Reload to continue.";
                    break;
            }

            await PopulateAsync(cancellationToken);
            return Page();
        }

        TempData["ToastMessage"] = Decision.Approve
            ? "Granular metrics approved."
            : "Granular metrics rejected.";

        ProjectId = Decision.ProjectId;
        Source = Decision.Source;
        Year = Decision.Year;
        Granularity = Decision.Granularity;
        Period = Decision.Period;

        return RedirectToPage(new
        {
            ProjectId,
            Source,
            Year,
            Granularity,
            Period
        });
    }

    private async Task PopulateAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();

        Projects = await _db.Projects
            .AsNoTracking()
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectOption(p.Id, p.Name))
            .ToListAsync(cancellationToken);

        IQueryable<ProliferationGranular> granularQuery = _db.ProliferationGranularEntries
            .AsNoTracking()
            .Include(g => g.Project);

        if (ProjectId.HasValue)
        {
            granularQuery = granularQuery.Where(g => g.ProjectId == ProjectId.Value);
        }

        if (Source.HasValue)
        {
            granularQuery = granularQuery.Where(g => g.Source == Source.Value);
        }

        if (Year.HasValue)
        {
            granularQuery = granularQuery.Where(g => g.Year == Year.Value);
        }

        if (Granularity.HasValue)
        {
            granularQuery = granularQuery.Where(g => g.Granularity == Granularity.Value);
        }

        granularQuery = granularQuery
            .OrderBy(g => g.Project!.Name)
            .ThenBy(g => g.Year)
            .ThenBy(g => g.Granularity)
            .ThenBy(g => g.Period);

        ApprovedEntries = await granularQuery
            .Select(g => new GranularRow(
                g.Id,
                g.ProjectId,
                g.Project!.Name,
                g.Source,
                g.Year,
                g.Granularity,
                g.Period,
                g.PeriodLabel,
                g.Metrics.DirectBeneficiaries,
                g.Metrics.IndirectBeneficiaries,
                g.Metrics.InvestmentValue,
                g.Notes,
                g.LastModifiedAtUtc ?? g.CreatedAtUtc,
                g.LastModifiedByUserId ?? g.CreatedByUserId))
            .ToListAsync(cancellationToken);

        PendingRequests = await _db.ProliferationGranularRequests
            .AsNoTracking()
            .Include(r => r.Project)
            .Where(r => r.DecisionState == ProliferationRequestDecisionState.Pending)
            .OrderBy(r => r.Project!.Name)
            .ThenBy(r => r.Year)
            .ThenBy(r => r.Granularity)
            .ThenBy(r => r.Period)
            .Select(r => new GranularRequestRow(
                r.Id,
                r.ProjectId,
                r.Project!.Name,
                r.Source,
                r.Year,
                r.Granularity,
                r.Period,
                r.PeriodLabel,
                r.Metrics.DirectBeneficiaries,
                r.Metrics.IndirectBeneficiaries,
                r.Metrics.InvestmentValue,
                r.Notes,
                r.SubmittedByUserId,
                r.SubmittedAtUtc,
                r.RowVersion,
                r.DecisionNotes))
            .ToListAsync(cancellationToken);
    }

    private async Task PopulatePermissionsAsync()
    {
        var approveResult = await _authorizationService.AuthorizeAsync(User, null, ProjectOfficeReportsPolicies.ApproveProliferationTracker);
        CanApprove = approveResult.Succeeded;
    }

    public sealed record ProjectOption(int Id, string Name);

    public sealed record GranularRow(
        Guid Id,
        int ProjectId,
        string ProjectName,
        ProliferationSource Source,
        int Year,
        ProliferationGranularity Granularity,
        int Period,
        string? PeriodLabel,
        int? DirectBeneficiaries,
        int? IndirectBeneficiaries,
        decimal? InvestmentValue,
        string? Notes,
        DateTimeOffset UpdatedAt,
        string UpdatedBy);

    public sealed record GranularRequestRow(
        Guid Id,
        int ProjectId,
        string ProjectName,
        ProliferationSource Source,
        int Year,
        ProliferationGranularity Granularity,
        int Period,
        string? PeriodLabel,
        int? DirectBeneficiaries,
        int? IndirectBeneficiaries,
        decimal? InvestmentValue,
        string? Notes,
        string SubmittedBy,
        DateTimeOffset SubmittedAt,
        byte[] RowVersion,
        string? DecisionNotes)
    {
        public string RowVersionBase64 => Convert.ToBase64String(RowVersion);
    }

    public sealed class GranularInput
    {
        [Required]
        [Display(Name = "Project")]
        public int ProjectId { get; set; }

        [Required]
        [Display(Name = "Source")]
        public ProliferationSource Source { get; set; } = ProliferationSource.Abw515;

        [Range(1900, 9999)]
        [Display(Name = "Year")]
        public int Year { get; set; } = DateTime.UtcNow.Year;

        [Required]
        [Display(Name = "Granularity")]
        public ProliferationGranularity Granularity { get; set; } = ProliferationGranularity.Monthly;

        [Display(Name = "Period")]
        public int Period { get; set; } = 1;

        [MaxLength(200)]
        [Display(Name = "Period label")]
        public string? PeriodLabel { get; set; }

        [Display(Name = "Direct beneficiaries")]
        public int? DirectBeneficiaries { get; set; }

        [Display(Name = "Indirect beneficiaries")]
        public int? IndirectBeneficiaries { get; set; }

        [Display(Name = "Investment value (₹)")]
        public decimal? InvestmentValue { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }
    }

    public sealed class DecisionInput
    {
        [Required]
        public Guid RequestId { get; set; }

        [Required]
        public bool Approve { get; set; }

        [Required]
        public int ProjectId { get; set; }

        [Required]
        public ProliferationSource Source { get; set; }

        [Range(1900, 9999)]
        public int Year { get; set; }

        [Required]
        public ProliferationGranularity Granularity { get; set; }

        [Required]
        public int Period { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public string? RowVersion { get; set; }
    }
}
