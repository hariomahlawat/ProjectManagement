using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Stages;

public sealed class PlanCompareService
{
    private readonly ApplicationDbContext _db;

    public PlanCompareService(ApplicationDbContext db)
    {
        _db = db;
    }

    public sealed record PlanDiffRow(
        string Code,
        DateOnly? NewStart,
        DateOnly? NewDue,
        DateOnly? OldStart,
        DateOnly? OldDue);

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
}
