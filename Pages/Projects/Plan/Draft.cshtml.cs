using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;

namespace ProjectManagement.Pages.Projects.Plan;

[Authorize(Roles = "Project Officer,HoD,Admin")]
public class DraftModel : PageModel
{
    private const string StageTemplateVersion = "SDD-1.0";

    private readonly ApplicationDbContext _db;
    private readonly PlanDraftService _drafts;
    private readonly UserManager<ApplicationUser> _users;

    private Dictionary<string, StageTemplate> _templatesByCode = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _dependenciesByStage = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _stageIndex = new(StringComparer.OrdinalIgnoreCase);

    public DraftModel(ApplicationDbContext db, PlanDraftService drafts, UserManager<ApplicationUser> users)
    {
        _db = db;
        _drafts = drafts;
        _users = users;
    }

    [BindProperty]
    public List<StageInput> Stages { get; set; } = new();

    public List<StageTemplate> StageTemplates { get; private set; } = new();
    public PlanVersion? Draft { get; private set; }
    public int ProjectId { get; private set; }
    public string ProjectName { get; private set; } = string.Empty;
    public bool IsPreview { get; private set; }
    public bool AllowEdit { get; private set; } = true;

    [TempData]
    public string? StatusMessage { get; set; }

    public class StageInput
    {
        public string StageCode { get; set; } = string.Empty;
        public DateOnly? PlannedStart { get; set; }
        public DateOnly? PlannedDue { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id, bool preview = false, CancellationToken cancellationToken = default)
    {
        ProjectId = id;

        await LoadStageMetadataAsync(cancellationToken);

        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.Name, p.LeadPoUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            return NotFound();
        }

        ProjectName = project.Name;

        var userId = _users.GetUserId(User);
        if (userId == null)
        {
            return Challenge();
        }

        if (!UserCanEdit(project.LeadPoUserId, userId))
        {
            return Forbid();
        }

        Draft = await _drafts.CreateDraftAsync(id, userId, cancellationToken);
        if (!Draft.StagePlans.Any())
        {
            Draft = await _db.PlanVersions
                .Include(p => p.StagePlans)
                .FirstAsync(p => p.Id == Draft.Id, cancellationToken);
        }

        HydrateStageInputsFromPlan();

        IsPreview = preview;
        AllowEdit = !IsPreview;

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id, CancellationToken cancellationToken)
    {
        var result = await LoadForPostAsync(id, cancellationToken);
        if (result != null)
        {
            return result;
        }

        AllowEdit = true;
        NormalizeStageInputs();
        ValidateDependencies();

        if (!ModelState.IsValid)
        {
            IsPreview = false;
            return Page();
        }

        ApplyInputsToDraft();
        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = "Draft saved.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostPreviewAsync(int id, CancellationToken cancellationToken)
    {
        var result = await LoadForPostAsync(id, cancellationToken);
        if (result != null)
        {
            return result;
        }

        NormalizeStageInputs();
        ValidateDependencies();

        if (!ModelState.IsValid)
        {
            AllowEdit = true;
            IsPreview = false;
            return Page();
        }

        ApplyInputsToDraft();
        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { id, preview = true });
    }

