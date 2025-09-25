using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions StageMetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ApplicationDbContext _db;
    private readonly PlanDraftService _drafts;
    private readonly PlanApprovalService _approvals;
    private readonly UserManager<ApplicationUser> _users;

    private Dictionary<string, StageTemplate> _templatesByCode = new(StringComparer.OrdinalIgnoreCase);
    private List<StageDependencyTemplate> _dependencyTemplates = new();
    private Dictionary<string, int> _stageSequence = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentUserId;

    public DraftModel(ApplicationDbContext db, PlanDraftService drafts, PlanApprovalService approvals, UserManager<ApplicationUser> users)
    {
        _db = db;
        _drafts = drafts;
        _approvals = approvals;
        _users = users;
    }

    [BindProperty]
    public DraftInput Input { get; set; } = new();

    public List<StageTemplate> StageTemplates { get; private set; } = new();
    public PlanVersion? Draft { get; private set; }
    public int ProjectId { get; private set; }
    public string ProjectName { get; private set; } = string.Empty;
    public bool AllowEdit { get; private set; }
    public bool CanSubmit { get; private set; }
    public string StageMetadataJson { get; private set; } = string.Empty;
    public IReadOnlyDictionary<string, (DateOnly start, DateOnly due)> ComputedSchedule { get; private set; } = new Dictionary<string, (DateOnly start, DateOnly due)>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ManualOverrideStages { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasManualOverrides => ManualOverrideStages.Count > 0;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken = default)
    {
        ProjectId = id;

        await LoadStageMetadataAsync(cancellationToken);
        BuildStageMetadataJson();

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

        _currentUserId = userId;

        if (!UserCanEdit(project.LeadPoUserId, userId))
        {
            return Forbid();
        }

        Draft = await LoadOrCreateDraftAsync(id, userId, cancellationToken);

        AllowEdit = Draft.Status == PlanVersionStatus.Draft;

        PopulateInputFromDraft();
        RecalculateSchedule();
        CanSubmit = AllowEdit && IsReadyForSubmission();

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id, CancellationToken cancellationToken)
    {
        var result = await LoadForPostAsync(id, requireDraft: true, cancellationToken);
        if (result != null)
        {
            return result;
        }

        AllowEdit = true;

        NormalizeInput();
        ValidateInputBasics();
        RecalculateSchedule();
        CanSubmit = AllowEdit && IsReadyForSubmission();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ApplyInputsToDraft();
        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = "Draft saved.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSubmitAsync(int id, CancellationToken cancellationToken)
    {
        var result = await LoadForPostAsync(id, requireDraft: true, cancellationToken);
        if (result != null)
        {
            return result;
        }

        AllowEdit = true;

        NormalizeInput();
        ValidateInputBasics();
        RecalculateSchedule();
        RequireSubmissionCompleteness();
        CanSubmit = AllowEdit && IsReadyForSubmission();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ApplyInputsToDraft();
        await _db.SaveChangesAsync(cancellationToken);

        if (string.IsNullOrEmpty(_currentUserId))
        {
            return Challenge();
        }

        try
        {
            await _approvals.SubmitAsync(id, _currentUserId, cancellationToken);
        }
        catch (PlanApprovalValidationException ex)
        {
            foreach (var error in ex.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            CanSubmit = false;
            return Page();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            CanSubmit = false;
            return Page();
        }

        StatusMessage = "Project Timeline submitted for approval.";
        return RedirectToPage(new { id });
    }

    private async Task LoadStageMetadataAsync(CancellationToken cancellationToken)
    {
        StageTemplates = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == PlanConstants.StageTemplateVersion)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);

        _dependencyTemplates = await _db.StageDependencyTemplates
            .AsNoTracking()
            .Where(d => d.Version == PlanConstants.StageTemplateVersion)
            .ToListAsync(cancellationToken);

        _templatesByCode = StageTemplates.ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);
        _stageSequence = StageTemplates.ToDictionary(t => t.Code, t => t.Sequence, StringComparer.OrdinalIgnoreCase);
    }

    private void BuildStageMetadataJson()
    {
        var payload = new
        {
            stages = StageTemplates.Select(t => new
            {
                t.Code,
                t.Name,
                t.Sequence,
                t.Optional,
                t.ParallelGroup
            }),
            dependencies = _dependencyTemplates.Select(d => new
            {
                from = d.FromStageCode,
                on = d.DependsOnStageCode
            })
        };

        StageMetadataJson = JsonSerializer.Serialize(payload, StageMetadataJsonOptions);
    }

    private async Task<PlanVersion> LoadOrCreateDraftAsync(int projectId, string userId, CancellationToken cancellationToken)
    {
        var draft = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (draft == null)
        {
            draft = await _drafts.CreateDraftAsync(projectId, userId, cancellationToken);
            draft = await _db.PlanVersions
                .Include(p => p.StagePlans)
                .FirstAsync(p => p.Id == draft.Id, cancellationToken);
        }
        else if (!draft.StagePlans.Any())
        {
            await _db.Entry(draft).Collection(p => p.StagePlans).LoadAsync(cancellationToken);
        }

        return draft;
    }

    private async Task<IActionResult?> LoadForPostAsync(int projectId, bool requireDraft, CancellationToken cancellationToken)
    {
        ProjectId = projectId;

        await LoadStageMetadataAsync(cancellationToken);
        BuildStageMetadataJson();

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

        _currentUserId = userId;

        if (!UserCanEdit(project.LeadPoUserId, userId))
        {
            return Forbid();
        }

        Draft = await LoadOrCreateDraftAsync(projectId, userId, cancellationToken);

        if (requireDraft && Draft.Status != PlanVersionStatus.Draft)
        {
            return Forbid();
        }

        AllowEdit = Draft.Status == PlanVersionStatus.Draft;
        CanSubmit = AllowEdit && IsReadyForSubmission();

        return null;
    }

    private void PopulateInputFromDraft()
    {
        Input = new DraftInput
        {
            AnchorStageCode = !string.IsNullOrWhiteSpace(Draft?.AnchorStageCode)
                ? Draft!.AnchorStageCode!
                : PlanConstants.DefaultAnchorStageCode,
            AnchorDate = Draft?.AnchorDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            SkipWeekends = Draft?.SkipWeekends ?? true,
            TransitionRule = Draft?.TransitionRule ?? PlanTransitionRule.NextWorkingDay,
            PncApplicable = Draft?.PncApplicable ?? true,
            UnlockManualDates = false
        };

        var planStages = Draft?.StagePlans.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, StagePlan>(StringComparer.OrdinalIgnoreCase);

        foreach (var template in StageTemplates)
        {
            var row = new DraftInput.StageRow
            {
                StageCode = template.Code,
                DurationDays = 1
            };

            if (planStages.TryGetValue(template.Code, out var stage) &&
                stage.PlannedStart is DateOnly start &&
                stage.PlannedDue is DateOnly due)
            {
                row.DurationDays = Math.Max((due.DayNumber - start.DayNumber) + 1, 0);
            }

            Input.Stages.Add(row);
        }

        DetectExistingManualOverrides(planStages);
    }

    private void DetectExistingManualOverrides(IReadOnlyDictionary<string, StagePlan> planStages)
    {
        if (Input.AnchorDate == default)
        {
            Input.AnchorDate = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        var calculator = new PlanCalculator(StageTemplates, _dependencyTemplates);
        var durations = Input.Stages.ToDictionary(s => s.StageCode, s => Math.Max(s.DurationDays, 0), StringComparer.OrdinalIgnoreCase);
        var baseOptions = new PlanCalculatorOptions(
            Input.AnchorStageCode,
            Input.AnchorDate,
            Input.SkipWeekends,
            Input.TransitionRule,
            Input.PncApplicable,
            durations,
            new Dictionary<string, PlanCalculatorManualOverride>(StringComparer.OrdinalIgnoreCase));

        IDictionary<string, (DateOnly start, DateOnly due)> projectTimeline;
        try
        {
            projectTimeline = calculator.Compute(baseOptions);
        }
        catch (InvalidOperationException)
        {
            projectTimeline = new Dictionary<string, (DateOnly start, DateOnly due)>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var stage in planStages.Values)
        {
            if (stage.PlannedStart is not DateOnly start || stage.PlannedDue is not DateOnly due)
            {
                continue;
            }

            if (!projectTimeline.TryGetValue(stage.StageCode, out var computed) || computed.start != start || computed.due != due)
            {
                var row = Input.Stages.FirstOrDefault(r => string.Equals(r.StageCode, stage.StageCode, StringComparison.OrdinalIgnoreCase));
                if (row != null)
                {
                    row.ManualStart = start;
                    row.ManualDue = due;
                }
            }
        }

        if (Input.Stages.Any(s => s.ManualStart.HasValue || s.ManualDue.HasValue))
        {
            Input.UnlockManualDates = true;
        }
    }

    private void NormalizeInput()
    {
        Input.AnchorStageCode = string.IsNullOrWhiteSpace(Input.AnchorStageCode)
            ? PlanConstants.DefaultAnchorStageCode
            : Input.AnchorStageCode.Trim().ToUpperInvariant();

        var map = (Input.Stages ?? new List<DraftInput.StageRow>()).ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);
        var normalized = new List<DraftInput.StageRow>(StageTemplates.Count);

        foreach (var template in StageTemplates)
        {
            if (map.TryGetValue(template.Code, out var stage))
            {
                normalized.Add(new DraftInput.StageRow
                {
                    StageCode = template.Code,
                    DurationDays = stage.DurationDays,
                    ManualStart = stage.ManualStart,
                    ManualDue = stage.ManualDue
                });
            }
            else
            {
                normalized.Add(new DraftInput.StageRow
                {
                    StageCode = template.Code,
                    DurationDays = 1
                });
            }
        }

        Input.Stages = normalized;
    }

    private void ValidateInputBasics()
    {
        if (string.IsNullOrWhiteSpace(Input.AnchorStageCode) || !_templatesByCode.ContainsKey(Input.AnchorStageCode))
        {
            ModelState.AddModelError("Input.AnchorStageCode", "Select a valid anchor stage.");
        }

        if (Input.AnchorDate == default)
        {
            ModelState.AddModelError("Input.AnchorDate", "Anchor date is required.");
        }

        for (var i = 0; i < Input.Stages.Count; i++)
        {
            var stage = Input.Stages[i];
            if (stage.DurationDays < 0)
            {
                ModelState.AddModelError($"Input.Stages[{i}].DurationDays", "Duration must be zero or greater.");
            }

            if (stage.ManualStart.HasValue && stage.ManualDue.HasValue && stage.ManualDue.Value < stage.ManualStart.Value)
            {
                ModelState.AddModelError($"Input.Stages[{i}].ManualDue", "Manual due must be on or after the manual start date.");
            }
        }
    }

    private void RecalculateSchedule()
    {
        ComputedSchedule = new Dictionary<string, (DateOnly start, DateOnly due)>(StringComparer.OrdinalIgnoreCase);
        ManualOverrideStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (StageTemplates.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Input.AnchorStageCode) || !_templatesByCode.ContainsKey(Input.AnchorStageCode))
        {
            return;
        }

        if (Input.AnchorDate == default)
        {
            return;
        }

        var durations = Input.Stages
            .ToDictionary(s => s.StageCode, s => Math.Max(s.DurationDays, 0), StringComparer.OrdinalIgnoreCase);

        var manualOverrides = Input.Stages
            .Where(s => s.ManualStart.HasValue || s.ManualDue.HasValue)
            .ToDictionary(s => s.StageCode, s => new PlanCalculatorManualOverride(s.ManualStart, s.ManualDue), StringComparer.OrdinalIgnoreCase);

        var options = new PlanCalculatorOptions(
            Input.AnchorStageCode,
            Input.AnchorDate,
            Input.SkipWeekends,
            Input.TransitionRule,
            Input.PncApplicable,
            durations,
            manualOverrides);

        try
        {
            var calculator = new PlanCalculator(StageTemplates, _dependencyTemplates);
            var computed = calculator.Compute(options);
            ComputedSchedule = new Dictionary<string, (DateOnly start, DateOnly due)>(computed, StringComparer.OrdinalIgnoreCase);
            ManualOverrideStages = new HashSet<string>(computed.Keys.Where(manualOverrides.ContainsKey), StringComparer.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
    }

    private void RequireSubmissionCompleteness()
    {
        if (!_templatesByCode.TryGetValue(Input.AnchorStageCode, out var anchor))
        {
            ModelState.AddModelError("Input.AnchorStageCode", "Select a valid anchor stage.");
            return;
        }

        foreach (var template in StageTemplates)
        {
            if (template.Sequence < anchor.Sequence)
            {
                continue;
            }

            if (!Input.PncApplicable && string.Equals(template.Code, "PNC", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ComputedSchedule.TryGetValue(template.Code, out var schedule))
            {
                ModelState.AddModelError(string.Empty, $"Stage {template.Name} is missing a computed schedule.");
                continue;
            }

            if (schedule.due < schedule.start)
            {
                ModelState.AddModelError(string.Empty, $"Stage {template.Name} has an invalid planned range.");
            }
        }
    }

    private bool IsReadyForSubmission()
    {
        if (ComputedSchedule.Count == 0)
        {
            return false;
        }

        if (!_templatesByCode.TryGetValue(Input.AnchorStageCode, out var anchor))
        {
            return false;
        }

        if (Input.AnchorDate == default)
        {
            return false;
        }

        foreach (var template in StageTemplates)
        {
            if (template.Sequence < anchor.Sequence)
            {
                continue;
            }

            if (!Input.PncApplicable && string.Equals(template.Code, "PNC", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ComputedSchedule.TryGetValue(template.Code, out var schedule))
            {
                return false;
            }

            if (schedule.due < schedule.start)
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyInputsToDraft()
    {
        if (Draft == null)
        {
            return;
        }

        Draft.AnchorStageCode = Input.AnchorStageCode;
        Draft.AnchorDate = Input.AnchorDate;
        Draft.SkipWeekends = Input.SkipWeekends;
        Draft.TransitionRule = Input.TransitionRule;
        Draft.PncApplicable = Input.PncApplicable;

        var entities = Draft.StagePlans.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);

        foreach (var template in StageTemplates)
        {
            if (!entities.TryGetValue(template.Code, out var entity))
            {
                entity = new StagePlan
                {
                    StageCode = template.Code,
                    PlanVersionId = Draft.Id
                };
                Draft.StagePlans.Add(entity);
                entities[template.Code] = entity;
            }

            if (ComputedSchedule.TryGetValue(template.Code, out var schedule))
            {
                entity.PlannedStart = schedule.start;
                entity.PlannedDue = schedule.due;
            }
            else
            {
                entity.PlannedStart = null;
                entity.PlannedDue = null;
            }
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

    public class DraftInput
    {
        public string AnchorStageCode { get; set; } = PlanConstants.DefaultAnchorStageCode;
        public DateOnly AnchorDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
        public bool SkipWeekends { get; set; } = true;
        public PlanTransitionRule TransitionRule { get; set; } = PlanTransitionRule.NextWorkingDay;
        public bool PncApplicable { get; set; } = true;
        public bool UnlockManualDates { get; set; }
        public List<StageRow> Stages { get; set; } = new();

        public class StageRow
        {
            public string StageCode { get; set; } = string.Empty;
            public int DurationDays { get; set; }
            public DateOnly? ManualStart { get; set; }
            public DateOnly? ManualDue { get; set; }
        }
    }
}
