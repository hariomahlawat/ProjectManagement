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

public sealed record StageValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MissingPredecessors,
    DateOnly? SuggestedAutoStart);

public interface IStageValidationService
{
    Task<StageValidationResult> ValidateAsync(
        int projectId,
        string stageCode,
        string targetStatus,
        DateOnly? targetDate,
        bool isHoD,
        CancellationToken ct = default);
}

public sealed class StageValidationService : IStageValidationService
{
    private static readonly TimeZoneInfo IndiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public StageValidationService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<StageValidationResult> ValidateAsync(
        int projectId,
        string stageCode,
        string targetStatus,
        DateOnly? targetDate,
        bool isHoD,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var missingPredecessors = new List<string>();
        DateOnly? suggestedAutoStart = null;

        if (string.IsNullOrWhiteSpace(stageCode))
        {
            errors.Add("A stage code is required.");
            return BuildResult();
        }

        if (string.IsNullOrWhiteSpace(targetStatus))
        {
            errors.Add("A target status is required.");
            return BuildResult();
        }

        var normalizedStageCode = stageCode.Trim().ToUpperInvariant();
        var normalizedStatus = targetStatus.Trim();

        if (!Enum.TryParse<StageStatus>(normalizedStatus, ignoreCase: true, out var desiredStatus))
        {
            errors.Add("The target status is not recognised.");
            return BuildResult();
        }

        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(ct);

        if (stages.Count == 0)
        {
            errors.Add("No stages were found for this project.");
            return BuildResult();
        }

        var stageLookup = stages.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);

        if (!stageLookup.TryGetValue(normalizedStageCode, out var stage))
        {
            errors.Add("The requested stage was not found for this project.");
            return BuildResult();
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, IndiaTimeZone).Date);

        if (!IsTransitionAllowed(stage.Status, desiredStatus, targetDate, out var transitionError))
        {
            if (!string.IsNullOrEmpty(transitionError))
            {
                errors.Add(transitionError);
            }
            else
            {
                errors.Add($"Changing from {stage.Status} to {desiredStatus} is not allowed.");
            }
        }

        if (desiredStatus == StageStatus.InProgress && targetDate.HasValue)
        {
            if (targetDate.Value > today)
            {
                errors.Add("Actual start date cannot be in the future.");
            }
        }

        if (desiredStatus == StageStatus.Completed)
        {
            if (!targetDate.HasValue && !isHoD)
            {
                errors.Add("A completion date is required when completing a stage.");
            }

            if (targetDate.HasValue && targetDate.Value > today)
            {
                errors.Add("Completion date cannot be in the future.");
            }

            if (targetDate.HasValue && stage.ActualStart.HasValue && targetDate.Value < stage.ActualStart.Value)
            {
                errors.Add("Completion date cannot be before the actual start date.");
            }
        }

        var requiresDependencies = desiredStatus is StageStatus.InProgress or StageStatus.Completed;

        if (requiresDependencies)
        {
            var pncApplicable = await ResolvePncApplicabilityAsync(projectId, ct);
            var predecessors = StageDependencies.RequiredPredecessors(stage.StageCode)
                .Where(code => pncApplicable || !string.Equals(code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (predecessors.Count > 0)
            {
                var completedDates = new List<DateOnly>();

                foreach (var predecessorCode in predecessors)
                {
                    if (!stageLookup.TryGetValue(predecessorCode, out var predecessor) || predecessor.Status != StageStatus.Completed)
                    {
                        missingPredecessors.Add(predecessorCode);
                        continue;
                    }

                    if (predecessor.CompletedOn.HasValue)
                    {
                        completedDates.Add(predecessor.CompletedOn.Value);
                    }
                }

                if (completedDates.Count > 0)
                {
                    suggestedAutoStart = completedDates.Max();
                }
            }
        }

        if (desiredStatus == StageStatus.Completed && targetDate.HasValue && suggestedAutoStart.HasValue && targetDate.Value < suggestedAutoStart.Value)
        {
            var message = $"Completion date cannot be earlier than {suggestedAutoStart.Value:yyyy-MM-dd}, when the latest predecessor completed.";
            if (isHoD)
            {
                warnings.Add("Completion before the latest predecessor requires a force override.");
            }

            errors.Add(message);
        }

        return BuildResult();

        StageValidationResult BuildResult()
        {
            var isValid = errors.Count == 0 && missingPredecessors.Count == 0;
            return new StageValidationResult(
                isValid,
                errors.AsReadOnly(),
                warnings.AsReadOnly(),
                missingPredecessors.AsReadOnly(),
                suggestedAutoStart);
        }
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

    private static bool IsTransitionAllowed(StageStatus current, StageStatus target, DateOnly? targetDate, out string? error)
    {
        error = null;

        if (current == target)
        {
            error = "The stage is already in the requested status.";
            return false;
        }

        return target switch
        {
            StageStatus.InProgress => ValidateStartTransition(current, targetDate, out error),
            StageStatus.Completed => ValidateCompleteTransition(current, out error),
            StageStatus.Blocked => ValidateBlockTransition(current, out error),
            StageStatus.Skipped => ValidateSkipTransition(current, out error),
            StageStatus.NotStarted => ValidateReopenTransition(current, out error),
            _ => false
        };
    }

    private static bool ValidateStartTransition(StageStatus current, DateOnly? targetDate, out string? error)
    {
        error = null;

        return current switch
        {
            StageStatus.NotStarted => true,
            StageStatus.Blocked => true,
            StageStatus.Skipped => true,
            StageStatus.Completed => targetDate.HasValue
                ? true
                : (error = "Reopening to InProgress requires an actual start date.", false),
            _ => (error = $"Changing from {current} to {StageStatus.InProgress} is not allowed.", false)
        };
    }

    private static bool ValidateCompleteTransition(StageStatus current, out string? error)
    {
        return current switch
        {
            StageStatus.NotStarted => true,
            StageStatus.InProgress => true,
            _ => (error = $"Changing from {current} to {StageStatus.Completed} is not allowed.", false)
        };
    }

    private static bool ValidateBlockTransition(StageStatus current, out string? error)
    {
        if (current == StageStatus.Completed)
        {
            error = "Completed stages cannot be blocked.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool ValidateSkipTransition(StageStatus current, out string? error)
    {
        if (current != StageStatus.NotStarted)
        {
            error = "Only stages that have not started can be skipped.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool ValidateReopenTransition(StageStatus current, out string? error)
    {
        if (current is StageStatus.Completed or StageStatus.Skipped or StageStatus.Blocked)
        {
            error = null;
            return true;
        }

        error = "Only completed, skipped, or blocked stages can be reopened.";
        return false;
    }
}
