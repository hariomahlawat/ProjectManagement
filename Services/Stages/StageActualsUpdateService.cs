using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Stages;

// SECTION: Stage actuals updater
public sealed class StageActualsUpdateService
{
    private const string PendingDecisionStatus = "Pending";
    private const string ActualsUpdatedLogAction = "ActualsUpdated";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<StageActualsUpdateService> _logger;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;

    public StageActualsUpdateService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        ILogger<StageActualsUpdateService> logger,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null,
        IProjectStageWorkflowPolicy? workflowPolicy = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db);
        _workflowPolicy = workflowPolicy ?? new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
    }

    public async Task<StageActualsUpdateResult> UpdateAsync(
        ActualsEditInput input,
        string userId,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(userId));
        }

        if (input.ProjectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input.ProjectId));
        }

        if (input.Rows is null || input.Rows.Count == 0)
        {
            throw new StageActualsValidationException(new[] { "At least one stage update is required." });
        }

        var normalized = input.Rows
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.StageCode))
            .GroupBy(r => r.StageCode!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new StageActualsValidationException(new[] { "At least one stage update is required." });
        }

        var codes = normalized.Select(r => r.StageCode).ToArray();

        var allProjectStages = await _db.ProjectStages
            .Where(s => s.ProjectId == input.ProjectId)
            .ToListAsync(cancellationToken);

        var stageLookup = allProjectStages
            .Where(s => codes.Contains(s.StageCode))
            .ToDictionary(s => s.StageCode!, StringComparer.OrdinalIgnoreCase);

        if (stageLookup.Count != codes.Length)
        {
            var missing = codes.Where(code => !stageLookup.ContainsKey(code)).ToArray();
            throw new StageActualsNotFoundException(missing);
        }

        var ipaAuthority = codes.Any(code =>
                string.Equals(code, StageCodes.IPA, StringComparison.OrdinalIgnoreCase))
            ? await _arppIpaStageAuthority.ResolveAsync(input.ProjectId, cancellationToken)
            : null;

        var workflow = await _workflowPolicy.GetAsync(input.ProjectId, cancellationToken);
        var submittedByCode = normalized.ToDictionary(row => row.StageCode, StringComparer.OrdinalIgnoreCase);
        var projectedStages = allProjectStages
            .Select(stage =>
            {
                var projectedCompletion = stage.CompletedOn;
                if (submittedByCode.TryGetValue(stage.StageCode, out var submittedRow))
                {
                    var isArppManaged = ipaAuthority is not null
                        && string.Equals(stage.StageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase);
                    var submittedStart = submittedRow.ClearActualStart
                        ? null
                        : submittedRow.ActualStart ?? stage.ActualStart;
                    if (isArppManaged && submittedStart != stage.ActualStart)
                    {
                        projectedCompletion = ipaAuthority!.IssueDate;
                    }
                    else if (!isArppManaged && submittedRow.CompletedOn.HasValue)
                    {
                        projectedCompletion = submittedRow.CompletedOn;
                    }
                }

                return new ProjectStage
                {
                    StageCode = stage.StageCode,
                    Status = stage.Status,
                    CompletedOn = projectedCompletion
                };
            })
            .ToArray();

        var lockedStages = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(r => r.ProjectId == input.ProjectId && r.DecisionStatus == PendingDecisionStatus && codes.Contains(r.StageCode))
            .Select(r => r.StageCode)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
        var now = _clock.UtcNow;
        var validationErrors = new List<string>();
        var changes = new List<StageActualChange>();

        foreach (var row in normalized)
        {
            var stage = stageLookup[row.StageCode];
            var start = row.ActualStart;
            var completed = row.CompletedOn;
            var isArppManagedIpa = ipaAuthority is not null &&
                                   string.Equals(stage.StageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase);

            if (isArppManagedIpa && completed.HasValue && completed != ipaAuthority!.IssueDate)
            {
                validationErrors.Add($"{row.StageCode}: Completion date is controlled by the published ARPP record and cannot be edited here.");
                continue;
            }

            if (row.ClearActualStart && start.HasValue)
            {
                validationErrors.Add($"{row.StageCode}: Supply a start date or explicitly clear it, not both.");
                continue;
            }

            if (row.ClearActualStart && stage.Status == StageStatus.InProgress)
            {
                validationErrors.Add($"{row.StageCode}: Actual start is required while the stage is InProgress. Transition the stage before clearing it.");
                continue;
            }

            // A missing value is not a clear command, including for managed IPA.
            // The additive flag distinguishes intentional clearing from older or omitted fields.
            var proposedStart = row.ClearActualStart ? null : start ?? stage.ActualStart;
            var startChanged = proposedStart != stage.ActualStart;
            // A valid requested start correction must retain the published source's
            // completion, even if the stored value is stale. Unchanged rows are not
            // an implicit reconciliation job for pre-existing data.
            var proposedCompleted = isArppManagedIpa
                ? (startChanged ? ipaAuthority!.IssueDate : stage.CompletedOn)
                : completed ?? stage.CompletedOn;
            var isChanged = startChanged || proposedCompleted != stage.CompletedOn;

            if (!isChanged)
            {
                continue;
            }

            if (stage.Status is not StageStatus.InProgress and not StageStatus.Completed)
            {
                validationErrors.Add($"{row.StageCode}: Actual dates can only be edited when the stage is InProgress or Completed.");
                continue;
            }

            if (stage.Status == StageStatus.InProgress)
            {
                if (!proposedStart.HasValue)
                {
                    validationErrors.Add($"{row.StageCode}: Actual start is required while the stage is InProgress.");
                }

                if (proposedCompleted.HasValue)
                {
                    validationErrors.Add($"{row.StageCode}: Use the stage-transition workflow to complete this stage; an InProgress stage cannot carry a completion date.");
                }
            }

            if (proposedStart is not null && proposedStart > today)
            {
                validationErrors.Add($"{row.StageCode}: Start date cannot be in the future.");
            }

            if (proposedCompleted is not null && proposedCompleted > today)
            {
                validationErrors.Add($"{row.StageCode}: Completion date cannot be in the future.");
            }

            if (proposedStart is not null && proposedCompleted is not null && proposedStart > proposedCompleted)
            {
                validationErrors.Add($"{row.StageCode}: Completion date must be on or after the start date.");
            }

            var chronologyDate = proposedStart ?? proposedCompleted;
            var chronologyLabel = proposedStart.HasValue ? "Start date" : "Completion date";
            var startBoundary = StageDateSuggestionResolver.Resolve(workflow, projectedStages, row.StageCode);
            if (chronologyDate.HasValue
                && startBoundary.EarliestAllowedStartDate.HasValue
                && chronologyDate.Value < startBoundary.EarliestAllowedStartDate.Value)
            {
                var predecessorName = string.IsNullOrWhiteSpace(startBoundary.SourceStageName)
                    ? "the effective predecessor"
                    : startBoundary.SourceStageName;
                validationErrors.Add(
                    $"{row.StageCode}: {chronologyLabel} must be on or after " +
                    $"{startBoundary.EarliestAllowedStartDate.Value:dd MMM yyyy}, when {predecessorName} was completed. " +
                    "Same-day commencement is permitted.");
            }

            var updatedStart = proposedStart;
            var updatedCompleted = proposedCompleted;

            changes.Add(new StageActualChange(stage, updatedStart, updatedCompleted));
        }

        if (lockedStages.Length > 0)
        {
            var lockedLookup = new HashSet<string>(lockedStages, StringComparer.OrdinalIgnoreCase);
            var blocked = changes
                .Where(c => lockedLookup.Contains(c.Stage.StageCode))
                .Select(c => c.Stage.StageCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (blocked.Length > 0)
            {
                throw new StageActualsConflictException(blocked);
            }
        }

        if (validationErrors.Count > 0)
        {
            throw new StageActualsValidationException(validationErrors);
        }

        if (changes.Count == 0)
        {
            return StageActualsUpdateResult.NoChanges();
        }

        var auditData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectId"] = input.ProjectId.ToString(CultureInfo.InvariantCulture),
            ["UpdatedCount"] = changes.Count.ToString(CultureInfo.InvariantCulture)
        };

        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            var prefix = $"Stage[{i}]";
            auditData[$"{prefix}.Code"] = change.Stage.StageCode;
            auditData[$"{prefix}.From.Start"] = FormatDate(change.Stage.ActualStart);
            auditData[$"{prefix}.From.Completed"] = FormatDate(change.Stage.CompletedOn);
            auditData[$"{prefix}.To.Start"] = FormatDate(change.NewStart);
            auditData[$"{prefix}.To.Completed"] = FormatDate(change.NewCompleted);
        }

        foreach (var change in changes)
        {
            var stage = change.Stage;
            var log = new StageChangeLog
            {
                ProjectId = stage.ProjectId,
                StageCode = stage.StageCode,
                Action = ActualsUpdatedLogAction,
                FromStatus = stage.Status.ToString(),
                ToStatus = stage.Status.ToString(),
                FromActualStart = stage.ActualStart,
                ToActualStart = change.NewStart,
                FromCompletedOn = stage.CompletedOn,
                ToCompletedOn = change.NewCompleted,
                UserId = userId,
                At = now,
                Note = "Actual dates updated via bulk editor."
            };

            stage.ActualStart = change.NewStart;
            stage.CompletedOn = change.NewCompleted;

            // SECTION: Completed stage backfill state
            if (stage.Status == StageStatus.Completed)
            {
                var isArppManagedIpa = ipaAuthority is not null &&
                                       string.Equals(stage.StageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase);
                stage.RequiresBackfill = isArppManagedIpa
                    ? false
                    : !stage.CompletedOn.HasValue;
            }

            stage.IsAutoCompleted = false;
            stage.AutoCompletedFromCode = null;

            await _db.StageChangeLogs.AddAsync(log, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Projects.ActualsUpdated",
            userId: userId,
            userName: userName,
            data: auditData);

        _logger.LogInformation(
            "Updated actual dates for {Count} stage(s) in project {ProjectId} by user {UserId}",
            changes.Count,
            input.ProjectId,
            userId);

        var updatedCodes = changes.Select(c => c.Stage.StageCode).ToArray();
        return new StageActualsUpdateResult(updatedCodes.Length, updatedCodes);
    }

    // SECTION: Helpers
    private static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// SECTION: Result records and exceptions
public sealed record StageActualsUpdateResult(int UpdatedCount, IReadOnlyList<string> StageCodes)
{
    public static StageActualsUpdateResult NoChanges() => new(0, Array.Empty<string>());
}

internal sealed record StageActualChange(ProjectStage Stage, DateOnly? NewStart, DateOnly? NewCompleted);

public sealed class StageActualsValidationException : Exception
{
    public StageActualsValidationException(IReadOnlyList<string> errors)
        : base("Actuals input failed validation.")
    {
        Errors = errors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}

public sealed class StageActualsConflictException : Exception
{
    public StageActualsConflictException(IReadOnlyList<string> stageCodes)
        : base("One or more stages cannot be edited while pending approval.")
    {
        StageCodes = stageCodes ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> StageCodes { get; }
}

public sealed class StageActualsNotFoundException : Exception
{
    public StageActualsNotFoundException(IReadOnlyList<string> missing)
        : base("One or more stages were not found for this project.")
    {
        MissingStageCodes = missing ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> MissingStageCodes { get; }
}
