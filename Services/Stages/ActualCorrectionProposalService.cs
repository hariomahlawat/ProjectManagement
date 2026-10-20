using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Plans;

namespace ProjectManagement.Services.Stages;

// SECTION: Actual correction proposals (Phase D-3a/b)
/// <summary>
/// Records the assigned Project Officer's proposed Actual-date corrections and
/// completion backfill as immutable, project-scoped revisions (plan C.3–C.5).
/// Submission validates the proposed final state with the same validator and
/// normalization as direct publication, but never writes official
/// <see cref="ProjectStage"/> facts or <see cref="StageChangeLog"/> history:
/// only an approval (D-3d) publishes. At most one revision per project is
/// <see cref="ActualCorrectionRevisionStatus.Pending"/>; a new submission
/// supersedes it in one transaction.
/// </summary>
public sealed class ActualCorrectionProposalService
{
    internal const int NoteMaxLength = 1024;
    internal const int WithdrawalReasonMaxLength = 2000;
    private const string PendingDecisionStatus = "Pending";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IActualTimelineValidationService _validator;
    private readonly IAuditService? _audit;
    private readonly ILogger<ActualCorrectionProposalService> _logger;

    public ActualCorrectionProposalService(
        ApplicationDbContext db,
        IClock clock,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null,
        IProjectStageWorkflowPolicy? workflowPolicy = null,
        IActualTimelineValidationService? validator = null,
        IAuditService? audit = null,
        ILogger<ActualCorrectionProposalService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        if (!db.Database.IsRelational())
        {
            // Supersession and withdrawal are compare-and-set statements inside a real transaction.
            throw new NotSupportedException("Actual correction proposals require a relational database provider.");
        }

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _validator = validator ?? new ActualTimelineValidationService(
            workflowPolicy ?? new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()),
            arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db),
            clock);
        _audit = audit;
        _logger = logger ?? NullLogger<ActualCorrectionProposalService>.Instance;
    }

    // SECTION: Read model (D-3c editor baseline and status)
    /// <summary>
    /// The project's actionable revision with its items, or <c>null</c> for
    /// "none". Its <c>Id</c> and <c>SubmittedOn</c> are the baseline identity a
    /// form must post back. The caller authorizes viewing the project.
    /// </summary>
    public Task<ActualCorrectionRevision?> GetPendingAsync(int projectId, CancellationToken cancellationToken = default) =>
        _db.ActualCorrectionRevisions
            .AsNoTracking()
            .Include(revision => revision.Items)
            .Where(revision => revision.ProjectId == projectId
                && revision.Status == ActualCorrectionRevisionStatus.Pending)
            .SingleOrDefaultAsync(cancellationToken);

    // SECTION: Submission and supersession
    public async Task<ActualCorrectionSubmissionResult> SubmitAsync(
        ActualCorrectionSubmission submission,
        ActualWriteActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(actor.UserId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(actor));
        }

        if (submission.ProjectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(submission));
        }

        // Malformed input is refused before any lock; nothing is read or written.
        var inputErrors = CheckInput(submission, out var rows, out var revisionNote);
        if (inputErrors.Count > 0)
        {
            return ActualCorrectionSubmissionResult.Invalid(inputErrors);
        }

        // SECTION: Protected read (gate, project row, stage rows)
        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await PlanRevisionConsistency.LockProjectAsync(_db, submission.ProjectId, cancellationToken);

        var authority = await ActualWriteAuthority.ResolveProposalAsync(
            _db, submission.ProjectId, actor, requireEditableProject: true, cancellationToken);
        if (authority != ActualProposalAuthority.Allowed)
        {
            return ActualCorrectionSubmissionResult.NotAuthorized(authority);
        }

        // A post that does not carry the baseline cannot prove what the PO saw,
        // so it is never treated as "none" (E.4, S6).
        var baseline = submission.Baseline;
        if (baseline is null
            || baseline.BaselineVersion != ActualStageToken.BaselineVersion
            || baseline.BasedOnRevisionId.HasValue != baseline.BasedOnSubmittedOn.HasValue)
        {
            return ActualCorrectionSubmissionResult.Stale(ActualCorrectionStaleReason.MissingBaseline);
        }

        // Locked before the ARPP read inside validation, as lifecycle submission
        // does: ARPP synchronization (outside the gate) is seen entirely or not at all,
        // and the Before* snapshot below cannot change until commit.
        var stages = await ActualFactPublisher.LoadProjectStagesForUpdateAsync(_db, submission.ProjectId, cancellationToken);
        var stageLookup = stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);

        // Read under the gate, which every proposal writer takes.
        var pending = await GetPendingAsync(submission.ProjectId, cancellationToken);

        // SECTION: Editor baseline (E.4)
        var postedIdentityMatches = pending is null
            ? !baseline.BasedOnRevisionId.HasValue
            : baseline.BasedOnRevisionId == pending.Id && baseline.BasedOnSubmittedOn == pending.SubmittedOn;
        if (!postedIdentityMatches)
        {
            return ActualCorrectionSubmissionResult.Stale(ActualCorrectionStaleReason.PendingRevisionChanged);
        }

        var unknown = rows
            .Where(row => !stageLookup.ContainsKey(row.StageCode))
            .Select(row => $"{row.StageCode}: This stage was not found for this project.")
            .ToArray();
        if (unknown.Length > 0)
        {
            return ActualCorrectionSubmissionResult.Invalid(unknown);
        }

        var isBackfill = submission.EntryPoint == ActualChangeEntryPoint.Backfill;
        var staleStages = rows
            .Where(row => isBackfill
                // Every backfill row writes; an editor row needs a token only when it would change the stage.
                || ActualStageToken.WouldChange(stageLookup[row.StageCode], row.ActualStart, row.ClearActualStart, row.CompletedOn))
            .Where(row => !ActualStageToken.Matches(row.StageToken, stageLookup[row.StageCode]))
            .Select(row => stageLookup[row.StageCode].StageCode)
            .ToArray();
        if (staleStages.Length > 0)
        {
            return ActualCorrectionSubmissionResult.Stale(ActualCorrectionStaleReason.StagesChanged, staleStages);
        }

        // The editor posts the complete proposal; backfill carries forward the
        // pending items it does not override (rule 24).
        var submittedCodes = rows.Select(row => row.StageCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var carried = isBackfill && pending is not null
            ? pending.Items.Where(item => !submittedCodes.Contains(item.StageCode)).ToArray()
            : Array.Empty<ActualCorrectionItem>();
        var changedCarried = carried
            .Where(item => !stageLookup.TryGetValue(item.StageCode, out var stage) || !MatchesBefore(item, stage))
            .Select(item => item.StageCode)
            .ToArray();
        if (changedCarried.Length > 0)
        {
            return ActualCorrectionSubmissionResult.Stale(ActualCorrectionStaleReason.CarriedItemsChanged, changedCarried);
        }

        // SECTION: Final-state validation (shared validator and normalization)
        var official = stages.Select(ActualStageState.From).ToArray();
        var submitted = rows
            .Select(row => new ActualStageChange(row.StageCode, row.ActualStart, row.ClearActualStart, row.CompletedOn))
            .ToArray();
        var validation = await _validator.ValidateAsync(
            new ActualTimelineCandidate(submission.ProjectId, official, submitted, submission.EntryPoint),
            cancellationToken);

        if (isBackfill)
        {
            // Same precedence as direct backfill: validation (422) before "no longer requires backfill" (409).
            var rowErrors = validation.Errors
                .Where(error => error.Rule != ActualValidationRule.NotAwaitingBackfill)
                .ToArray();
            if (rowErrors.Length > 0 && (carried.Length == 0 || rowErrors.Any(error => error.Rule != ActualValidationRule.Chronology)))
            {
                return ActualCorrectionSubmissionResult.Invalid(rowErrors.Select(error => error.Message).ToArray());
            }

            var notAwaiting = validation.Errors
                .Where(error => error.Rule == ActualValidationRule.NotAwaitingBackfill)
                .Select(error => error.StageCode)
                .ToArray();
            if (notAwaiting.Length > 0 && rowErrors.Length > 0)
            {
                return ActualCorrectionSubmissionResult.Invalid(rowErrors.Select(error => error.Message).ToArray());
            }

            if (notAwaiting.Length > 0)
            {
                return ActualCorrectionSubmissionResult.Conflict(notAwaiting);
            }

            if (carried.Length > 0)
            {
                // The backfill rows' normalized values and the carried-forward items form
                // the whole proposal; its chronology is validated as one final state.
                validation = await _validator.ValidateAsync(
                    new ActualTimelineCandidate(
                        submission.ProjectId,
                        official,
                        carried.Select(ToChange)
                            .Concat(validation.Changes.Select(change => ToFinalChange(change.Official, change.Final)))
                            .ToArray(),
                        ActualChangeEntryPoint.Editor),
                    cancellationToken);
            }
        }

        if (!validation.IsValid)
        {
            return ActualCorrectionSubmissionResult.Invalid(validation.Errors.Select(error => error.Message).ToArray());
        }

        // SECTION: Pending lifecycle contradiction (F.6)
        var contradictions = await FindLifecycleContradictionsAsync(
            submission.ProjectId, stages, validation.Changes, cancellationToken);
        if (contradictions.Count > 0)
        {
            return ActualCorrectionSubmissionResult.Invalid(contradictions);
        }

        // SECTION: Candidate revision
        var notesByStage = rows.ToDictionary(row => row.StageCode, row => row.Note, StringComparer.OrdinalIgnoreCase);
        foreach (var item in carried)
        {
            notesByStage[item.StageCode] = item.Note;
        }

        var items = validation.Changes
            .Select(change => new ActualCorrectionItem
            {
                StageCode = stageLookup[change.StageCode].StageCode,
                // Derived from the locked official row, never from the client.
                Kind = change.IsBackfill ? ActualCorrectionItemKind.Backfill : ActualCorrectionItemKind.Correction,
                BeforeStatus = change.Official.Status,
                BeforeActualStart = change.Official.ActualStart,
                BeforeCompletedOn = change.Official.CompletedOn,
                BeforeRequiresBackfill = change.Official.RequiresBackfill,
                ProposedActualStart = change.Final.ActualStart,
                ProposedCompletedOn = change.Final.CompletedOn,
                ClearsActualStart = change.Official.ActualStart.HasValue && !change.Final.ActualStart.HasValue,
                Note = notesByStage.GetValueOrDefault(change.StageCode)
            })
            .ToList();

        // A backfill that only adds rows keeps the pending justification unless a new one is given.
        var note = isBackfill && revisionNote is null && pending is not null ? pending.Note : revisionNote;
        var contentHash = ComputeContentHash(items, note);

        if (pending is not null
            && string.Equals(contentHash, ComputeContentHash(pending.Items, pending.Note), StringComparison.Ordinal)
            && pending.Items.All(item => stageLookup.TryGetValue(item.StageCode, out var stage) && MatchesBefore(item, stage)))
        {
            // Same proposal on the same official baseline: the reviewer's view stays valid.
            return ActualCorrectionSubmissionResult.Unchanged(Identity(pending));
        }

        if (items.Count == 0)
        {
            return ActualCorrectionSubmissionResult.Invalid(new[]
            {
                pending is null
                    ? "No changes to submit. Every date matches the recorded value."
                    : "No changes. Use Withdraw to cancel the pending update."
            });
        }

        // SECTION: Atomic supersession and insert (C.4 step 9)
        var now = ToStoredPrecision(_clock.UtcNow);
        var revision = new ActualCorrectionRevision
        {
            ProjectId = submission.ProjectId,
            RevisionNo = await NextRevisionNoAsync(submission.ProjectId, cancellationToken),
            Status = ActualCorrectionRevisionStatus.Pending,
            EntryPoint = isBackfill ? ActualCorrectionEntryPoint.Backfill : ActualCorrectionEntryPoint.Editor,
            SubmittedByUserId = actor.UserId,
            SubmittedOn = now,
            Note = note,
            ContentHash = contentHash,
            SupersedesRevisionId = pending?.Id,
            Items = items
        };

        try
        {
            // 1. Close the previous revision without the forward link, so no statement
            //    ever sees two Pending rows (the filtered unique index is per statement).
            if (pending is not null
                && !await CloseIfPendingAsync(pending.Id, ActualCorrectionRevisionStatus.Superseded, actor.UserId, now, null, cancellationToken))
            {
                return ActualCorrectionSubmissionResult.Stale(ActualCorrectionStaleReason.PendingRevisionChanged);
            }

            // 2. Insert the new revision and its items; the backward link targets an existing row.
            _db.ActualCorrectionRevisions.Add(revision);
            await _db.SaveChangesAsync(cancellationToken);

            // 3. Forward link, now that the new Id exists.
            if (pending is not null)
            {
                await LinkSupersededAsync(pending.Id, revision.Id, cancellationToken);
            }

            RegisterAudit(transaction, "Projects.ActualCorrectionSubmitted", actor.UserId, revision, pending?.Id);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // The transaction rolls back on dispose. Detach what this call added so a
            // later SaveChanges on the same context cannot replay the aborted insert.
            Detach(revision);
            throw;
        }

        LogPostCommitErrors(transaction, "submitting an actual correction", submission.ProjectId);
        _logger.LogInformation(
            "Actual correction revision {RevisionNo} ({RevisionId}) submitted for project {ProjectId} by {UserId}; supersedes {SupersedesRevisionId}.",
            revision.RevisionNo, revision.Id, submission.ProjectId, actor.UserId, pending?.Id);

        return ActualCorrectionSubmissionResult.Submitted(
            Identity(revision),
            pending is null ? null : Identity(pending),
            validation.Warnings.Select(warning => warning.Message).ToArray());
    }

    // SECTION: Withdrawal (C.5)
    public async Task<ActualCorrectionWithdrawalResult> WithdrawAsync(
        int projectId,
        int revisionId,
        DateTimeOffset expectedSubmittedOn,
        ActualWriteActor actor,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(actor.UserId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(actor));
        }

        var normalizedReason = NormalizeNote(reason);
        if (normalizedReason?.Length > WithdrawalReasonMaxLength)
        {
            return new ActualCorrectionWithdrawalResult(ActualCorrectionWithdrawalOutcome.Invalid,
                Error: $"The reason must be {WithdrawalReasonMaxLength} characters or fewer.");
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await PlanRevisionConsistency.LockProjectAsync(_db, projectId, cancellationToken);

        // The current assignee only (K-6); a former PO cannot withdraw.
        var authority = await ActualWriteAuthority.ResolveProposalAsync(
            _db, projectId, actor, requireEditableProject: false, cancellationToken);
        if (authority != ActualProposalAuthority.Allowed)
        {
            return new ActualCorrectionWithdrawalResult(authority switch
            {
                ActualProposalAuthority.ProjectUnavailable => ActualCorrectionWithdrawalOutcome.NotFound,
                ActualProposalAuthority.DirectAuthority => ActualCorrectionWithdrawalOutcome.DirectAuthority,
                _ => ActualCorrectionWithdrawalOutcome.Forbidden
            });
        }

        // Exact identity: the revision the form showed, still actionable.
        var revision = await _db.ActualCorrectionRevisions
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == revisionId && item.ProjectId == projectId, cancellationToken);
        if (revision is null
            || revision.Status != ActualCorrectionRevisionStatus.Pending
            || revision.SubmittedOn != expectedSubmittedOn
            || !await CloseIfPendingAsync(revision.Id, ActualCorrectionRevisionStatus.Withdrawn, actor.UserId,
                ToStoredPrecision(_clock.UtcNow), normalizedReason, cancellationToken))
        {
            return new ActualCorrectionWithdrawalResult(ActualCorrectionWithdrawalOutcome.NotActionable);
        }

        RegisterAudit(transaction, "Projects.ActualCorrectionWithdrawn", actor.UserId, revision, supersedesRevisionId: null);
        await transaction.CommitAsync(cancellationToken);
        LogPostCommitErrors(transaction, "withdrawing an actual correction", projectId);

        return new ActualCorrectionWithdrawalResult(ActualCorrectionWithdrawalOutcome.Withdrawn, Identity(revision));
    }

    // SECTION: HoD/Admin decisions (C.6, Phase D-3d)
    /// <summary>
    /// Approves the exact pending revision and publishes its Actual facts with their
    /// mandatory <see cref="StageChangeLog"/> rows in one transaction. Lock order is
    /// the submission order (gate, project row, stage rows), so it serializes with
    /// direct publication, submission, lifecycle decisions and ARPP synchronization.
    /// A changed official baseline, an invalid final state or a pending lifecycle
    /// contradiction refuses the approval and leaves the revision Pending.
    /// </summary>
    public async Task<ActualCorrectionDecisionResult> ApproveAsync(
        ActualCorrectionDecision decision,
        ActualWriteActor reviewer,
        CancellationToken cancellationToken = default)
    {
        var refusal = CheckDecisionInput(decision, reviewer);
        if (refusal is not null)
        {
            return refusal;
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await PlanRevisionConsistency.LockProjectAsync(_db, decision.ProjectId, cancellationToken);
        if (!await ActualWriteAuthority.IsProjectAvailableAsync(_db, decision.ProjectId, cancellationToken))
        {
            return ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.ProjectUnavailable);
        }

        // Stage rows before the revision and ARPP reads: a writer outside the gate is
        // seen entirely or not at all, and nothing validated below can change before commit.
        var stages = await ActualFactPublisher.LoadProjectStagesForUpdateAsync(_db, decision.ProjectId, cancellationToken);
        var stageLookup = stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);

        var (revision, identityRefusal) = await LoadDecidableAsync(decision, reviewer, cancellationToken);
        if (revision is null)
        {
            return identityRefusal!;
        }

        // SECTION: Baseline (SourceChanged)
        var changedStages = revision.Items
            .Where(item => !stageLookup.TryGetValue(item.StageCode, out var stage) || !MatchesBefore(item, stage))
            .Select(item => item.StageCode)
            .ToArray();
        if (changedStages.Length > 0)
        {
            return ActualCorrectionDecisionResult.SourceChanged(Identity(revision), changedStages);
        }

        // SECTION: Final-state revalidation (stored final values replayed as corrections)
        var validation = await _validator.ValidateAsync(
            new ActualTimelineCandidate(
                decision.ProjectId,
                stages.Select(ActualStageState.From).ToArray(),
                revision.Items.Select(ToChange).ToArray(),
                ActualChangeEntryPoint.Editor),
            cancellationToken);
        if (!validation.IsValid)
        {
            return ActualCorrectionDecisionResult.Invalid(Identity(revision), validation.Errors.Select(error => error.Message).ToArray());
        }

        // Every item must still publish exactly one change; anything else is not what was reviewed.
        var changedCodes = validation.Changes.Select(change => change.StageCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unchangedItems = revision.Items.Where(item => !changedCodes.Contains(item.StageCode)).Select(item => item.StageCode).ToArray();
        if (unchangedItems.Length > 0 || validation.Changes.Count != revision.Items.Count)
        {
            return ActualCorrectionDecisionResult.SourceChanged(Identity(revision), unchangedItems);
        }

        var contradictions = await FindLifecycleContradictionsAsync(decision.ProjectId, stages, validation.Changes, cancellationToken);
        if (contradictions.Count > 0)
        {
            return ActualCorrectionDecisionResult.LifecycleConflict(Identity(revision), contradictions);
        }

        // SECTION: Claim, publish and record (one atomic unit, E.1)
        var now = ToStoredPrecision(_clock.UtcNow);
        var remarks = NormalizeNote(decision.Remarks);
        var logs = new List<StageChangeLog>(validation.Changes.Count);
        try
        {
            if (!await CloseIfPendingAsync(revision.Id, ActualCorrectionRevisionStatus.Approved, reviewer.UserId, now, remarks, cancellationToken))
            {
                return ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.NotActionable, Identity(revision));
            }

            var note = string.Format(
                CultureInfo.InvariantCulture,
                "Approved actual correction rev {0} (#{1}) submitted by {2} at {3:yyyy-MM-dd HH:mm} UTC.",
                revision.RevisionNo,
                revision.Id,
                revision.SubmittedByUserId,
                revision.SubmittedOn.UtcDateTime);
            foreach (var change in validation.Changes)
            {
                var log = ActualFactPublisher.Apply(
                    stageLookup[change.StageCode],
                    change,
                    reviewer.UserId,
                    now,
                    change.IsBackfill ? ActualFactPublisher.BackfillAction : ActualFactPublisher.ActualsUpdatedAction,
                    note);
                logs.Add(log);
                await _db.StageChangeLogs.AddAsync(log, cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
            RegisterAudit(transaction, "Projects.ActualCorrectionApproved", reviewer.UserId, revision, supersedesRevisionId: null);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Rolled back on dispose; detach so a later save on this context cannot replay
            // the aborted stage changes or history.
            foreach (var log in logs)
            {
                _db.Entry(log).State = EntityState.Detached;
            }

            foreach (var stage in stages)
            {
                _db.Entry(stage).State = EntityState.Detached;
            }

            throw;
        }

        LogPostCommitErrors(transaction, "approving an actual correction", decision.ProjectId);
        _logger.LogInformation(
            "Actual correction revision {RevisionNo} ({RevisionId}) for project {ProjectId} approved by {UserId}; {Count} stage(s) published.",
            revision.RevisionNo, revision.Id, decision.ProjectId, reviewer.UserId, validation.Changes.Count);

        return new ActualCorrectionDecisionResult(
            ActualCorrectionDecisionOutcome.Approved,
            Identity(revision),
            StageCodes: validation.Changes.Select(change => change.StageCode).ToArray(),
            Warnings: validation.Warnings.Select(warning => warning.Message).ToArray());
    }

    /// <summary>
    /// Rejects the exact pending revision. Nothing is published, so no baseline or
    /// validity check applies (K-3); the epoch check still does (rule 24).
    /// </summary>
    public async Task<ActualCorrectionDecisionResult> RejectAsync(
        ActualCorrectionDecision decision,
        ActualWriteActor reviewer,
        CancellationToken cancellationToken = default)
    {
        var refusal = CheckDecisionInput(decision, reviewer);
        if (refusal is not null)
        {
            return refusal;
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await PlanRevisionConsistency.LockProjectAsync(_db, decision.ProjectId, cancellationToken);
        if (!await ActualWriteAuthority.IsProjectAvailableAsync(_db, decision.ProjectId, cancellationToken))
        {
            return ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.ProjectUnavailable);
        }

        var (revision, identityRefusal) = await LoadDecidableAsync(decision, reviewer, cancellationToken);
        if (revision is null)
        {
            return identityRefusal!;
        }

        if (!await CloseIfPendingAsync(revision.Id, ActualCorrectionRevisionStatus.Rejected, reviewer.UserId,
                ToStoredPrecision(_clock.UtcNow), NormalizeNote(decision.Remarks), cancellationToken))
        {
            return ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.NotActionable, Identity(revision));
        }

        RegisterAudit(transaction, "Projects.ActualCorrectionRejected", reviewer.UserId, revision, supersedesRevisionId: null);
        await transaction.CommitAsync(cancellationToken);
        LogPostCommitErrors(transaction, "rejecting an actual correction", decision.ProjectId);

        return ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.Rejected, Identity(revision));
    }

    // K-1: HoD and Admin decide, whatever other roles they hold; the client supplies no authority.
    private static ActualCorrectionDecisionResult? CheckDecisionInput(ActualCorrectionDecision decision, ActualWriteActor reviewer)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(reviewer);
        if (string.IsNullOrWhiteSpace(reviewer.UserId) || !(reviewer.IsAdmin || reviewer.IsHoD))
        {
            return ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.Forbidden);
        }

        if (decision.ProjectId <= 0 || decision.RevisionId <= 0)
        {
            return ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.NotActionable);
        }

        return NormalizeNote(decision.Remarks)?.Length > WithdrawalReasonMaxLength
            ? ActualCorrectionDecisionResult.Invalid(null, new[] { $"Remarks must be {WithdrawalReasonMaxLength} characters or fewer." })
            : null;
    }

    /// <summary>
    /// Re-reads the revision under the gate: it must be the project's Pending revision with
    /// the reviewed number and epoch, and must not have been submitted by the reviewer.
    /// The submitter is the revision's immutable <c>SubmittedByUserId</c>, never the
    /// current assignment, so role elevation or reassignment cannot enable a self-decision.
    /// </summary>
    private async Task<(ActualCorrectionRevision? Revision, ActualCorrectionDecisionResult? Refusal)> LoadDecidableAsync(
        ActualCorrectionDecision decision,
        ActualWriteActor reviewer,
        CancellationToken cancellationToken)
    {
        var revision = await _db.ActualCorrectionRevisions
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == decision.RevisionId && item.ProjectId == decision.ProjectId, cancellationToken);

        if (revision is null
            || revision.Status != ActualCorrectionRevisionStatus.Pending
            || revision.SubmittedOn != decision.ExpectedSubmittedOn
            || (decision.ExpectedRevisionNo.HasValue && revision.RevisionNo != decision.ExpectedRevisionNo.Value))
        {
            return (null, ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.NotActionable));
        }

        if (string.Equals(revision.SubmittedByUserId, reviewer.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return (null, ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.SelfDecisionForbidden, Identity(revision)));
        }

        return (revision, null);
    }

    // SECTION: Canonical content hash
    /// <summary>
    /// SHA-256 (hex) of the canonical proposed content: the items in stage-code
    /// order (code, kind, normalized proposed dates, clear intent, note) and the
    /// revision note. Notes are trimmed, line endings normalized, and blank means
    /// absent. Before-values and the entry point are not content; the official
    /// baseline is compared separately.
    /// </summary>
    internal static string ComputeContentHash(IEnumerable<ActualCorrectionItem> items, string? note)
    {
        ArgumentNullException.ThrowIfNull(items);

        var canonical = new
        {
            v = 1,
            note = NormalizeNote(note),
            items = items
                .OrderBy(item => item.StageCode.Trim().ToUpperInvariant(), StringComparer.Ordinal)
                .Select(item => new object?[]
                {
                    item.StageCode.Trim().ToUpperInvariant(),
                    item.Kind,
                    FormatDate(item.ProposedActualStart),
                    FormatDate(item.ProposedCompletedOn),
                    item.ClearsActualStart,
                    NormalizeNote(item.Note)
                })
                .ToArray()
        };

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
    }

    internal static string? NormalizeNote(string? note)
    {
        var trimmed = note?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    // SECTION: Helpers
    private static List<string> CheckInput(
        ActualCorrectionSubmission submission,
        out IReadOnlyList<NormalizedRow> rows,
        out string? revisionNote)
    {
        var errors = new List<string>();
        var posted = submission.Rows ?? Array.Empty<ActualCorrectionProposalRow>();
        rows = posted
            .Where(row => row is not null && !string.IsNullOrWhiteSpace(row.StageCode))
            .Select(row => new NormalizedRow(
                row.StageCode.Trim(),
                row.ActualStart,
                row.ClearActualStart,
                row.CompletedOn,
                row.StageToken,
                NormalizeNote(row.Note)))
            .ToArray();

        if (rows.Count != posted.Count)
        {
            errors.Add("Each row must name a stage.");
        }

        if (submission.EntryPoint == ActualChangeEntryPoint.Backfill && rows.Count == 0)
        {
            errors.Add("At least one stage update is required.");
        }

        var duplicates = rows
            .GroupBy(row => row.StageCode, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            errors.Add($"Include each stage once: {string.Join(", ", duplicates)}.");
        }

        if (rows.Any(row => row.Note?.Length > NoteMaxLength))
        {
            errors.Add($"Stage notes must be {NoteMaxLength} characters or fewer.");
        }

        revisionNote = NormalizeNote(submission.Note);
        if (revisionNote?.Length > NoteMaxLength)
        {
            errors.Add($"The note must be {NoteMaxLength} characters or fewer.");
        }

        return errors;
    }

    /// <summary>
    /// F.6: official facts plus the latest pending lifecycle request per stage,
    /// with the proposal applied. A violation that appears only in that projection
    /// (the proposal itself is already valid against official facts) conflicts
    /// with a lifecycle update that could still publish, so it is refused.
    /// </summary>
    private async Task<IReadOnlyList<string>> FindLifecycleContradictionsAsync(
        int projectId,
        IReadOnlyCollection<ProjectStage> stages,
        IReadOnlyList<ActualStageResolution> changes,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return Array.Empty<string>();
        }

        var pendingRequests = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(request => request.ProjectId == projectId && request.DecisionStatus == PendingDecisionStatus)
            .ToListAsync(cancellationToken);
        if (pendingRequests.Count == 0)
        {
            return Array.Empty<string>();
        }

        var latestPending = pendingRequests
            .GroupBy(request => request.StageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(request => request.RequestedOn)
                .ThenByDescending(request => request.Id)
                .First())
            .ToArray();
        var pendingCodes = latestPending.Select(request => request.StageCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ARPP effective state is applied by the validator itself.
        var projected = ProjectedLifecycleProjection.BuildProjectedStates(
            stages, latestPending, new Dictionary<string, DateOnly?>(StringComparer.OrdinalIgnoreCase));
        var flags = stages.ToDictionary(stage => stage.StageCode, stage => stage.RequiresBackfill, StringComparer.OrdinalIgnoreCase);

        var result = await _validator.ValidateAsync(
            new ActualTimelineCandidate(
                projectId,
                projected.Values
                    .Select(state => new ActualStageState(
                        state.StageCode,
                        state.ProjectedStatus,
                        state.ProjectedStartDate,
                        state.ProjectedCompletionDate,
                        flags[state.StageCode]))
                    .ToArray(),
                changes.Select(change => ToFinalChange(change.Official, change.Final)).ToArray(),
                ActualChangeEntryPoint.Editor),
            cancellationToken);

        return result.Errors
            .Select(error =>
            {
                var involved = new[] { error.StageCode, error.RelatedStageCode }
                    .Where(code => code is not null && pendingCodes.Contains(code))
                    .Select(code => code!)
                    .ToArray();
                var named = involved.Length > 0 ? involved : pendingCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray();
                return $"{error.Message} This conflicts with the pending lifecycle update for {string.Join(", ", named)} " +
                    "awaiting approval. Revise this correction or wait for that decision.";
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<int> NextRevisionNoAsync(int projectId, CancellationToken cancellationToken)
    {
        // Allocated under the project gate; the unique (ProjectId, RevisionNo) index is the backstop.
        var max = await _db.ActualCorrectionRevisions
            .Where(revision => revision.ProjectId == projectId)
            .MaxAsync(revision => (int?)revision.RevisionNo, cancellationToken);
        return (max ?? 0) + 1;
    }

    /// <summary>Compare-and-set closure: changes the revision only while it is still Pending.</summary>
    private async Task<bool> CloseIfPendingAsync(
        int revisionId,
        string status,
        string userId,
        DateTimeOffset closedOn,
        string? note,
        CancellationToken cancellationToken)
    {
        var closed = await _db.ActualCorrectionRevisions
            .Where(item => item.Id == revisionId && item.Status == ActualCorrectionRevisionStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, status)
                    .SetProperty(item => item.ClosedByUserId, userId)
                    .SetProperty(item => item.ClosedOn, closedOn)
                    .SetProperty(item => item.DecisionNote, note),
                cancellationToken);
        return closed == 1;
    }

    private async Task LinkSupersededAsync(int previousId, int newId, CancellationToken cancellationToken)
    {
        var linked = await _db.ActualCorrectionRevisions
            .Where(item => item.Id == previousId
                && item.Status == ActualCorrectionRevisionStatus.Superseded
                && item.SupersededByRevisionId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.SupersededByRevisionId, (int?)newId),
                cancellationToken);

        if (linked != 1)
        {
            // Unreachable under the gate; refuse rather than leave a lineage without its forward link.
            throw new InvalidOperationException($"Actual correction revision {previousId} could not be linked to its successor {newId}.");
        }
    }

    private void RegisterAudit(
        RelationalTransactionScope transaction,
        string action,
        string userId,
        ActualCorrectionRevision revision,
        int? supersedesRevisionId)
    {
        if (_audit is null)
        {
            return;
        }

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectId"] = revision.ProjectId.ToString(CultureInfo.InvariantCulture),
            ["RevisionId"] = revision.Id.ToString(CultureInfo.InvariantCulture),
            ["RevisionNo"] = revision.RevisionNo.ToString(CultureInfo.InvariantCulture),
            ["EntryPoint"] = revision.EntryPoint,
            ["StageCodes"] = string.Join(",", revision.Items.Select(item => item.StageCode)),
            ["SupersedesRevisionId"] = supersedesRevisionId?.ToString(CultureInfo.InvariantCulture)
        };

        // Secondary telemetry after commit only; it never decides whether the evidence commits (E.1).
        transaction.RegisterAfterCommit(_ => _audit.LogAsync(action, userId: userId, data: data));
    }

    private void LogPostCommitErrors(RelationalTransactionScope transaction, string operation, int projectId)
    {
        foreach (var error in transaction.PostCommitErrors)
        {
            _logger.LogError(error, "Post-commit audit failed after {Operation} for project {ProjectId}.", operation, projectId);
        }
    }

    private void Detach(ActualCorrectionRevision revision)
    {
        // Detaching fixes up navigations, so iterate a copy.
        foreach (var item in revision.Items.ToArray())
        {
            _db.Entry(item).State = EntityState.Detached;
        }

        _db.Entry(revision).State = EntityState.Detached;
    }

    private static bool MatchesBefore(ActualCorrectionItem item, ProjectStage stage) =>
        item.BeforeStatus == stage.Status
        && item.BeforeActualStart == stage.ActualStart
        && item.BeforeCompletedOn == stage.CompletedOn
        && item.BeforeRequiresBackfill == stage.RequiresBackfill;

    // A stored item is a final value; replaying it as a correction reproduces it.
    private static ActualStageChange ToChange(ActualCorrectionItem item) =>
        new(item.StageCode, item.ProposedActualStart, item.ClearsActualStart, item.ProposedCompletedOn);

    private static ActualStageChange ToFinalChange(ActualStageState official, ActualStageState final) =>
        new(final.StageCode,
            final.ActualStart,
            ClearActualStart: official.ActualStart.HasValue && !final.ActualStart.HasValue,
            final.CompletedOn);

    private static ActualCorrectionRevisionIdentity Identity(ActualCorrectionRevision revision) =>
        new(revision.Id, revision.RevisionNo, revision.SubmittedOn);

    // PostgreSQL timestamptz keeps microseconds; store the epoch at that precision so
    // the value a form posts back equals the value held in memory and in the row.
    private static DateTimeOffset ToStoredPrecision(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMicrosecond), value.Offset);

    private static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record NormalizedRow(
        string StageCode,
        DateOnly? ActualStart,
        bool ClearActualStart,
        DateOnly? CompletedOn,
        string? StageToken,
        string? Note);
}

