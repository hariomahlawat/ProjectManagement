using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Execution;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Phase D-1d: warning-only Actual chronology observability for lifecycle writers
/// (direct apply, approved lifecycle requests, historical reconstruction). A
/// lifecycle transition is not an Actual correction, so these writers never block
/// on chronology; they report what their committed state introduced.
///
/// The shared validator lists every chronology violation in the project's stage
/// facts before and after the writer's mutation, with the same workflow, ARPP
/// authority and operational date. Violations present only afterwards are
/// reported; violations already in the record are existing data, as they are for
/// Actual corrections, and are not repeated.
///
/// <c>Stages.ActualChronologyWarning</c> is written only once the writer's managed
/// transaction has committed. A writer nested in a transaction its caller owns
/// cannot be confirmed as committed, so it reports
/// <c>Stages.ActualChronologyWarningUnconfirmed</c> instead and never claims a
/// committed change.
/// </summary>
internal sealed class LifecycleChronologyObserver
{
    internal const string WarningEventName = "Stages.ActualChronologyWarning";
    internal const string FailureEventName = "Stages.ActualChronologyWarningFailed";
    internal const string UnconfirmedEventName = "Stages.ActualChronologyWarningUnconfirmed";
    internal static readonly EventId WarningEvent = new(4101, WarningEventName);
    internal static readonly EventId FailureEvent = new(4102, FailureEventName);
    internal static readonly EventId UnconfirmedEvent = new(4103, UnconfirmedEventName);

    private readonly ApplicationDbContext _db;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;
    private readonly ILogger _logger;

    public LifecycleChronologyObserver(
        ApplicationDbContext db,
        IProjectStageWorkflowPolicy workflowPolicy,
        IArppIpaStageAuthorityService arppIpaStageAuthority,
        ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? throw new ArgumentNullException(nameof(arppIpaStageAuthority));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The Actual facts of tracked stage rows, captured before the writer mutates them.</summary>
    public static IReadOnlyList<ActualStageState> Snapshot(IEnumerable<ProjectStage> stages) =>
        stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .Select(ActualStageState.From)
            .ToArray();

    /// <summary>
    /// Evaluates the final tracked state inside the writer's transaction, after its
    /// last save and before its commit, and arranges for any finding to be logged
    /// once that transaction commits. Never throws for an observability failure.
    /// </summary>
    public async Task ObserveBeforeCommitAsync(
        RelationalTransactionScope transaction,
        string operation,
        int projectId,
        string stageCode,
        IReadOnlyList<ActualStageState> before,
        IEnumerable<ProjectStage> after,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        IReadOnlyList<ActualValidationIssue> introduced;
        Exception? failure = null;
        try
        {
            introduced = await FindIntroducedViolationsAsync(projectId, before, Snapshot(after), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation of the lifecycle request itself, not an observability failure.
            throw;
        }
        catch (Exception exception)
        {
            failure = exception;
            introduced = Array.Empty<ActualValidationIssue>();
        }

        if (failure is null && introduced.Count == 0)
        {
            return;
        }

        if (!transaction.CanRegisterAfterCommit)
        {
            // The caller owns the commit and may still roll back: report the
            // observation now, without claiming the change was committed.
            Report(confirmed: false, operation, projectId, stageCode, introduced, failure);
            return;
        }

        try
        {
            // Report only what was actually committed: a rolled-back write logs nothing.
            transaction.RegisterAfterCommit(_ =>
            {
                Report(confirmed: true, operation, projectId, stageCode, introduced, failure);
                return Task.CompletedTask;
            });
        }
        catch (InvalidOperationException exception)
        {
            // Not reachable for an active managed scope; never let it block the write.
            Report(confirmed: false, operation, projectId, stageCode, introduced, failure ?? exception);
        }
    }

    private void Report(
        bool confirmed,
        string operation,
        int projectId,
        string stageCode,
        IReadOnlyList<ActualValidationIssue> introduced,
        Exception? failure)
    {
        if (failure is not null)
        {
            LogFailure(confirmed, operation, projectId, stageCode, failure);
        }
        else if (confirmed)
        {
            LogWarning(operation, projectId, stageCode, introduced);
        }
        else
        {
            LogUnconfirmed(operation, projectId, stageCode, introduced);
        }
    }

    private async Task<IReadOnlyList<ActualValidationIssue>> FindIntroducedViolationsAsync(
        int projectId,
        IReadOnlyList<ActualStageState> before,
        IReadOnlyList<ActualStageState> after,
        CancellationToken cancellationToken)
    {
        ProjectStageWorkflowSnapshot workflow;
        ArppIpaStageAuthority? ipaAuthority;

        // A savepoint keeps a failed read from aborting the writer's PostgreSQL
        // transaction: disposing it uncommitted rolls back to the savepoint.
        await using (var savepoint = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken))
        {
            workflow = await _workflowPolicy.GetAsync(projectId, cancellationToken);
            ipaAuthority = await _arppIpaStageAuthority.ResolveAsync(projectId, cancellationToken);
            await savepoint.CommitAsync(cancellationToken);
        }

        // Compare every violation, not the controlling one per stage: a write can add
        // a violation behind an unchanged controlling predecessor.
        var existing = ActualTimelineValidationService.ListChronologyViolations(workflow, ipaAuthority, before)
            .ToHashSet();

        return ActualTimelineValidationService.ListChronologyViolations(workflow, ipaAuthority, after)
            .Where(issue => !existing.Contains(issue))
            .ToArray();
    }

    private void LogWarning(
        string operation,
        int projectId,
        string stageCode,
        IReadOnlyList<ActualValidationIssue> introduced)
    {
        _logger.LogWarning(
            WarningEvent,
            WarningEventName + ": {Operation} on project {ProjectId} stage {StageCode} committed {ViolationCount} new Actual chronology violation(s) on {AffectedStageCodes} (sources {SourceStageCodes}): {Violations}",
            operation,
            projectId,
            stageCode,
            introduced.Count,
            introduced.Select(issue => issue.StageCode).ToArray(),
            introduced.Select(issue => issue.RelatedStageCode).ToArray(),
            introduced.Select(issue => issue.Message).ToArray());
    }

    private void LogUnconfirmed(
        string operation,
        int projectId,
        string stageCode,
        IReadOnlyList<ActualValidationIssue> introduced)
    {
        _logger.LogWarning(
            UnconfirmedEvent,
            UnconfirmedEventName + ": {Operation} on project {ProjectId} stage {StageCode} would introduce {ViolationCount} new Actual chronology violation(s) on {AffectedStageCodes} (sources {SourceStageCodes}): {Violations}. The enclosing transaction is owned by the caller, so whether this change was committed is not confirmed.",
            operation,
            projectId,
            stageCode,
            introduced.Count,
            introduced.Select(issue => issue.StageCode).ToArray(),
            introduced.Select(issue => issue.RelatedStageCode).ToArray(),
            introduced.Select(issue => issue.Message).ToArray());
    }

    private void LogFailure(bool confirmed, string operation, int projectId, string stageCode, Exception failure)
    {
        _logger.LogError(
            FailureEvent,
            failure,
            FailureEventName + ": Actual chronology observation failed for {Operation} on project {ProjectId} stage {StageCode}; {Outcome}.",
            operation,
            projectId,
            stageCode,
            confirmed
                ? "the lifecycle change was committed unchanged"
                : "the lifecycle change was not affected, and its commit is owned by the caller");
    }
}
