using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services;

namespace ProjectManagement.Pages.Projects;

[Authorize(Roles = "Project Officer,HoD,Admin")]
public class StagesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly StageRulesService _rules;
    private readonly IClock _clock;

    public StagesModel(ApplicationDbContext db, StageRulesService rules, IClock clock)
    {
        _db = db;
        _rules = rules;
        _clock = clock;
    }

    public record StageRow(
        string Code,
        string Name,
        DateOnly? PlannedStart,
        DateOnly? PlannedDue,
        StageStatus Status,
        DateOnly? ActualStart,
        DateOnly? CompletedOn,
        int SlipDays,
        StageGuardResult StartGuard,
        StageGuardResult CompleteGuard,
        StageGuardResult SkipGuard);

    public int ProjectId { get; private set; }
    public string ProjectName { get; private set; } = string.Empty;
    public List<StageRow> Stages { get; private set; } = new();
    public List<StageSlipSummary> StageSlips { get; private set; } = new();
    public ProjectRagStatus ProjectRag { get; private set; } = ProjectRagStatus.Green;
    public bool CanManageStages { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var cancellationToken = HttpContext.RequestAborted;
        return await LoadAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostStartAsync(int projectId, string stage, CancellationToken cancellationToken)
    {
        var stageCode = NormalizeStageCode(stage);
        if (stageCode == null)
        {
            ErrorMessage = "Stage code is required.";
            return RedirectToPage(new { id = projectId });
        }

        var (result, ctx) = await LoadForMutationAsync(projectId, cancellationToken);
        if (result != null)
        {
            return result;
        }

        if (ctx is null)
        {
            ErrorMessage = "Unable to load stage data.";
            return RedirectToPage(new { id = projectId });
        }

        var stageEntity = ctx.Stages.FirstOrDefault(s => s.StageCode.Equals(stageCode, StringComparison.OrdinalIgnoreCase));
        if (stageEntity == null)
        {
            return NotFound();
        }

        var context = await _rules.BuildContextAsync(ctx.Stages, cancellationToken);
        var guard = _rules.CanStart(context, stageCode);
        if (!guard.Allowed)
        {
            ErrorMessage = guard.Reason ?? $"Stage {stageCode} cannot be started.";
            return RedirectToPage(new { id = projectId });
        }

        var today = Today();
        stageEntity.ActualStart ??= today;
        stageEntity.Status = StageStatus.InProgress;

        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = $"Stage {stageCode} started.";
        return RedirectToPage(new { id = projectId });
    }

    public async Task<IActionResult> OnPostCompleteAsync(int projectId, string stage, CancellationToken cancellationToken)
    {
        var stageCode = NormalizeStageCode(stage);
        if (stageCode == null)
        {
            ErrorMessage = "Stage code is required.";
            return RedirectToPage(new { id = projectId });
        }

        var (result, ctx) = await LoadForMutationAsync(projectId, cancellationToken);
        if (result != null)
        {
            return result;
        }

        if (ctx is null)
        {
            ErrorMessage = "Unable to load stage data.";
            return RedirectToPage(new { id = projectId });
        }

        var stageEntity = ctx.Stages.FirstOrDefault(s => s.StageCode.Equals(stageCode, StringComparison.OrdinalIgnoreCase));
        if (stageEntity == null)
        {
            return NotFound();
        }

        var context = await _rules.BuildContextAsync(ctx.Stages, cancellationToken);
        var guard = _rules.CanComplete(context, stageCode);
        if (!guard.Allowed)
        {
            ErrorMessage = guard.Reason ?? $"Stage {stageCode} cannot be completed.";
            return RedirectToPage(new { id = projectId });
        }

        var today = Today();
        stageEntity.CompletedOn = today;
        stageEntity.ActualStart ??= today;
        stageEntity.Status = StageStatus.Completed;

        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = $"Stage {stageCode} completed.";
        return RedirectToPage(new { id = projectId });
    }

    public async Task<IActionResult> OnPostSkipAsync(int projectId, string stage, string? reason, CancellationToken cancellationToken)
    {
        var stageCode = NormalizeStageCode(stage);
        if (stageCode == null)
        {
            ErrorMessage = "Stage code is required.";
            return RedirectToPage(new { id = projectId });
        }

        reason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length is < 3 or > 200)
        {
            ErrorMessage = "Provide a reason between 3 and 200 characters to skip PNC.";
            return RedirectToPage(new { id = projectId });
        }

        var (result, ctx) = await LoadForMutationAsync(projectId, cancellationToken);
        if (result != null)
        {
            return result;
        }

        if (ctx is null)
        {
            ErrorMessage = "Unable to load stage data.";
            return RedirectToPage(new { id = projectId });
        }

        var stageEntity = ctx.Stages.FirstOrDefault(s => s.StageCode.Equals(stageCode, StringComparison.OrdinalIgnoreCase));
        if (stageEntity == null)
        {
            return NotFound();
        }

        var context = await _rules.BuildContextAsync(ctx.Stages, cancellationToken);
        var guard = _rules.CanSkip(context, stageCode);
        if (!guard.Allowed)
        {
            ErrorMessage = guard.Reason ?? $"Stage {stageCode} cannot be skipped.";
            return RedirectToPage(new { id = projectId });
        }

        stageEntity.Status = StageStatus.Skipped;
        stageEntity.ActualStart = null;
        stageEntity.CompletedOn = null;

        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = $"Stage {stageCode} skipped.";
        return RedirectToPage(new { id = projectId });
    }

    private async Task<IActionResult> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return Challenge();
        }

        var project = await _db.Projects
            .Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Name, p.LeadPoUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        ProjectId = project.Id;
        ProjectName = project.Name;

        var canManage = UserCanManage(project.LeadPoUserId, userId);
        if (!canManage && User.IsInRole("Project Officer"))
        {
            return Forbid();
        }

        CanManageStages = canManage;

        var templates = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == PlanConstants.StageTemplateVersion)
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Code, t.Name })
            .ToListAsync(cancellationToken);

        var projectStages = await _db.ProjectStages
            .AsNoTracking()
            .Where(ps => ps.ProjectId == id)
            .ToListAsync(cancellationToken);

        var stageLookup = projectStages
            .ToDictionary(ps => ps.StageCode, ps => ps, StringComparer.OrdinalIgnoreCase);

        var context = await _rules.BuildContextAsync(projectStages, cancellationToken);
        var today = Today();
        var health = StageHealthCalculator.Compute(projectStages, today);

        StageSlips = templates
            .Select(t => new StageSlipSummary(
                t.Code,
                health.SlipByStage.TryGetValue(t.Code, out var slip) ? slip : 0))
            .ToList();
        ProjectRag = health.Rag;

        Stages = templates
            .Select(template =>
            {
                stageLookup.TryGetValue(template.Code, out var projectStage);

                var status = projectStage?.Status ?? StageStatus.NotStarted;

                return new StageRow(
                    template.Code,
                    template.Name,
                    projectStage?.PlannedStart,
                    projectStage?.PlannedDue,
                    status,
                    projectStage?.ActualStart,
                    projectStage?.CompletedOn,
                    health.SlipByStage.TryGetValue(template.Code, out var slip) ? slip : 0,
                    _rules.CanStart(context, template.Code),
                    _rules.CanComplete(context, template.Code),
                    _rules.CanSkip(context, template.Code));
            })
            .ToList();

        return Page();
    }

    private async Task<(IActionResult? Result, MutationContext? Context)> LoadForMutationAsync(int projectId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return (Challenge(), null);
        }

        var project = await _db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.LeadPoUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            return (NotFound(), null);
        }

        if (!UserCanManage(project.LeadPoUserId, userId))
        {
            return (Forbid(), null);
        }

        var stages = await _db.ProjectStages
            .Where(ps => ps.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        return (null, new MutationContext(stages));
    }

    private static string? NormalizeStageCode(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            return null;
        }

        return stage.Trim().ToUpperInvariant();
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.UtcNow.DateTime);

    private bool UserCanManage(string? leadPoUserId, string? currentUserId)
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

    private sealed record MutationContext(List<ProjectStage> Stages);
}