// SECTION: Input contracts
/// <summary>
/// One posted row, with the same semantics as the direct editors: a null date
/// keeps the recorded value, <see cref="ClearActualStart"/> explicitly clears
/// the start, and a missing completion date never clears it.
/// </summary>
public sealed record ActualCorrectionProposalRow(
    string StageCode,
    DateOnly? ActualStart = null,
    bool ClearActualStart = false,
    DateOnly? CompletedOn = null,
    string? StageToken = null,
    string? Note = null);

/// <summary>
/// The editor baseline the form was rendered with (E.4): the marker, and the
/// actionable revision identity, both empty for "none". It carries no authority.
/// </summary>
public sealed record ActualCorrectionBaseline(
    int? BaselineVersion,
    int? BasedOnRevisionId = null,
    DateTimeOffset? BasedOnSubmittedOn = null);

public sealed record ActualCorrectionSubmission(
    int ProjectId,
    ActualChangeEntryPoint EntryPoint,
    IReadOnlyList<ActualCorrectionProposalRow> Rows,
    ActualCorrectionBaseline? Baseline,
    string? Note = null);

// SECTION: Result contracts
/// <summary>The exact identity a decision or withdrawal binds to (C.3).</summary>
public sealed record ActualCorrectionRevisionIdentity(int Id, int RevisionNo, DateTimeOffset SubmittedOn);

