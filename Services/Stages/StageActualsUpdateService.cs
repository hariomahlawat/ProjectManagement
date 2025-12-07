using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
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

    public StageActualsUpdateService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        ILogger<StageActualsUpdateService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        var stageLookup = await _db.ProjectStages
            .Where(s => s.ProjectId == input.ProjectId && codes.Contains(s.StageCode))
            .ToDictionaryAsync(s => s.StageCode!, StringComparer.OrdinalIgnoreCase, cancellationToken);

        if (stageLookup.Count != codes.Length)
        {
            var missing = codes.Where(code => !stageLookup.ContainsKey(code)).ToArray();
            throw new StageActualsNotFoundException(missing);
        }

        var lockedStages = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(r => r.ProjectId == input.ProjectId && r.DecisionStatus == PendingDecisionStatus && codes.Contains(r.StageCode))
            .Select(r => r.StageCode)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime.Date);
        var now = _clock.UtcNow;
        var validationErrors = new List<string>();
        var changes = new List<StageActualChange>();

        foreach (var row in normalized)
        {
            var stage = stageLookup[row.StageCode];
            var start = row.ActualStart;
            var completed = row.CompletedOn;
            var resolvedStart = start ?? stage.ActualStart;
            var resolvedCompleted = completed ?? stage.CompletedOn;

            if (resolvedStart is not null && resolvedStart > today)
            {
                validationErrors.Add($"{row.StageCode}: Start date cannot be in the future.");
            }

            if (resolvedCompleted is not null && resolvedCompleted > today)
            {
                validationErrors.Add($"{row.StageCode}: Completion date cannot be in the future.");
            }

            if (resolvedStart is not null && resolvedCompleted is not null && resolvedStart > resolvedCompleted)
            {
                validationErrors.Add($"{row.StageCode}: Completion date must be on or after the start date.");
            }

            switch (stage.Status)
            {
                case StageStatus.NotStarted when resolvedStart.HasValue || resolvedCompleted.HasValue:
                    validationErrors.Add($"{row.StageCode}: Cannot add actual dates until the stage has started.");
                    break;
                case StageStatus.Skipped when resolvedStart.HasValue || resolvedCompleted.HasValue:
                    validationErrors.Add($"{row.StageCode}: Skipped stages cannot record actual dates.");
                    break;
                case StageStatus.InProgress when resolvedCompleted.HasValue && !resolvedStart.HasValue:
                case StageStatus.Blocked when resolvedCompleted.HasValue && !resolvedStart.HasValue:
                    validationErrors.Add($"{row.StageCode}: Start date is required before marking completion.");
                    break;
                case StageStatus.Completed when !resolvedStart.HasValue || !resolvedCompleted.HasValue:
                    validationErrors.Add($"{row.StageCode}: Completed stages require both start and completion dates.");
                    break;
            }

            var updatedStart = resolvedStart;
            var updatedCompleted = resolvedCompleted;

            if (updatedStart == stage.ActualStart && updatedCompleted == stage.CompletedOn)
            {
                continue;
            }

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
            stage.RequiresBackfill = false;
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
}

// SECTION: Result records and exceptions
public sealed record StageActualsUpdateResult(int UpdatedCount, IReadOnlyList<string> StageCodes)
{
    public static StageActualsUpdateResult NoChanges() => new(0, Array.Empty<string>());
}

internal sealed record StageActualChange(ProjectStage Stage, DateOnly? NewStart, DateOnly? NewCompleted);

// SECTION: Helpers
internal static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
