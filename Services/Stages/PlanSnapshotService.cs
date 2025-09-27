using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Services.Stages;

public sealed class PlanSnapshotService
{
    private readonly ApplicationDbContext _db;

    public PlanSnapshotService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<int> CreateSnapshotAsync(int projectId, string userId, CancellationToken ct = default)
    {
        var stages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.StageCode)
            .ToListAsync(ct);

        var snapshot = new ProjectPlanSnapshot
        {
            ProjectId = projectId,
            TakenAt = DateTimeOffset.UtcNow,
            TakenByUserId = userId
        };

        _db.ProjectPlanSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        var rows = stages.Select(stage => new ProjectPlanSnapshotRow
        {
            SnapshotId = snapshot.Id,
            StageCode = stage.StageCode,
            PlannedStart = stage.PlannedStart,
            PlannedDue = stage.PlannedDue
        });

        await _db.ProjectPlanSnapshotRows.AddRangeAsync(rows, ct);
        await _db.SaveChangesAsync(ct);

        return snapshot.Id;
    }
}