public enum ActualCorrectionSubmissionOutcome
{
    Submitted,
    Unchanged,
    Stale,
    Invalid,
    Conflict,
    Forbidden,
    /// <summary>The actor holds HoD or Admin authority and publishes directly.</summary>
    DirectAuthority,
    /// <summary>The project is closed or legacy; its Actual dates are not editable.</summary>
    ProjectNotEditable,
    NotFound
}

public enum ActualCorrectionStaleReason
{
    /// <summary>The post carries no complete baseline (legacy or incomplete form).</summary>
    MissingBaseline,
    /// <summary>The actionable revision differs from the one the form was based on.</summary>
    PendingRevisionChanged,
    /// <summary>Official facts of a submitted stage differ from its posted token.</summary>
    StagesChanged,
    /// <summary>Official facts of a carried-forward pending item differ from its recorded baseline.</summary>
    CarriedItemsChanged
}

public sealed record ActualCorrectionSubmissionResult(
    ActualCorrectionSubmissionOutcome Outcome,
    ActualCorrectionRevisionIdentity? Revision = null,
    ActualCorrectionRevisionIdentity? SupersededRevision = null,
    ActualCorrectionStaleReason? StaleReason = null,
    IReadOnlyList<string>? StageCodes = null,
    IReadOnlyList<string>? Errors = null,
    IReadOnlyList<string>? Warnings = null)
{
    internal static ActualCorrectionSubmissionResult Submitted(
        ActualCorrectionRevisionIdentity revision,
        ActualCorrectionRevisionIdentity? superseded,
        IReadOnlyList<string> warnings) =>
        new(ActualCorrectionSubmissionOutcome.Submitted, revision, superseded, Warnings: warnings);

    internal static ActualCorrectionSubmissionResult Unchanged(ActualCorrectionRevisionIdentity revision) =>
        new(ActualCorrectionSubmissionOutcome.Unchanged, revision);

    internal static ActualCorrectionSubmissionResult Stale(
        ActualCorrectionStaleReason reason,
        IReadOnlyList<string>? stageCodes = null) =>
        new(ActualCorrectionSubmissionOutcome.Stale, StaleReason: reason, StageCodes: stageCodes ?? Array.Empty<string>());

    internal static ActualCorrectionSubmissionResult Invalid(IReadOnlyList<string> errors) =>
        new(ActualCorrectionSubmissionOutcome.Invalid, Errors: errors);

    internal static ActualCorrectionSubmissionResult Conflict(IReadOnlyList<string> stageCodes) =>
        new(ActualCorrectionSubmissionOutcome.Conflict, StageCodes: stageCodes);

    internal static ActualCorrectionSubmissionResult NotAuthorized(ActualProposalAuthority authority) =>
        new(authority switch
        {
            ActualProposalAuthority.ProjectUnavailable => ActualCorrectionSubmissionOutcome.NotFound,
            ActualProposalAuthority.DirectAuthority => ActualCorrectionSubmissionOutcome.DirectAuthority,
            ActualProposalAuthority.ProjectNotEditable => ActualCorrectionSubmissionOutcome.ProjectNotEditable,
            _ => ActualCorrectionSubmissionOutcome.Forbidden
        });
}

