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

namespace ProjectManagement.Pages.Projects.CompletedSummary;

[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.HoD},{RoleNames.ProjectOffice}")]
// SECTION: Completed projects summary edit page model
public sealed class EditModel : PageModel
{
    // SECTION: Dependencies
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

    // SECTION: View data
    [BindProperty]
    public EditCompletedProjectInput Input { get; set; } = new();

    public string ProjectName { get; private set; } = string.Empty;
    public IReadOnlyList<SelectListItem> DocumentOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> TechStatusOptions { get; } = ProjectTechStatusCodes.All
        .Select(status => new SelectListItem(status, status))
        .ToArray();

    // SECTION: Handlers
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(id, populateForm: true, cancellationToken);
        if (!loaded)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(Input.ProjectId, populateForm: false, cancellationToken);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Input.TechStatus) && Array.IndexOf(ProjectTechStatusCodes.All, Input.TechStatus) < 0)
        {
            ModelState.AddModelError(nameof(Input.TechStatus), "Select a valid technology status.");
        }

        if (Input.ApproxProductionCost is < 0)
        {
            ModelState.AddModelError(nameof(Input.ApproxProductionCost), "Approximate production cost cannot be negative.");
        }

        var hasNewLppInput = Input.HasNewLppPayload();

        if (hasNewLppInput && !Input.NewLppAmount.HasValue)
        {
            ModelState.AddModelError(nameof(Input.NewLppAmount), "Enter an amount for the new LPP.");
        }

        if (Input.NewLppAmount is < 0)
        {
            ModelState.AddModelError(nameof(Input.NewLppAmount), "LPP amount cannot be negative.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(Input.ProjectId, populateForm: false, cancellationToken);
            return Page();
        }

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == Input.ProjectId, cancellationToken);

        if (project == null)
        {
            return NotFound();
        }

        var userId = _userManager.GetUserId(User) ?? "system";
        var now = _clock.UtcNow;

        var prod = await _db.ProjectProductionCostFacts
            .FirstOrDefaultAsync(x => x.ProjectId == Input.ProjectId, cancellationToken);

        if (prod == null)
        {
            prod = new ProjectProductionCostFact
            {
                ProjectId = Input.ProjectId
            };
            await _db.ProjectProductionCostFacts.AddAsync(prod, cancellationToken);
        }

        prod.ApproxProductionCost = Input.ApproxProductionCost;
        prod.Remarks = Normalize(Input.ProductionRemarks);
        prod.UpdatedAtUtc = now;
        prod.UpdatedByUserId = userId;

        var tech = await _db.ProjectTechStatuses
            .FirstOrDefaultAsync(x => x.ProjectId == Input.ProjectId, cancellationToken);

        if (tech == null)
        {
            tech = new ProjectTechStatus
            {
                ProjectId = Input.ProjectId
            };
            await _db.ProjectTechStatuses.AddAsync(tech, cancellationToken);
        }

        tech.TechStatus = Input.TechStatus ?? ProjectTechStatusCodes.Current;
        tech.AvailableForProliferation = Input.AvailableForProliferation;
        tech.NotAvailableReason = Normalize(Input.NotAvailableReason);
        tech.Remarks = Normalize(Input.TechRemarks);
        tech.MarkedAtUtc = now;
        tech.MarkedByUserId = userId;

        if (Input.NewProjectDocumentId.HasValue)
        {
            var documentExists = await _db.ProjectDocuments
                .AnyAsync(
                    d => d.Id == Input.NewProjectDocumentId.Value &&
                         d.ProjectId == Input.ProjectId &&
                         d.Status == ProjectDocumentStatus.Published &&
                         !d.IsArchived,
                    cancellationToken);

            if (!documentExists)
            {
                ModelState.AddModelError(nameof(Input.NewProjectDocumentId), "Select a valid document from the list.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(Input.ProjectId, populateForm: false, cancellationToken);
            return Page();
        }

        if (Input.NewLppAmount.HasValue)
        {
            var lpp = new ProjectLppRecord
            {
                ProjectId = Input.ProjectId,
                LppAmount = Input.NewLppAmount.Value,
                LppDate = Input.NewLppDate,
                SupplyOrderNumber = Normalize(Input.NewSupplyOrderNumber),
                ProjectDocumentId = Input.NewProjectDocumentId,
                Remarks = Normalize(Input.NewLppRemarks),
                CreatedAtUtc = now,
                CreatedByUserId = userId
            };
            await _db.ProjectLppRecords.AddAsync(lpp, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return RedirectToPage("./Index");
    }

    // SECTION: Helpers
    private async Task<bool> LoadAsync(int projectId, bool populateForm, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return false;
        }

        ProjectName = project.Name;
        Input.ProjectId = projectId;

        var prod = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);

        var tech = await _db.ProjectTechStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);

        var lpps = await _db.ProjectLppRecords
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Include(x => x.ProjectDocument)
            .OrderByDescending(x => x.LppDate ?? DateOnly.MinValue)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        Input.LppRecords = lpps
            .Select(l => new LppRecordVm
            {
                Id = l.Id,
                Amount = l.LppAmount,
                Date = l.LppDate,
                SupplyOrderNumber = l.SupplyOrderNumber,
                Remarks = l.Remarks,
                DocumentId = l.ProjectDocumentId,
                DocumentTitle = l.ProjectDocument?.Title
            })
            .ToList();

        var documentOptions = await _db.ProjectDocuments
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.Status == ProjectDocumentStatus.Published && !d.IsArchived)
            .OrderBy(d => d.Title)
            .Select(d => new SelectListItem(d.Title, d.Id.ToString()))
            .ToListAsync(cancellationToken);

        DocumentOptions = new[] { new SelectListItem("(none)", string.Empty) }
            .Concat(documentOptions)
            .ToArray();

        if (populateForm)
        {
            Input.ApproxProductionCost = prod?.ApproxProductionCost;
            Input.ProductionRemarks = prod?.Remarks;
            Input.TechStatus = tech?.TechStatus ?? ProjectTechStatusCodes.Current;
            Input.AvailableForProliferation = tech?.AvailableForProliferation ?? false;
            Input.NotAvailableReason = tech?.NotAvailableReason;
            Input.TechRemarks = tech?.Remarks;
        }

        return true;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // SECTION: View models
    public sealed class EditCompletedProjectInput
    {
        public int ProjectId { get; set; }

        public decimal? ApproxProductionCost { get; set; }
        public string? ProductionRemarks { get; set; }

        public string? TechStatus { get; set; } = ProjectTechStatusCodes.Current;
        public bool AvailableForProliferation { get; set; }
        public string? NotAvailableReason { get; set; }
        public string? TechRemarks { get; set; }

        public List<LppRecordVm> LppRecords { get; set; } = new();

        public decimal? NewLppAmount { get; set; }
        public DateOnly? NewLppDate { get; set; }
        public string? NewSupplyOrderNumber { get; set; }
        public int? NewProjectDocumentId { get; set; }
        public string? NewLppRemarks { get; set; }

        public bool HasNewLppPayload()
        {
            return NewLppAmount.HasValue ||
                   NewLppDate.HasValue ||
                   !string.IsNullOrWhiteSpace(NewSupplyOrderNumber) ||
                   NewProjectDocumentId.HasValue ||
                   !string.IsNullOrWhiteSpace(NewLppRemarks);
        }
    }

    public sealed class LppRecordVm
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public DateOnly? Date { get; set; }
        public string? SupplyOrderNumber { get; set; }
        public string? Remarks { get; set; }
        public int? DocumentId { get; set; }
        public string? DocumentTitle { get; set; }
    }
}
