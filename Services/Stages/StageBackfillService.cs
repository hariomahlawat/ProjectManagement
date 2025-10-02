using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Stages;

public sealed class StageBackfillService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public StageBackfillService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<StageBackfillResult> ApplyAsync(
        int projectId,
        IReadOnlyCollection<StageBackfillUpdate> updates,
        string userId,
        CancellationToken ct = default)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(userId));
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
            .Select(group =>
            {
                var item = group.Last();
                return new StageBackfillUpdate(
                    group.Key,
                    item.ActualStart,
                    item.CompletedOn);
            })
            .ToArray();

        if (distinctUpdates.Length == 0)
        {
            throw new StageBackfillValidationException(new[]
            {
                "At least one stage update is required."
            });
        }

        var stageCodes = distinctUpdates.Select(u => u.StageCode).ToArray();

        var stageLookup = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId && stageCodes.Contains(s.StageCode))
            .ToDictionaryAsync(s => s.StageCode!, StringComparer.OrdinalIgnoreCase, ct);

        if (stageLookup.Count != stageCodes.Length)
        {
            var missing = stageCodes
                .Where(code => !stageLookup.ContainsKey(code))
                .ToArray();

            throw new StageBackfillNotFoundException(missing);
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime.Date);
        var validationErrors = new List<string>();
        var conflicts = new List<string>();

        foreach (var update in distinctUpdates)
        {
            var stage = stageLookup[update.StageCode];

            if (stage.Status != StageStatus.Completed)
            {
                conflicts.Add(update.StageCode);
                continue;
            }

            if (!StageBackfillRules.RequiresBackfill(stage))
            {
                conflicts.Add(update.StageCode);
                continue;
            }

            var start = update.ActualStart ?? stage.ActualStart;
            var completed = update.CompletedOn ?? stage.CompletedOn;

            if (start is null)
            {
                validationErrors.Add($"Stage {update.StageCode}: actual start date is required.");
            }
            else if (start > today)
            {
                validationErrors.Add($"Stage {update.StageCode}: actual start date cannot be in the future.");
            }

            if (completed is null)
            {
                validationErrors.Add($"Stage {update.StageCode}: completion date is required.");
            }
            else if (completed > today)
            {
                validationErrors.Add($"Stage {update.StageCode}: completion date cannot be in the future.");
            }

            if (start is not null && completed is not null && start > completed)
            {
                validationErrors.Add($"Stage {update.StageCode}: completion date must be on or after the start date.");
            }
        }

        if (validationErrors.Count > 0)
        {
            throw new StageBackfillValidationException(validationErrors);
        }

        if (conflicts.Count > 0)
        {
            throw new StageBackfillConflictException(conflicts.ToArray());
        }

        var now = _clock.UtcNow;
        var updatedCodes = new List<string>(distinctUpdates.Length);

        foreach (var update in distinctUpdates)
        {
            var stage = stageLookup[update.StageCode];
            var originalActualStart = stage.ActualStart;
            var originalCompletedOn = stage.CompletedOn;

            var resolvedStart = update.ActualStart ?? stage.ActualStart!;
            var resolvedCompleted = update.CompletedOn ?? stage.CompletedOn!;

            stage.ActualStart = resolvedStart;
            stage.CompletedOn = resolvedCompleted;
            stage.RequiresBackfill = false;
            stage.IsAutoCompleted = false;
            stage.AutoCompletedFromCode = null;

            var log = new StageChangeLog
            {
                ProjectId = stage.ProjectId,
                StageCode = stage.StageCode,
                Action = StageBackfillLogAction,
                FromStatus = stage.Status.ToString(),
                ToStatus = stage.Status.ToString(),
                FromActualStart = originalActualStart,
                ToActualStart = resolvedStart,
                FromCompletedOn = originalCompletedOn,
                ToCompletedOn = resolvedCompleted,
                UserId = userId,
                At = now,
                Note = "Dates supplied via backfill workflow."
            };

            await _db.StageChangeLogs.AddAsync(log, ct);
            updatedCodes.Add(stage.StageCode);
        }

        await _db.SaveChangesAsync(ct);

        return new StageBackfillResult(updatedCodes.Count, updatedCodes.ToArray());
    }

    private const string StageBackfillLogAction = "Backfill";
}

public sealed record StageBackfillUpdate(string StageCode, DateOnly? ActualStart, DateOnly? CompletedOn);

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

public sealed class StageBackfillNotFoundException : Exception
{
    public StageBackfillNotFoundException(IReadOnlyList<string> missingStageCodes)
        : base("Stage backfill target not found.")
    {
        MissingStageCodes = missingStageCodes ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> MissingStageCodes { get; }
}
