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
/// The shared validator evaluates the project's stage facts before and after the
/// writer's mutation with the same workflow, ARPP authority and operational date.
/// Violations present only afterwards are reported as
/// <c>Stages.ActualChronologyWarning</c>; violations already in the record are
/// existing data, as they are for Actual corrections, and are not repeated.
/// </summary>
internal sealed class LifecycleChronologyObserver
{
    internal const string WarningEventName = "Stages.ActualChronologyWarning";
    internal const string FailureEventName = "Stages.ActualChronologyWarningFailed";
    internal static readonly EventId WarningEvent = new(4101, WarningEventName);
    internal static readonly EventId FailureEvent = new(4102, FailureEventName);

    private readonly ApplicationDbContext _db;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;
    private readonly IClock _clock;
    private readonly ILogger _logger;

    public LifecycleChronologyObserver(
        ApplicationDbContext db,
        IProjectStageWorkflowPolicy workflowPolicy,
        IArppIpaStageAuthorityService arppIpaStageAuthority,
        IClock clock,
        ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? throw new ArgumentNullException(nameof(arppIpaStageAuthority));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
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

        Action report = failure is null
            ? () => LogWarning(operation, projectId, stageCode, introduced)
            : () => LogFailure(operation, projectId, stageCode, failure);

        try
        {
            // Report only what was actually committed: a rolled-back write logs nothing.
            transaction.RegisterAfterCommit(_ =>
            {
                report();
                return Task.CompletedTask;
            });
        }
        catch (InvalidOperationException)
        {
            // The writer runs inside a transaction RelationalTransactionScope did not
            // create, so no post-commit hook exists; report now rather than lose it.
            report();
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

        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
        var existing = ActualTimelineValidationService.Evaluate(
                workflow, ipaAuthority, today, new ActualTimelineCandidate(projectId, before, Array.Empty<ActualStageChange>()))
            .Warnings
            .ToHashSet();

        return ActualTimelineValidationService.Evaluate(
                workflow, ipaAuthority, today, new ActualTimelineCandidate(projectId, after, Array.Empty<ActualStageChange>()))
            .Warnings
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

    private void LogFailure(string operation, int projectId, string stageCode, Exception failure)
    {
        _logger.LogError(
            FailureEvent,
            failure,
            FailureEventName + ": Actual chronology observation failed for {Operation} on project {ProjectId} stage {StageCode}; the lifecycle change was committed unchanged.",
            operation,
            projectId,
            stageCode);
    }
}
