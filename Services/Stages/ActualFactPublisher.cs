using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Publishes validated Actual-date facts to <see cref="ProjectStage"/> together
/// with their mandatory <see cref="StageChangeLog"/> history. It only stages
/// tracked changes: it never saves, audits or notifies, so the caller's
/// transaction commits the facts and their history as one unit.
/// </summary>
internal static class ActualFactPublisher
{
    internal const string ActualsUpdatedAction = "ActualsUpdated";
    internal const string BackfillAction = "Backfill";

    /// <summary>
    /// Loads every stage of the project for an authoritative Actual write. On
    /// PostgreSQL the rows are locked (FOR UPDATE) so writers that do not take
    /// the project gate, such as ARPP synchronization, cannot change them
    /// between validation and publication. The caller must hold the project
    /// gate inside an open transaction.
    /// </summary>
    internal static Task<List<ProjectStage>> LoadProjectStagesForUpdateAsync(
        ApplicationDbContext db,
        int projectId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (db.Database.IsNpgsql())
        {
            return db.ProjectStages
                // A fixed lock order avoids lock-order deadlocks with other multi-row writers.
                .FromSqlInterpolated($"SELECT * FROM \"ProjectStages\" WHERE \"ProjectId\" = {projectId} ORDER BY \"Id\" FOR UPDATE")
                .ToListAsync(cancellationToken);
        }

        return db.ProjectStages
            .Where(stage => stage.ProjectId == projectId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Applies one validated change to its tracked stage and returns the history
    /// row describing exactly what was published.
    /// </summary>
    internal static StageChangeLog Apply(
        ProjectStage stage,
        ActualStageResolution change,
        string userId,
        DateTimeOffset at,
        string action,
        string note)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(change);

        if (!string.Equals(stage.StageCode, change.StageCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The validated change does not belong to this stage.");
        }

        var log = new StageChangeLog
        {
            ProjectId = stage.ProjectId,
            StageCode = stage.StageCode,
            Action = action,
            FromStatus = stage.Status.ToString(),
            ToStatus = change.Final.Status.ToString(),
            FromActualStart = stage.ActualStart,
            ToActualStart = change.Final.ActualStart,
            FromCompletedOn = stage.CompletedOn,
            ToCompletedOn = change.Final.CompletedOn,
            UserId = userId,
            At = at,
            Note = note
        };

        // Status changes only in the documented ARPP-effective IPA case, where an
        // explicit correction publishes the published lifecycle state.
        stage.Status = change.Final.Status;
        stage.ActualStart = change.Final.ActualStart;
        stage.CompletedOn = change.Final.CompletedOn;
        stage.RequiresBackfill = change.Final.RequiresBackfill;
        stage.IsAutoCompleted = false;
        stage.AutoCompletedFromCode = null;

        return log;
    }
}
