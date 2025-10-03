using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;

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
            var pncApplicable = await ResolvePncApplicabilityAsync(projectId, cancellationToken);
            await CompleteWithCascadeAsync(
                projectId,
                stage,
                resolvedDate,
                effectiveDate,
                userId,
                pncApplicable,
                cancellationToken);
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
        bool pncApplicable,
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

        foreach (var predecessorCode in StageDependencies.RequiredPredecessors(stage.StageCode, pncApplicable))
        {
            await AutoCompleteStageAndDependenciesAsync(
                projectId,
                predecessorCode,
                cascadeDate,
                stage.StageCode,
                visited,
                autoCompleted,
                pncApplicable,
                ct);
        }

        var completedOnDate = stage.CompletedOn ?? resolvedDate;
        var autoStart = await AutoStartNextStageAsync(projectId, stage, completedOnDate, pncApplicable, ct);

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

        if (autoStart.HasValue && autoStart.Value.Stage is { } nextStage)
        {
            var startDate = autoStart.Value.StartDate;
            await _audit.LogAsync(
                "Stages.StageAutoStarted",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["StageCode"] = nextStage.StageCode,
                    ["TriggeredBy"] = stage.StageCode,
                    ["StartDate"] = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                });
        }

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
        bool pncApplicable,
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

        foreach (var predecessorCode in StageDependencies.RequiredPredecessors(stage.StageCode, pncApplicable))
        {
            await AutoCompleteStageAndDependenciesAsync(
                projectId,
                predecessorCode,
                resolvedDate,
                triggeredBy,
                visited,
                autoCompleted,
                pncApplicable,
                ct);
        }
    }

    private async Task<(ProjectStage Stage, DateOnly StartDate)?> AutoStartNextStageAsync(
        int projectId,
        ProjectStage completedStage,
        DateOnly completedOn,
        bool pncApplicable,
        CancellationToken ct)
    {
        var settings = await _db.ProjectScheduleSettings.SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
        if (settings is null || settings.AnchorStart is null)
        {
            return null;
        }

        var stages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(ct);

        var ordered = stages
            .OrderBy(s => StageOrderValue(s.StageCode))
            .ThenBy(s => s.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = ordered.FindIndex(s => string.Equals(s.StageCode, completedStage.StageCode, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= ordered.Count)
        {
            return null;
        }

        var nextStage = ordered[index + 1];
        if (string.IsNullOrWhiteSpace(nextStage.StageCode))
        {
            return null;
        }
        if (nextStage.Status != StageStatus.NotStarted)
        {
            return null;
        }

        foreach (var dependency in StageDependencies.RequiredPredecessors(nextStage.StageCode, pncApplicable))
        {
            var depStage = stages.FirstOrDefault(s => string.Equals(s.StageCode, dependency, StringComparison.OrdinalIgnoreCase));
            if (depStage is null || depStage.Status is not StageStatus.Completed and not StageStatus.Skipped)
            {
                return null;
            }
        }

        var startDate = settings.NextStageStartPolicy == NextStageStartPolicies.SameDay
            ? completedOn
            : await ResolveNextWorkingDayAsync(settings, completedOn, ct);

        nextStage.Status = StageStatus.InProgress;
        nextStage.ActualStart ??= startDate;
        nextStage.IsAutoCompleted = false;
        nextStage.AutoCompletedFromCode = null;
        nextStage.RequiresBackfill = false;

        return (nextStage, startDate);
    }

    private async Task<DateOnly> ResolveNextWorkingDayAsync(ProjectScheduleSettings settings, DateOnly completedOn, CancellationToken ct)
    {
        if (settings.NextStageStartPolicy == NextStageStartPolicies.SameDay)
        {
            return completedOn;
        }

        var holidays = await _db.Holidays
            .AsNoTracking()
            .Select(h => h.Date)
            .ToListAsync(ct);

        var calendar = new WorkingCalendar(holidays, settings.IncludeWeekends, settings.SkipHolidays);
        return calendar.NextWorkingDay(completedOn);
    }

    private async Task<bool> ResolvePncApplicabilityAsync(int projectId, CancellationToken ct)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.ActivePlanVersionNo })
            .SingleOrDefaultAsync(ct);

        if (project is null || !project.ActivePlanVersionNo.HasValue)
        {
            return true;
        }

        var plan = await _db.PlanVersions
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.VersionNo == project.ActivePlanVersionNo.Value)
            .Select(p => new { p.PncApplicable })
            .SingleOrDefaultAsync(ct);

        return plan?.PncApplicable ?? true;
    }

    private static int StageOrderValue(string? stageCode)
    {
        if (stageCode is null)
        {
            return int.MaxValue;
        }

        var index = Array.IndexOf(StageCodes.All, stageCode);
        return index >= 0 ? index : int.MaxValue;
    }
}