    private async Task LoadStageMetadataAsync(CancellationToken cancellationToken)
    {
        StageTemplates = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == StageTemplateVersion)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);

        var dependencies = await _db.StageDependencyTemplates
            .AsNoTracking()
            .Where(d => d.Version == StageTemplateVersion)
            .ToListAsync(cancellationToken);

        _templatesByCode = StageTemplates.ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);
        _stageIndex = StageTemplates
            .Select((t, idx) => new { t.Code, idx })
            .ToDictionary(x => x.Code, x => x.idx, StringComparer.OrdinalIgnoreCase);
        _dependenciesByStage = dependencies
            .GroupBy(d => d.FromStageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DependsOnStageCode).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IActionResult?> LoadForPostAsync(int projectId, CancellationToken cancellationToken)
    {
        ProjectId = projectId;
        await LoadStageMetadataAsync(cancellationToken);

        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Name, p.LeadPoUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            return NotFound();
        }

        ProjectName = project.Name;

        var userId = _users.GetUserId(User);
        if (userId == null)
        {
            return Challenge();
        }

        if (!UserCanEdit(project.LeadPoUserId, userId))
        {
            return Forbid();
        }

        Draft = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.Draft, cancellationToken);

        if (Draft == null)
        {
            Draft = await _drafts.CreateDraftAsync(projectId, userId, cancellationToken);
            Draft = await _db.PlanVersions
                .Include(p => p.StagePlans)
                .FirstAsync(p => p.Id == Draft.Id, cancellationToken);
        }

        return null;
    }

    private void HydrateStageInputsFromPlan()
    {
        if (Draft == null)
        {
            Stages = StageTemplates.Select(t => new StageInput { StageCode = t.Code }).ToList();
            return;
        }

        var planStages = Draft.StagePlans.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);
        Stages = StageTemplates.Select(t =>
        {
            if (planStages.TryGetValue(t.Code, out var stage))
            {
                return new StageInput
                {
                    StageCode = t.Code,
                    PlannedStart = stage.PlannedStart,
                    PlannedDue = stage.PlannedDue
                };
            }

            return new StageInput { StageCode = t.Code };
        }).ToList();
    }

    private void NormalizeStageInputs()
    {
        var map = Stages.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);
        Stages = StageTemplates.Select(t =>
        {
            if (map.TryGetValue(t.Code, out var input))
            {
                return new StageInput
                {
                    StageCode = t.Code,
                    PlannedStart = input.PlannedStart,
                    PlannedDue = input.PlannedDue
                };
            }

            return new StageInput { StageCode = t.Code };
        }).ToList();
    }

    private void ValidateDependencies()
    {
        var inputs = Stages.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);

        foreach (var template in StageTemplates)
        {
            if (!inputs.TryGetValue(template.Code, out var input) || input.PlannedStart is not DateOnly start)
            {
                continue;
            }

            if (!_dependenciesByStage.TryGetValue(template.Code, out var predecessors) || predecessors.Count == 0)
            {
                continue;
            }

            DateOnly? latestDue = null;
            string? latestCode = null;

            foreach (var predecessor in predecessors)
            {
                if (inputs.TryGetValue(predecessor, out var predecessorInput) && predecessorInput.PlannedDue is DateOnly due)
                {
                    if (latestDue == null || due > latestDue)
                    {
                        latestDue = due;
                        latestCode = predecessor;
                    }
                }
            }

            if (latestDue.HasValue && start < latestDue.Value)
            {
                var index = _stageIndex[template.Code];
                var blockerName = latestCode != null && _templatesByCode.TryGetValue(latestCode, out var blocker)
                    ? blocker.Name
                    : latestCode ?? "the predecessor";
                ModelState.AddModelError($"Stages[{index}].PlannedStart",
                    $"Planned start must be on or after {blockerName}'s planned due date ({latestDue:dd MMM yyyy}).");
            }
        }
    }

    private void ApplyInputsToDraft()
    {
        if (Draft == null)
        {
            return;
        }

        var entities = Draft.StagePlans.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);

        foreach (var input in Stages)
        {
            if (!entities.TryGetValue(input.StageCode, out var entity))
            {
                entity = new StagePlan
                {
                    StageCode = input.StageCode
                };
                Draft.StagePlans.Add(entity);
                entities[input.StageCode] = entity;
            }

            entity.PlannedStart = input.PlannedStart;
            entity.PlannedDue = input.PlannedDue;
        }
    }

    private bool UserCanEdit(string? leadPoUserId, string? currentUserId)
    {
        if (User.IsInRole("Admin") || User.IsInRole("HoD"))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(currentUserId) && string.Equals(leadPoUserId, currentUserId, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
