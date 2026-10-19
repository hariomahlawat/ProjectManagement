using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Plans;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Authoritative mandatory completion-date backfill. Validation, publication and
/// stage history run inside one transaction under the project gate, against
/// row-locked stage state, using the shared final-state Actual validator.
/// </summary>
public sealed class StageBackfillService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IActualTimelineValidationService _validator;
    private readonly IAuditService? _audit;
    private readonly ILogger<StageBackfillService> _logger;

    public StageBackfillService(
        ApplicationDbContext db,
        IClock clock,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null,
        IProjectStageWorkflowPolicy? workflowPolicy = null,
        IActualTimelineValidationService? validator = null,
        IAuditService? audit = null,
        ILogger<StageBackfillService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _validator = validator ?? new ActualTimelineValidationService(
            workflowPolicy ?? new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()),
            arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db),
            clock);
        _audit = audit;
        _logger = logger ?? NullLogger<StageBackfillService>.Instance;
    }

    public async Task<StageBackfillResult> ApplyAsync(
        int projectId,
        IReadOnlyCollection<StageBackfillUpdate> updates,
        ActualWriteActor actor,
        CancellationToken ct = default,
        int? baselineVersion = null)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        ArgumentNullException.ThrowIfNull(actor);
        var userId = actor.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(actor));
        }

        if (updates is null || updates.Count == 0)
        {
            throw new StageBackfillValidationException(new[]
            {
                "At least one stage update is required."
            });
        }

        var distinctUpdates = updates
            .Where(u => u is not null && !string.IsNullOrWhiteSpace(u.StageCode))
            .GroupBy(u => u.StageCode!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last() with { StageCode = group.Key })
            .ToArray();

        if (distinctUpdates.Length == 0)
        {
            throw new StageBackfillValidationException(new[]
            {
                "At least one stage update is required."
            });
        }

        // SECTION: Protected read
        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, ct);
        await PlanRevisionConsistency.LockProjectAsync(_db, projectId, ct);

        // Authority and active-project status are re-established against the
        // locked project row, for every actor including Admin and HoD.
        await ActualWriteAuthority.EnsureAsync(_db, projectId, actor, ct);

        var allProjectStages = await ActualFactPublisher.LoadProjectStagesForUpdateAsync(_db, projectId, ct);
        var stageLookup = allProjectStages
            .Where(s => !string.IsNullOrWhiteSpace(s.StageCode))
            .ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);

        var missing = distinctUpdates
            .Select(update => update.StageCode)
            .Where(code => !stageLookup.ContainsKey(code))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new StageBackfillNotFoundException(missing);
        }

        // SECTION: Stale-editor protection (every backfill row writes)
        if (baselineVersion == ActualStageToken.BaselineVersion)
        {
            var stale = distinctUpdates
                .Where(update => !ActualStageToken.Matches(update.StageToken, stageLookup[update.StageCode]))
                .Select(update => stageLookup[update.StageCode].StageCode)
                .ToArray();
            if (stale.Length > 0)
            {
                throw new StageBackfillStaleException(stale);
            }
        }
        else
        {
            _logger.LogWarning(
                "Accepted a legacy backfill post without a baseline marker for project {ProjectId}.",
                projectId);
        }

        // SECTION: Final-state validation
        var validation = await _validator.ValidateAsync(
            new ActualTimelineCandidate(
                projectId,
                allProjectStages.Select(ActualStageState.From).ToArray(),
                distinctUpdates
                    .Select(update => new ActualStageChange(update.StageCode, update.ActualStart, CompletedOn: update.CompletedOn))
                    .ToArray(),
                ActualChangeEntryPoint.Backfill),
            ct);

        // Preserve the existing response contract: validation failures (422)
        // take precedence over stages that no longer need backfill (409).
        var validationErrors = validation.Errors
            .Where(error => error.Rule != ActualValidationRule.NotAwaitingBackfill)
            .Select(error => error.Message)
            .ToArray();
        if (validationErrors.Length > 0)
        {
            throw new StageBackfillValidationException(validationErrors);
        }

        var conflicts = validation.Errors
            .Where(error => error.Rule == ActualValidationRule.NotAwaitingBackfill)
            .Select(error => error.StageCode)
            .ToArray();
        if (conflicts.Length > 0)
        {
            throw new StageBackfillConflictException(conflicts);
        }

        // SECTION: Atomic publication (facts and mandatory history)
        var now = _clock.UtcNow;
        var updatedCodes = new List<string>(validation.Changes.Count);
        foreach (var change in validation.Changes)
        {
            var log = ActualFactPublisher.Apply(
                stageLookup[change.StageCode],
                change,
                userId,
                now,
                ActualFactPublisher.BackfillAction,
                "Dates supplied via backfill workflow.");
            await _db.StageChangeLogs.AddAsync(log, ct);
            updatedCodes.Add(change.StageCode);
        }

        await _db.SaveChangesAsync(ct);

        if (_audit is not null && updatedCodes.Count > 0)
        {
            var auditData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                ["StageCodes"] = string.Join(",", updatedCodes)
            };

            // Secondary audit only after the facts and their history commit.
            transaction.RegisterAfterCommit(_ => _audit.LogAsync(
                "Projects.StageBackfilled",
                userId: userId,
                data: auditData));
        }

        await transaction.CommitAsync(ct);

        foreach (var error in transaction.PostCommitErrors)
        {
            _logger.LogError(error, "Post-commit audit failed after stage backfill for project {ProjectId}.", projectId);
        }

        return new StageBackfillResult(updatedCodes.Count, updatedCodes.ToArray());
    }
}

public sealed record StageBackfillUpdate(
    string StageCode,
    DateOnly? ActualStart,
    DateOnly? CompletedOn,
    string? StageToken = null);

public sealed record StageBackfillResult(int UpdatedCount, IReadOnlyList<string> StageCodes);

public sealed class StageBackfillValidationException : Exception
{
    public StageBackfillValidationException(IReadOnlyList<string> details)
        : base("Validation failed for backfill input.")
    {
        Details = details ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Details { get; }
}

public sealed class StageBackfillConflictException : Exception
{
    public StageBackfillConflictException(IReadOnlyList<string> conflictingStages)
        : base("One or more stages cannot be backfilled.")
    {
        ConflictingStages = conflictingStages ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> ConflictingStages { get; }
}

/// <summary>A stage changed after the backfill form was rendered; nothing was saved.</summary>
public sealed class StageBackfillStaleException : Exception
{
    public StageBackfillStaleException(IReadOnlyList<string> staleStages)
        : base("One or more stages changed since the backfill form was opened.")
    {
        StaleStages = staleStages ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> StaleStages { get; }
}

public sealed class StageBackfillNotFoundException : Exception
{
    public StageBackfillNotFoundException(IReadOnlyList<string> missingStageCodes)
        : base("Stage backfill target not found.")
    {
        MissingStageCodes = missingStageCodes ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> MissingStageCodes { get; }
}
