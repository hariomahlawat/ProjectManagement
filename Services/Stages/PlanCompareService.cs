using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

public record PlanDiffRow(string StageCode, DateOnly? NewStart, DateOnly? NewDue, DateOnly? OldStart, DateOnly? OldDue);

public sealed class PlanCompareService
{
    private readonly ApplicationDbContext _db;

    public PlanCompareService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlanDiffRow>> GetDiffAsync(int projectId, CancellationToken ct = default)
    {
        var lastSnapshot = await _db.ProjectPlanSnapshots
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.TakenAt)
            .FirstOrDefaultAsync(ct);

        var currentStages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.StageCode)
            .ToListAsync(ct);

        if (lastSnapshot is null)
        {
            return currentStages
                .Select(stage => new PlanDiffRow(
                    stage.StageCode,
                    stage.PlannedStart,
                    stage.PlannedDue,
                    null,
                    null))
                .ToList();
        }

        var approved = await _db.ProjectPlanSnapshotRows
            .Where(r => r.SnapshotId == lastSnapshot.Id)
            .ToDictionaryAsync(r => r.StageCode, ct);

        return currentStages
            .Select(stage =>
            {
                approved.TryGetValue(stage.StageCode, out var row);
                return new PlanDiffRow(
                    stage.StageCode,
                    stage.PlannedStart,
                    stage.PlannedDue,
                    row?.PlannedStart,
                    row?.PlannedDue);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<PlanDiffRow>> GetDraftVsCurrentAsync(int projectId, CancellationToken ct = default)
    {
        var draft = await _db.PlanVersions
            .AsNoTracking()
            .Include(v => v.StagePlans)
            .Where(v => v.ProjectId == projectId && v.Status == PlanVersionStatus.PendingApproval)
            .OrderByDescending(v => v.SubmittedOn)
            .ThenByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

        if (draft is null)
        {
            return Array.Empty<PlanDiffRow>();
        }

        var draftLookup = draft.StagePlans
            .Where(sp => !string.IsNullOrWhiteSpace(sp.StageCode))
            .ToDictionary(sp => sp.StageCode!, sp => sp, StringComparer.OrdinalIgnoreCase);

        var currentStages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.StageCode)
            .ToListAsync(ct);

        var workflowVersion = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.WorkflowVersion)
            .FirstOrDefaultAsync(ct);

        var stageOrderLookup = ProcurementWorkflow.BuildOrderLookup(workflowVersion);

        var currentLookup = currentStages
            .Where(s => !string.IsNullOrWhiteSpace(s.StageCode))
            .ToDictionary(s => s.StageCode!, s => s, StringComparer.OrdinalIgnoreCase);

        var codes = currentLookup.Keys
            .Union(draftLookup.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(code =>
            {
                return stageOrderLookup.TryGetValue(code, out var index) ? index : int.MaxValue;
            })
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var diffs = new List<PlanDiffRow>(codes.Count);
        foreach (var code in codes)
        {
            draftLookup.TryGetValue(code, out var draftRow);
            currentLookup.TryGetValue(code, out var currentRow);

            diffs.Add(new PlanDiffRow(
                code,
                draftRow?.PlannedStart,
                draftRow?.PlannedDue,
                currentRow?.PlannedStart,
                currentRow?.PlannedDue));
        }

        return diffs;
    }
}
