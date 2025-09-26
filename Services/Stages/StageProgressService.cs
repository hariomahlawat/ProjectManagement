using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services;

public class StageProgressService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ProjectFactsReadService _factsRead;

    public StageProgressService(ApplicationDbContext db, IClock clock, IAuditService audit, ProjectFactsReadService factsRead)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _factsRead = factsRead;
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

        if (newStatus == StageStatus.Completed)
        {
            await CompleteWithCascadeAsync(projectId, stage, resolvedDate, effectiveDate, userId, cancellationToken);
            return;
        }

        if (newStatus == StageStatus.InProgress && stage.Status != StageStatus.InProgress)
        {
            stage.ActualStart ??= resolvedDate;
            stage.IsAutoCompleted = false;
            stage.AutoCompletedFromCode = null;
            stage.RequiresBackfill = false;
        }
        else if (newStatus != StageStatus.Completed && stage.Status == StageStatus.Completed)
        {
            stage.CompletedOn = null;
            stage.IsAutoCompleted = false;
            stage.AutoCompletedFromCode = null;
            stage.RequiresBackfill = false;
        }

        if (newStatus == StageStatus.NotStarted)
        {
            stage.ActualStart = null;
            stage.CompletedOn = null;
            stage.IsAutoCompleted = false;
            stage.AutoCompletedFromCode = null;
            stage.RequiresBackfill = false;
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
                ["EffectiveDate"] = resolvedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
    }

    private async Task CompleteWithCascadeAsync(
        int projectId,
        ProjectStage stage,
        DateOnly resolvedDate,
        DateOnly? explicitDate,
        string userId,
        CancellationToken ct)
    {
        if (!await _factsRead.HasRequiredFactsAsync(projectId, stage.StageCode, ct))
        {
            throw new InvalidOperationException($"Required information for stage {stage.StageCode} is missing and must be captured before completion.");
        }

        stage.Status = StageStatus.Completed;
        stage.ActualStart ??= resolvedDate;
        stage.CompletedOn = explicitDate ?? stage.CompletedOn ?? resolvedDate;
        stage.IsAutoCompleted = false;
        stage.AutoCompletedFromCode = null;
        stage.RequiresBackfill = false;

        var autoCompleted = new List<ProjectStage>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cascadeDate = stage.CompletedOn ?? resolvedDate;

        foreach (var predecessorCode in StageDependencies.RequiredPredecessors(stage.StageCode))
        {
            await AutoCompleteStageAndDependenciesAsync(
                projectId,
                predecessorCode,
                cascadeDate,
                stage.StageCode,
                visited,
                autoCompleted,
                ct);
        }

        await _db.SaveChangesAsync(ct);

        var data = new Dictionary<string, string?>
        {
            ["ProjectId"] = projectId.ToString(),
            ["StageCode"] = stage.StageCode,
            ["NewStatus"] = StageStatus.Completed.ToString(),
            ["EffectiveDate"] = (explicitDate ?? resolvedDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["AutoCompleted"] = autoCompleted.Count == 0 ? null : string.Join(",", autoCompleted.Select(s => s.StageCode))
        };

        await _audit.LogAsync("Stages.StageStatusChanged", userId: userId, data: data);

        foreach (var auto in autoCompleted)
        {
            await _audit.LogAsync(
                "Stages.StageAutoCompleted",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(),
                    ["StageCode"] = auto.StageCode,
                    ["TriggeredBy"] = stage.StageCode,
                    ["CompletedOn"] = auto.CompletedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                });
        }
    }

    private async Task AutoCompleteStageAndDependenciesAsync(
        int projectId,
        string stageCode,
        DateOnly resolvedDate,
        string triggeredBy,
        ISet<string> visited,
        ICollection<ProjectStage> autoCompleted,
        CancellationToken ct)
    {
        if (!visited.Add(stageCode))
        {
            return;
        }

        var stage = await _db.ProjectStages
            .SingleOrDefaultAsync(s => s.ProjectId == projectId && s.StageCode == stageCode, ct);

        if (stage is null)
        {
            return;
        }

        if (stage.Status != StageStatus.Completed)
        {
            stage.Status = StageStatus.Completed;
            stage.ActualStart ??= resolvedDate;
            stage.CompletedOn ??= resolvedDate;
            stage.IsAutoCompleted = true;
            stage.AutoCompletedFromCode = triggeredBy;

            var hasFacts = await _factsRead.HasRequiredFactsAsync(projectId, stage.StageCode, ct);
            stage.RequiresBackfill = !hasFacts;
            autoCompleted.Add(stage);
        }
        else if (stage.IsAutoCompleted && stage.CompletedOn is null)
        {
            stage.CompletedOn = resolvedDate;
        }

        foreach (var predecessorCode in StageDependencies.RequiredPredecessors(stage.StageCode))
        {
            await AutoCompleteStageAndDependenciesAsync(
                projectId,
                predecessorCode,
                resolvedDate,
                triggeredBy,
                visited,
                autoCompleted,
                ct);
        }
    }
}
