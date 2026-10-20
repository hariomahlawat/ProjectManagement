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
using ProjectManagement.Services.Plans;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Stages;

// SECTION: Stage actuals updater
/// <summary>
/// Authoritative (HoD/Admin and, until Phase D-2, assigned PO) Actual-date
/// correction. Validation, publication and stage history run inside one
/// transaction under the project gate, against row-locked stage state.
/// </summary>
public sealed class StageActualsUpdateService
{
    private const string PendingDecisionStatus = "Pending";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<StageActualsUpdateService> _logger;
    private readonly IActualTimelineValidationService _validator;

    public StageActualsUpdateService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        ILogger<StageActualsUpdateService> logger,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null,
        IProjectStageWorkflowPolicy? workflowPolicy = null,
        IActualTimelineValidationService? validator = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? new ActualTimelineValidationService(
            workflowPolicy ?? new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()),
            arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db),
            clock);
    }

    public async Task<StageActualsUpdateResult> UpdateAsync(
        ActualsEditInput input,
        ActualWriteActor actor,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        ArgumentNullException.ThrowIfNull(actor);
        var userId = actor.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(actor));
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

        // SECTION: Protected read
        // Every decision-critical read happens after the project gate and the row
        // lock, so validation and publication see the same committed state.
        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        await PlanRevisionConsistency.LockProjectAsync(_db, input.ProjectId, cancellationToken);

        // The page authorized before this transaction; authority is re-established
        // here against the locked project row so a reassignment or deletion cannot
        // slip in between the check and publication.
        await ActualWriteAuthority.EnsureAsync(_db, input.ProjectId, actor, cancellationToken);

        var allProjectStages = await ActualFactPublisher.LoadProjectStagesForUpdateAsync(
            _db,
            input.ProjectId,
            cancellationToken);

        var stageLookup = allProjectStages
            .Where(s => !string.IsNullOrWhiteSpace(s.StageCode))
            .ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);

        var missing = normalized
            .Select(row => row.StageCode)
            .Where(code => !stageLookup.ContainsKey(code))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new StageActualsNotFoundException(missing);
        }

        // SECTION: Stale-editor protection
        var stale = FindStaleRows(input, normalized, stageLookup);
        if (stale.Count > 0)
        {
            throw new StageActualsStaleException(stale);
        }

        // SECTION: Final-state validation
        var validation = await _validator.ValidateAsync(
            new ActualTimelineCandidate(
                input.ProjectId,
                allProjectStages.Select(ActualStageState.From).ToArray(),
                normalized
                    .Select(row => new ActualStageChange(row.StageCode, row.ActualStart, row.ClearActualStart, row.CompletedOn))
                    .ToArray(),
                ActualChangeEntryPoint.Editor),
            cancellationToken);

        var changedCodes = validation.Changes
            .Select(change => change.StageCode)
            .ToArray();

        var lockedStages = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(r => r.ProjectId == input.ProjectId
                && r.DecisionStatus == PendingDecisionStatus
                && changedCodes.Contains(r.StageCode))
            .Select(r => r.StageCode)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (lockedStages.Length > 0)
        {
            throw new StageActualsConflictException(lockedStages);
        }

        if (!validation.IsValid)
        {
            throw new StageActualsValidationException(validation.Errors.Select(error => error.Message).ToArray());
        }

        if (validation.Changes.Count == 0)
        {
            return StageActualsUpdateResult.NoChanges();
        }

        // SECTION: Atomic publication (facts and mandatory history)
        var auditData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectId"] = input.ProjectId.ToString(CultureInfo.InvariantCulture),
            ["UpdatedCount"] = validation.Changes.Count.ToString(CultureInfo.InvariantCulture)
        };

        var now = _clock.UtcNow;
        for (var i = 0; i < validation.Changes.Count; i++)
        {
            var change = validation.Changes[i];
            var prefix = $"Stage[{i}]";
            auditData[$"{prefix}.Code"] = change.StageCode;
            auditData[$"{prefix}.From.Start"] = FormatDate(change.Official.ActualStart);
            auditData[$"{prefix}.From.Completed"] = FormatDate(change.Official.CompletedOn);
            auditData[$"{prefix}.To.Start"] = FormatDate(change.Final.ActualStart);
            auditData[$"{prefix}.To.Completed"] = FormatDate(change.Final.CompletedOn);

            var log = ActualFactPublisher.Apply(
                stageLookup[change.StageCode],
                change,
                userId,
                now,
                change.IsBackfill ? ActualFactPublisher.BackfillAction : ActualFactPublisher.ActualsUpdatedAction,
                change.IsBackfill
                    ? "Completion date supplied via the actual-dates editor."
                    : "Actual dates updated via bulk editor.");
            await _db.StageChangeLogs.AddAsync(log, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Secondary security/usage audit only after the facts and history commit;
        // it must never decide whether they commit.
        transaction.RegisterAfterCommit(_ => _audit.LogAsync(
            "Projects.ActualsUpdated",
            userId: userId,
            userName: userName,
            data: auditData));

        await transaction.CommitAsync(cancellationToken);

        foreach (var error in transaction.PostCommitErrors)
        {
            _logger.LogError(error, "Post-commit audit failed after updating actual dates for project {ProjectId}.", input.ProjectId);
        }

        _logger.LogInformation(
            "Updated actual dates for {Count} stage(s) in project {ProjectId} by user {UserId}",
            validation.Changes.Count,
            input.ProjectId,
            userId);

        return new StageActualsUpdateResult(
            validation.Changes.Count,
            changedCodes,
            validation.Warnings.Select(warning => warning.Message).ToArray());
    }

    // SECTION: Helpers
    private IReadOnlyList<string> FindStaleRows(
        ActualsEditInput input,
        IReadOnlyList<ActualsEditRowInput> rows,
        IReadOnlyDictionary<string, ProjectStage> stageLookup)
    {
        var wouldChange = rows
            .Where(row => WouldChange(row, stageLookup[row.StageCode]))
            .ToArray();

        if (input.BaselineVersion != ActualStageToken.BaselineVersion)
        {
            // Forms rendered before baseline tokens existed keep the previous
            // contract (Phase D-1 compatibility); log so the gap is observable.
            if (wouldChange.Length > 0)
            {
                _logger.LogWarning(
                    "Accepted a legacy actual-dates post without a baseline marker for project {ProjectId}.",
                    input.ProjectId);
            }

            return Array.Empty<string>();
        }

        return wouldChange
            .Where(row => !ActualStageToken.Matches(row.StageToken, stageLookup[row.StageCode]))
            .Select(row => stageLookup[row.StageCode].StageCode)
            .ToArray();
    }

    private static bool WouldChange(ActualsEditRowInput row, ProjectStage stage) =>
        ActualStageToken.WouldChange(stage, row.ActualStart, row.ClearActualStart, row.CompletedOn);

    private static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// SECTION: Result records and exceptions
public sealed record StageActualsUpdateResult(
    int UpdatedCount,
    IReadOnlyList<string> StageCodes,
    IReadOnlyList<string>? Warnings = null)
{
    public static StageActualsUpdateResult NoChanges() => new(0, Array.Empty<string>());
}

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

/// <summary>A stage changed after the editor was rendered; nothing was saved.</summary>
public sealed class StageActualsStaleException : Exception
{
    public StageActualsStaleException(IReadOnlyList<string> stageCodes)
        : base("One or more stages changed since the editor was opened.")
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
