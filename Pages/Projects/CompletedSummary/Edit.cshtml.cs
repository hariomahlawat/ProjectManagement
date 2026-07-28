using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.CompletedSummary;

[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.HoD},{RoleNames.ProjectOffice}")]
public sealed class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IClock _clock;

    public EditModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    [BindProperty]
    public EditCompletedProjectInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string ProjectName { get; private set; } = string.Empty;
    public int? CompletedYear { get; private set; }
    public decimal? RecordedDevelopmentCostLakhs { get; private set; }
    public ProjectTotStatus? TotStatus { get; private set; }
    public bool ShowNewLppEditor { get; private set; }

    public IReadOnlyList<SelectListItem> DocumentOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> TechStatusOptions { get; } = ProjectTechStatusCodes.All
        .Select(status => new SelectListItem(status, status))
        .ToArray();

    public string BackUrl => ReturnUrl ?? Url.Page("./Index") ?? "/Projects/CompletedSummary";
    public string TotStatusLabel => CompletedProjectPortfolioPolicy.GetTotLabel(TotStatus);

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        ReturnUrl = NormaliseReturnUrl(ReturnUrl);

        var loaded = await LoadAsync(id, populateForm: true, cancellationToken);
        if (!loaded)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ReturnUrl = NormaliseReturnUrl(ReturnUrl);

        var projectExists = await _db.Projects
            .AsNoTracking()
            .AnyAsync(
                p => p.Id == Input.ProjectId
                     && p.LifecycleStatus == ProjectLifecycleStatus.Completed
                     && !p.IsDeleted
                     && !p.IsArchived,
                cancellationToken);

        if (!projectExists)
        {
            return NotFound();
        }

        ValidateAssessmentAndProliferationInputs();
        await ValidateLppInputsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            ShowNewLppEditor = Input.HasNewLppPayload();
            await LoadAsync(Input.ProjectId, populateForm: false, cancellationToken);
            return Page();
        }

        var userId = _userManager.GetUserId(User) ?? "system";
        var now = _clock.UtcNow;

        await UpsertProliferationCostInformationAsync(userId, now, cancellationToken);
        await UpsertTechnologyAndProliferationAsync(userId, now, cancellationToken);
        await UpdateExistingLppRecordsAsync(cancellationToken);
        await AddNewLppRecordAsync(userId, now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        TempData["CompletedProjectEditSuccess"] = "Completed project details updated.";
        return LocalRedirect(BackUrl);
    }

    private void ValidateAssessmentAndProliferationInputs()
    {
        if (string.IsNullOrWhiteSpace(Input.TechStatus)
            || Array.IndexOf(ProjectTechStatusCodes.All, Input.TechStatus) < 0)
        {
            ModelState.AddModelError("Input.TechStatus", "Select a valid technology status.");
        }

        if (Input.ProliferationCostLakhs is <= 0m)
        {
            ModelState.AddModelError("Input.ProliferationCostLakhs", "Enter a proliferation cost greater than zero, or leave it blank.");
        }

        if (Input.AvailableForProliferation == false && string.IsNullOrWhiteSpace(Input.NotAvailableReason))
        {
            ModelState.AddModelError("Input.NotAvailableReason", "Enter the reason the project is not available for proliferation.");
        }

        if (Normalize(Input.NotAvailableReason)?.Length > 500)
        {
            ModelState.AddModelError("Input.NotAvailableReason", "Reason cannot exceed 500 characters.");
        }

        if (Normalize(Input.ProliferationRemarks)?.Length > 500)
        {
            ModelState.AddModelError("Input.ProliferationRemarks", "Proliferation remarks cannot exceed 500 characters.");
        }

        if (Normalize(Input.TechRemarks)?.Length > 500)
        {
            ModelState.AddModelError("Input.TechRemarks", "Technology remarks cannot exceed 500 characters.");
        }

        if (Normalize(Input.ProliferationCostRemarks)?.Length > 500)
        {
            ModelState.AddModelError("Input.ProliferationCostRemarks", "Proliferation cost remarks cannot exceed 500 characters.");
        }
    }

    private async Task ValidateLppInputsAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow.Date);

        if (Input.LppRecords is not null && Input.LppRecords.Count > 0)
        {
            var postedIds = Input.LppRecords.Select(x => x.Id).Distinct().ToList();
            var ownedIds = await _db.ProjectLppRecords
                .AsNoTracking()
                .Where(x => x.ProjectId == Input.ProjectId && postedIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            if (ownedIds.Count != postedIds.Count)
            {
                ModelState.AddModelError(string.Empty, "One or more LPP records could not be validated for this project.");
            }

            for (var i = 0; i < Input.LppRecords.Count; i++)
            {
                var record = Input.LppRecords[i];

                if (record.Amount < 0)
                {
                    ModelState.AddModelError($"Input.LppRecords[{i}].Amount", "LPP amount cannot be negative.");
                }

                if (record.Date is { } existingDate && existingDate > today)
                {
                    ModelState.AddModelError($"Input.LppRecords[{i}].Date", "LPP date cannot be in the future.");
                }

                if (Normalize(record.SupplyOrderNumber)?.Length > 64)
                {
                    ModelState.AddModelError($"Input.LppRecords[{i}].SupplyOrderNumber", "Supply order number cannot exceed 64 characters.");
                }

                if (Normalize(record.Remarks)?.Length > 500)
                {
                    ModelState.AddModelError($"Input.LppRecords[{i}].Remarks", "Remarks cannot exceed 500 characters.");
                }

                if (record.DocumentId.HasValue && !await IsValidProjectDocumentAsync(record.DocumentId.Value, cancellationToken))
                {
                    ModelState.AddModelError($"Input.LppRecords[{i}].DocumentId", "Select a valid document from the list.");
                }
            }
        }

        var hasNewLppInput = Input.HasNewLppPayload();
        if (hasNewLppInput && !Input.NewLppAmount.HasValue)
        {
            ModelState.AddModelError("Input.NewLppAmount", "Enter an amount for the new LPP.");
        }

        if (Input.NewLppAmount is < 0)
        {
            ModelState.AddModelError("Input.NewLppAmount", "LPP amount cannot be negative.");
        }

        if (Input.NewLppDate is { } newDate && newDate > today)
        {
            ModelState.AddModelError("Input.NewLppDate", "LPP date cannot be in the future.");
        }

        if (Normalize(Input.NewSupplyOrderNumber)?.Length > 64)
        {
            ModelState.AddModelError("Input.NewSupplyOrderNumber", "Supply order number cannot exceed 64 characters.");
        }

        if (Normalize(Input.NewLppRemarks)?.Length > 500)
        {
            ModelState.AddModelError("Input.NewLppRemarks", "Remarks cannot exceed 500 characters.");
        }

        if (Input.NewProjectDocumentId.HasValue
            && !await IsValidProjectDocumentAsync(Input.NewProjectDocumentId.Value, cancellationToken))
        {
            ModelState.AddModelError("Input.NewProjectDocumentId", "Select a valid document from the list.");
        }
    }

    private Task<bool> IsValidProjectDocumentAsync(int documentId, CancellationToken cancellationToken) =>
        _db.ProjectDocuments.AnyAsync(
            x => x.Id == documentId
                 && x.ProjectId == Input.ProjectId
                 && x.Status == ProjectDocumentStatus.Published
                 && !x.IsArchived,
            cancellationToken);

    private async Task UpsertProliferationCostInformationAsync(
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var proliferationCostFact = await _db.ProjectProductionCostFacts
            .FirstOrDefaultAsync(x => x.ProjectId == Input.ProjectId, cancellationToken);

        if (proliferationCostFact is null)
        {
            proliferationCostFact = new ProjectProductionCostFact { ProjectId = Input.ProjectId };
            await _db.ProjectProductionCostFacts.AddAsync(proliferationCostFact, cancellationToken);
        }

        proliferationCostFact.ApproxProductionCost = Input.ProliferationCostLakhs;
        proliferationCostFact.Remarks = Normalize(Input.ProliferationCostRemarks);
        proliferationCostFact.UpdatedAtUtc = now;
        proliferationCostFact.UpdatedByUserId = userId;
    }

    private async Task UpsertTechnologyAndProliferationAsync(
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var technology = await _db.ProjectTechStatuses
            .FirstOrDefaultAsync(x => x.ProjectId == Input.ProjectId, cancellationToken);

        if (technology is null)
        {
            technology = new ProjectTechStatus { ProjectId = Input.ProjectId };
            await _db.ProjectTechStatuses.AddAsync(technology, cancellationToken);
        }

        technology.TechStatus = Input.TechStatus!;
        technology.AvailableForProliferation = Input.AvailableForProliferation;
        technology.NotAvailableReason = Input.AvailableForProliferation == false
            ? Normalize(Input.NotAvailableReason)
            : null;
        technology.ProliferationRemarks = Normalize(Input.ProliferationRemarks);
        technology.Remarks = Normalize(Input.TechRemarks);
        technology.MarkedAtUtc = now;
        technology.MarkedByUserId = userId;
    }

    private async Task UpdateExistingLppRecordsAsync(CancellationToken cancellationToken)
    {
        if (Input.LppRecords is null || Input.LppRecords.Count == 0)
        {
            return;
        }

        var ids = Input.LppRecords.Select(r => r.Id).ToList();
        var existingLpps = await _db.ProjectLppRecords
            .Where(x => x.ProjectId == Input.ProjectId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var record in Input.LppRecords)
        {
            if (!existingLpps.TryGetValue(record.Id, out var entity))
            {
                continue;
            }

            entity.LppAmount = record.Amount;
            entity.LppDate = record.Date;
            entity.SupplyOrderNumber = Normalize(record.SupplyOrderNumber);
            entity.ProjectDocumentId = record.DocumentId;
            entity.Remarks = Normalize(record.Remarks);
        }
    }

    private async Task AddNewLppRecordAsync(
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!Input.NewLppAmount.HasValue)
        {
            return;
        }

        await _db.ProjectLppRecords.AddAsync(
            new ProjectLppRecord
            {
                ProjectId = Input.ProjectId,
                LppAmount = Input.NewLppAmount.Value,
                LppDate = Input.NewLppDate,
                SupplyOrderNumber = Normalize(Input.NewSupplyOrderNumber),
                ProjectDocumentId = Input.NewProjectDocumentId,
                Remarks = Normalize(Input.NewLppRemarks),
                CreatedAtUtc = now,
                CreatedByUserId = userId
            },
            cancellationToken);
    }

    private async Task<bool> LoadAsync(
        int projectId,
        bool populateForm,
        CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Id == projectId
                     && p.LifecycleStatus == ProjectLifecycleStatus.Completed
                     && !p.IsDeleted
                     && !p.IsArchived,
                cancellationToken);

        if (project is null)
        {
            return false;
        }

        ProjectName = project.Name;
        CompletedYear = project.CompletedYear;
        RecordedDevelopmentCostLakhs = project.CostLakhs;
        Input.ProjectId = projectId;

        TotStatus = await _db.ProjectTots
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Id)
            .Select(x => (ProjectTotStatus?)x.Status)
            .FirstOrDefaultAsync(cancellationToken);

        var proliferationCostFact = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);

        var technology = await _db.ProjectTechStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);

        var lpps = await _db.ProjectLppRecords
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.LppDate ?? DateOnly.MinValue)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var documentOptions = await _db.ProjectDocuments
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId
                        && d.Status == ProjectDocumentStatus.Published
                        && !d.IsArchived)
            .OrderBy(d => d.Title)
            .Select(d => new SelectListItem(d.Title, d.Id.ToString()))
            .ToListAsync(cancellationToken);

        DocumentOptions = new[] { new SelectListItem("(none)", string.Empty) }
            .Concat(documentOptions)
            .ToArray();

        if (populateForm)
        {
            Input.ProliferationCostLakhs = proliferationCostFact?.ApproxProductionCost;
            Input.ProliferationCostRemarks = proliferationCostFact?.Remarks;
            Input.TechStatus = technology?.TechStatus ?? ProjectTechStatusCodes.Current;
            Input.AvailableForProliferation = technology?.AvailableForProliferation;
            Input.NotAvailableReason = technology?.NotAvailableReason;
            Input.ProliferationRemarks = technology?.ProliferationRemarks;
            Input.TechRemarks = technology?.Remarks;
            Input.LppRecords = MapLppRecords(lpps);
        }
        else if (Input.LppRecords is null || Input.LppRecords.Count == 0)
        {
            Input.LppRecords = MapLppRecords(lpps);
        }

        return true;
    }

    private static List<LppRecordInput> MapLppRecords(IEnumerable<ProjectLppRecord> records) =>
        records.Select(record => new LppRecordInput
        {
            Id = record.Id,
            Amount = record.LppAmount,
            Date = record.LppDate,
            SupplyOrderNumber = record.SupplyOrderNumber,
            Remarks = record.Remarks,
            DocumentId = record.ProjectDocumentId
        }).ToList();

    private string? NormaliseReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return Url.IsLocalUrl(trimmed) ? trimmed : null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class EditCompletedProjectInput
    {
        public int ProjectId { get; set; }
        public decimal? ProliferationCostLakhs { get; set; }
        public string? ProliferationCostRemarks { get; set; }
        public string? TechStatus { get; set; } = ProjectTechStatusCodes.Current;
        public bool? AvailableForProliferation { get; set; }
        public string? NotAvailableReason { get; set; }
        public string? ProliferationRemarks { get; set; }
        public string? TechRemarks { get; set; }
        public List<LppRecordInput> LppRecords { get; set; } = new();
        public decimal? NewLppAmount { get; set; }
        public DateOnly? NewLppDate { get; set; }
        public string? NewSupplyOrderNumber { get; set; }
        public int? NewProjectDocumentId { get; set; }
        public string? NewLppRemarks { get; set; }

        public bool HasNewLppPayload() =>
            NewLppAmount.HasValue
            || NewLppDate.HasValue
            || !string.IsNullOrWhiteSpace(NewSupplyOrderNumber)
            || NewProjectDocumentId.HasValue
            || !string.IsNullOrWhiteSpace(NewLppRemarks);
    }

    public sealed class LppRecordInput
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public DateOnly? Date { get; set; }
        public string? SupplyOrderNumber { get; set; }
        public string? Remarks { get; set; }
        public int? DocumentId { get; set; }
    }
}
