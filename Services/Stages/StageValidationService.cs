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
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;

    public StageValidationService(
        ApplicationDbContext db,
        IClock clock,
        IProjectStageWorkflowPolicy workflowPolicy)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
    }

    public async Task<StageValidationResult> ValidateAsync(
        int projectId,
        string stageCode,
        string targetStatus,
        DateOnly? targetDate,
        bool isHoD,
        CancellationToken ct = default)
    {
        // SECTION: Initialization
        var errors = new List<string>();
        var warnings = new List<string>();
        var missingPredecessors = new List<string>();
        DateOnly? suggestedAutoStart = null;

        // SECTION: Basic input validation
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

        // SECTION: Date validation
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, IndiaTimeZone).Date);

        if (targetDate.HasValue && targetDate.Value > today)
        {
            errors.Add("Date cannot be in the future.");
        }

        if (!StageTransitionPolicy.TryValidateTransition(stage.Status, desiredStatus, targetDate, out var transitionError))
        {
            errors.Add(transitionError ?? "The requested transition is invalid.");
        }

        if (desiredStatus == StageStatus.Completed && !targetDate.HasValue && !isHoD)
        {
            errors.Add("Completion date is required.");
        }

        if (desiredStatus is StageStatus.InProgress or StageStatus.Completed)
        {
            var workflow = await _workflowPolicy.GetAsync(projectId, ct);
            var predecessors = workflow.RequiredPredecessorClosure(stage.StageCode);

            if (predecessors.Count > 0)
            {
                List<DateOnly>? predecessorCompletionDates = desiredStatus == StageStatus.Completed
                    ? new List<DateOnly>()
                    : null;

                foreach (var predecessorCode in predecessors)
                {
                    if (!stageLookup.TryGetValue(predecessorCode, out var predecessor)
                        || predecessor.Status is not StageStatus.Completed and not StageStatus.Skipped)
                    {
                        missingPredecessors.Add(predecessorCode);
                        continue;
                    }

                    if (desiredStatus == StageStatus.Completed
                        && predecessor.Status == StageStatus.Completed
                        && predecessor.CompletedOn.HasValue)
                    {
                        predecessorCompletionDates!.Add(predecessor.CompletedOn.Value);
                    }
                }

                if (desiredStatus == StageStatus.Completed && predecessorCompletionDates is { Count: > 0 })
                {
                    suggestedAutoStart = predecessorCompletionDates.Max().AddDays(1);
                }
            }
        }

        if (desiredStatus == StageStatus.Completed &&
            targetDate.HasValue &&
            suggestedAutoStart.HasValue &&
            targetDate.Value < suggestedAutoStart.Value)
        {
            errors.Add($"Completion cannot be before the inferred stage start ({suggestedAutoStart.Value:yyyy-MM-dd}).");

            if (isHoD)
            {
                warnings.Add("Completion before the inferred stage start requires correction.");
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


}
