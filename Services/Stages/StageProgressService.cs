using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services;

public class StageProgressService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public StageProgressService(ApplicationDbContext db, IClock clock, IAuditService audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task UpdateStageStatusAsync(
        int projectId,
        string stageCode,
        StageStatus newStatus,
        DateOnly? effectiveDate,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            throw new ArgumentException("A valid stage code is required.", nameof(stageCode));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(userId));
        }

        var stage = await _db.ProjectStages
            .SingleOrDefaultAsync(
                s => s.ProjectId == projectId && s.StageCode == stageCode,
                cancellationToken)
            ?? throw new InvalidOperationException($"Stage {stageCode} was not found for project {projectId}.");

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var resolvedDate = effectiveDate ?? today;

        if (newStatus == StageStatus.InProgress && stage.Status != StageStatus.InProgress)
        {
            stage.ActualStart ??= resolvedDate;
        }

        if (newStatus == StageStatus.Completed)
        {
            stage.ActualStart ??= resolvedDate;
            stage.CompletedOn = effectiveDate ?? stage.CompletedOn ?? resolvedDate;
        }
        else if (newStatus == StageStatus.NotStarted)
        {
            stage.ActualStart = null;
            stage.CompletedOn = null;
        }
        else if (newStatus != StageStatus.Completed && stage.Status == StageStatus.Completed)
        {
            stage.CompletedOn = null;
        }

        stage.Status = newStatus;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Stages.StageStatusChanged",
            userId: userId,
            data: new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["StageCode"] = stageCode,
                ["NewStatus"] = newStatus.ToString(),
                ["EffectiveDate"] = (effectiveDate ?? resolvedDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
    }
}