public enum ActualCorrectionWithdrawalOutcome
{
    Withdrawn,
    /// <summary>The revision is not the actionable one the caller saw (decided, superseded or another epoch).</summary>
    NotActionable,
    Invalid,
    Forbidden,
    DirectAuthority,
    NotFound
}

public sealed record ActualCorrectionWithdrawalResult(
    ActualCorrectionWithdrawalOutcome Outcome,
    ActualCorrectionRevisionIdentity? Revision = null,
    string? Error = null);

// SECTION: Decision contracts (D-3d)
/// <summary>The exact revision a reviewer saw: id, project, number and submission epoch.</summary>
public sealed record ActualCorrectionDecision(
    int ProjectId,
    int RevisionId,
    DateTimeOffset ExpectedSubmittedOn,
    int? ExpectedRevisionNo = null,
    string? Remarks = null);

public enum ActualCorrectionDecisionOutcome
{
    Approved,
    Rejected,
    /// <summary>Not the Pending revision the reviewer saw: decided, withdrawn, superseded or another epoch.</summary>
    NotActionable,
    /// <summary>Official facts of an item changed since submission; the revision stays Pending for the PO to revise.</summary>
    SourceChanged,
    /// <summary>The proposed final state fails the shared Actual validation now.</summary>
    Invalid,
    /// <summary>The proposal contradicts a pending lifecycle request that could still publish (F.6).</summary>
    LifecycleConflict,
    Forbidden,
    /// <summary>The reviewer submitted this revision.</summary>
    SelfDecisionForbidden,
    ProjectUnavailable
}

