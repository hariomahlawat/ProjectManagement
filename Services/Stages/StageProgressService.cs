using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Stages;

using ProjectManagement.Services.Scheduling;

namespace ProjectManagement.Services;

public class StageProgressService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ProjectFactsReadService _factsRead;
    private readonly IStageNotificationService _stageNotifications;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;

    public StageProgressService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        ProjectFactsReadService factsRead,
        IStageNotificationService stageNotifications,
        IProjectStageWorkflowPolicy workflowPolicy,
        IOfficeCalendarService? officeCalendar = null,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _factsRead = factsRead ?? throw new ArgumentNullException(nameof(factsRead));
        _stageNotifications = stageNotifications ?? throw new ArgumentNullException(nameof(stageNotifications));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db);
    }

    public Task UpdateStageStatusAsync(
        int projectId,
        string stageCode,
        StageStatus newStatus,
        DateOnly? effectiveDate,
        string userId,
        CancellationToken cancellationToken = default)
        => UpdateStageStatusAsync(
            projectId,
            stageCode,
            newStatus,
            effectiveDate,
            explicitStartDate: null,
            preserveBlankStartOnCompletion: false,
            userId: userId,
            cancellationToken: cancellationToken);

    public async Task UpdateStageStatusAsync(
        int projectId,
        string stageCode,
        StageStatus newStatus,
        DateOnly? effectiveDate,
        DateOnly? explicitStartDate,
        bool preserveBlankStartOnCompletion,
        string userId,
        CancellationToken cancellationToken = default,
        bool recordHistory = true)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            throw new ArgumentException("A valid stage code is required.", nameof(stageCode));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(userId));
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        await _arppIpaStageAuthority.EnsureManualLifecycleMutationAllowedAsync(
            projectId,
            stageCode,
            cancellationToken);

        var stage = await _db.ProjectStages
            .Include(s => s.Project)
            .SingleOrDefaultAsync(
                s => s.ProjectId == projectId && s.StageCode == stageCode,
                cancellationToken)
            ?? throw new InvalidOperationException($"Stage {stageCode} was not found for project {projectId}.");

        var previousStatus = stage.Status;
        var previousStart = stage.ActualStart;
        var previousCompletion = stage.CompletedOn;

        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
        var resolvedDate = effectiveDate ?? today;
        if (effectiveDate > today || explicitStartDate > today)
        {
            throw new InvalidOperationException("Actual dates cannot be in the future under the operational IST date.");
        }
        if (newStatus == StageStatus.Skipped &&
            (stage.ActualStart.HasValue || stage.CompletedOn.HasValue || stage.Status is StageStatus.InProgress or StageStatus.Completed))
        {
            throw new InvalidOperationException("A stage that actually started or completed cannot be skipped. Correct its factual state through an authorised path first.");
        }

        if (newStatus == StageStatus.Completed)
        {
            await CompleteWithCascadeAsync(
                projectId,
                stage,
                resolvedDate,
                effectiveDate,
                explicitStartDate,
                preserveBlankStartOnCompletion,
                previousStatus,
                recordHistory,
                userId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var nextStart = newStatus == StageStatus.InProgress
            ? stage.ActualStart ?? explicitStartDate ?? effectiveDate
            : stage.ActualStart;
        if (newStatus == StageStatus.InProgress && !nextStart.HasValue)
        {
            throw new InvalidOperationException("ActualStart is required before a stage can become InProgress. Supply and confirm a start date.");
        }
        if ((newStatus is StageStatus.InProgress or StageStatus.Blocked) && nextStart > today)
        {
            throw new InvalidOperationException("Actual start cannot be in the future under the operational IST date.");
        }
        stage.ActualStart = newStatus is StageStatus.NotStarted or StageStatus.Skipped ? null : nextStart;
        stage.CompletedOn = null;
        stage.IsAutoCompleted = false;
        stage.AutoCompletedFromCode = null;
        stage.RequiresBackfill = false;

        stage.Status = newStatus;
        if (recordHistory)
        {
            RecordChange(stage, previousStatus, previousStart, previousCompletion, userId, "Applied");
        }

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

        await transaction.CommitAsync(cancellationToken);

        if (stage.Project is not null)
        {
            await _stageNotifications.NotifyStageStatusChangedAsync(stage, stage.Project, previousStatus, userId, cancellationToken);
        }
    }

    private async Task CompleteWithCascadeAsync(
        int projectId,
        ProjectStage stage,
        DateOnly resolvedDate,
        DateOnly? explicitDate,
        DateOnly? explicitStartDate,
        bool preserveBlankStartOnCompletion,
        StageStatus previousStatus,
        bool recordHistory,
        string userId,
        CancellationToken ct)
    {
        if (!await _factsRead.HasRequiredFactsAsync(projectId, stage.StageCode, ct))
        {
            throw new InvalidOperationException($"Required information for stage {stage.StageCode} is missing and must be captured before completion.");
        }

        var workflow = await _workflowPolicy.GetAsync(projectId, ct);
        var completionDate = explicitDate ?? stage.CompletedOn ?? resolvedDate;

        var previousStart = stage.ActualStart;
        var previousCompletion = stage.CompletedOn;
        var selectedStart = explicitStartDate ?? previousStart;
        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
        if (completionDate > today || selectedStart > today)
        {
            throw new InvalidOperationException("Actual dates cannot be in the future under the operational IST date.");
        }
        if (selectedStart.HasValue && completionDate < selectedStart.Value)
        {
            throw new InvalidOperationException("Completion date cannot be before the selected start date.");
        }
        stage.Status = StageStatus.Completed;
        stage.ActualStart = selectedStart;
        stage.CompletedOn = completionDate;
        stage.IsAutoCompleted = false;
        stage.AutoCompletedFromCode = null;
        stage.RequiresBackfill = false;
        var autoCompleted = new List<ProjectStage>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cascadeDate = stage.CompletedOn ?? resolvedDate;

        foreach (var predecessorCode in workflow.RequiredPredecessors(stage.StageCode))
        {
            await AutoCompleteStageAndDependenciesAsync(
                projectId,
                predecessorCode,
                cascadeDate,
                stage.StageCode,
                visited,
                autoCompleted,
                workflow,
                ct);
        }

        // A calculated next-stage start is a suggestion, not observed commencement.
        if (recordHistory)
        {
            RecordChange(stage, previousStatus, previousStart, previousCompletion, userId, "Applied");
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

        if (stage.Project is null)
        {
            stage.Project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
        }

        if (stage.Project is not null)
        {
            await _stageNotifications.NotifyStageStatusChangedAsync(stage, stage.Project, previousStatus, userId, ct);
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
        ProjectStageWorkflowSnapshot workflow,
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

        // Published authority protects IPA's stored facts, not the ancestry behind it.
        // Required upstream stages must still participate in a downstream cascade.
        var hasPublishedIpaAuthority = string.Equals(stage.StageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase)
            && await _arppIpaStageAuthority.ResolveAsync(projectId, ct) is not null;
        var beforeStatus = stage.Status;
        var beforeStart = stage.ActualStart;
        var beforeCompletion = stage.CompletedOn;
        var isOptional = workflow.OptionalStageCodes.Contains(stage.StageCode);

        if (!hasPublishedIpaAuthority && isOptional && stage.Status == StageStatus.NotStarted)
        {
            if (stage.ActualStart.HasValue || stage.CompletedOn.HasValue)
            {
                throw new InvalidOperationException("An optional stage with recorded actual evidence cannot be skipped automatically. Correct its factual state through an authorised path first.");
            }
            stage.Status = StageStatus.Skipped;
            stage.ActualStart = null;
            stage.CompletedOn = null;
            stage.IsAutoCompleted = false;
            stage.AutoCompletedFromCode = null;
            stage.RequiresBackfill = false;
        }
        else if (!hasPublishedIpaAuthority && isOptional && stage.Status is not StageStatus.Completed and not StageStatus.Skipped)
        {
            throw new InvalidOperationException($"Complete or skip {stage.StageCode} before completing {triggeredBy}.");
        }
        else if (!hasPublishedIpaAuthority && !isOptional && stage.Status is not StageStatus.Completed and not StageStatus.Skipped)
        {
            stage.Status = StageStatus.Completed;
            // Completion of a successor does not establish either predecessor date.
            stage.IsAutoCompleted = true;
            stage.AutoCompletedFromCode = triggeredBy;

            var hasFacts = await _factsRead.HasRequiredFactsAsync(projectId, stage.StageCode, ct);
            stage.RequiresBackfill = !stage.CompletedOn.HasValue || !hasFacts;
            autoCompleted.Add(stage);
        }

        if (stage.Status != beforeStatus || stage.ActualStart != beforeStart || stage.CompletedOn != beforeCompletion)
        {
            RecordChange(stage, beforeStatus, beforeStart, beforeCompletion, "PRISM system", "AutoBackfill");
        }
        foreach (var predecessorCode in workflow.RequiredPredecessors(stage.StageCode))
        {
            await AutoCompleteStageAndDependenciesAsync(
                projectId,
                predecessorCode,
                resolvedDate,
                triggeredBy,
                visited,
                autoCompleted,
                workflow,
                ct);
        }
    }

    private void RecordChange(ProjectStage stage, StageStatus previousStatus, DateOnly? previousStart,
        DateOnly? previousCompletion, string userId, string action)
    {
        _db.StageChangeLogs.Add(new StageChangeLog
        {
            ProjectId = stage.ProjectId, StageCode = stage.StageCode, Action = action,
            FromStatus = previousStatus.ToString(), ToStatus = stage.Status.ToString(),
            FromActualStart = previousStart, ToActualStart = stage.ActualStart,
            FromCompletedOn = previousCompletion, ToCompletedOn = stage.CompletedOn,
            UserId = userId, At = _clock.UtcNow,
            Note = "Lifecycle transition; scheduling suggestions are not recorded as actual dates."
        });
    }
}
