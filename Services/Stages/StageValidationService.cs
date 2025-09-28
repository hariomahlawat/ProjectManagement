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
        var normalizedTargetStatus = targetStatus.Trim();

        if (!Enum.TryParse(normalizedTargetStatus, ignoreCase: true, out StageStatus desiredStatus))
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

        if (targetDate.HasValue && targetDate.Value > today)
        {
            errors.Add("Date cannot be in the future.");
        }

        if (!StageTransitionPolicy.TryValidateTransition(stage.Status, desiredStatus, targetDate, out var transitionError))
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

        if (desiredStatus == StageStatus.Completed && !isHoD && !targetDate.HasValue)
        {
            errors.Add("Completion date is required.");
        }

        if (desiredStatus is StageStatus.InProgress or StageStatus.Completed)
        {
            var pncApplicable = await ResolvePncApplicabilityAsync(projectId, ct);
            var predecessors = StageDependencies.RequiredPredecessors(stage.StageCode)
                .Where(code => pncApplicable || !string.Equals(code, StageCodes.PNC, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (predecessors.Count > 0)
            {
                List<DateOnly>? predecessorCompletionDates = desiredStatus == StageStatus.Completed
                    ? new List<DateOnly>()
                    : null;

                foreach (var predecessorCode in predecessors)
                {
                    if (!stageLookup.TryGetValue(predecessorCode, out var predecessor) ||
                        predecessor.Status != StageStatus.Completed)
                    {
                        missingPredecessors.Add(predecessorCode);
                        continue;
                    }

                    if (desiredStatus == StageStatus.Completed && predecessor.CompletedOn.HasValue)
                    {
                        predecessorCompletionDates!.Add(predecessor.CompletedOn.Value);
                    }
                }

                if (desiredStatus == StageStatus.Completed && predecessorCompletionDates is { Count: > 0 })
                {
                    suggestedAutoStart = predecessorCompletionDates.Max();
                }
            }
        }

        if (desiredStatus == StageStatus.Completed &&
            targetDate.HasValue &&
            suggestedAutoStart.HasValue &&
            targetDate.Value < suggestedAutoStart.Value)
        {
            errors.Add($"Completion cannot be before latest predecessor completion ({suggestedAutoStart.Value:yyyy-MM-dd}).");

            if (isHoD)
            {
                warnings.Add("Completion before the latest predecessor requires a force override.");
            }
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

}