public sealed record ActualCorrectionDecisionResult(
    ActualCorrectionDecisionOutcome Outcome,
    ActualCorrectionRevisionIdentity? Revision = null,
    IReadOnlyList<string>? StageCodes = null,
    IReadOnlyList<string>? Errors = null,
    IReadOnlyList<string>? Warnings = null)
{
    internal static ActualCorrectionDecisionResult Of(
        ActualCorrectionDecisionOutcome outcome,
        ActualCorrectionRevisionIdentity? revision = null) => new(outcome, revision);

    internal static ActualCorrectionDecisionResult SourceChanged(ActualCorrectionRevisionIdentity revision, IReadOnlyList<string> stageCodes) =>
        new(ActualCorrectionDecisionOutcome.SourceChanged, revision, StageCodes: stageCodes);

    internal static ActualCorrectionDecisionResult Invalid(ActualCorrectionRevisionIdentity? revision, IReadOnlyList<string> errors) =>
        new(ActualCorrectionDecisionOutcome.Invalid, revision, Errors: errors);

    internal static ActualCorrectionDecisionResult LifecycleConflict(ActualCorrectionRevisionIdentity revision, IReadOnlyList<string> errors) =>
        new(ActualCorrectionDecisionOutcome.LifecycleConflict, revision, Errors: errors);
}
