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

    Task<StageValidationResult> ValidateAsync(
        int projectId,
        string stageCode,
        string targetStatus,
        DateOnly? targetDate,
        DateOnly? requestedStartDate,
        bool isHoD,
        CancellationToken ct = default)
        => ValidateAsync(projectId, stageCode, targetStatus, targetDate, isHoD, ct);
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

    public Task<StageValidationResult> ValidateAsync(
        int projectId,
        string stageCode,
        string targetStatus,
        DateOnly? targetDate,
        bool isHoD,
        CancellationToken ct = default)
        => ValidateAsync(
            projectId,
            stageCode,
            targetStatus,
            targetDate,
            null,
            isHoD,
            ct);

    public async Task<StageValidationResult> ValidateAsync(
        int projectId,
        string stageCode,
        string targetStatus,
        DateOnly? targetDate,
        DateOnly? requestedStartDate,
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
            .Where(stage => stage.ProjectId == projectId)
            .ToListAsync(ct);

        if (stages.Count == 0)
        {
            errors.Add("No stages were found for this project.");
            return BuildResult();
        }

        var stageLookup = stages
            .Where(item => !string.IsNullOrWhiteSpace(item.StageCode))
            .ToDictionary(item => item.StageCode, StringComparer.OrdinalIgnoreCase);

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

        if (requestedStartDate.HasValue && requestedStartDate.Value > today)
        {
            errors.Add("Start date cannot be in the future.");
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
            var suggestion = StageDateSuggestionResolver.Resolve(workflow, stages, stage.StageCode);
            suggestedAutoStart = suggestion.SuggestedStartDate;

            foreach (var predecessorCode in workflow.RequiredPredecessorClosure(stage.StageCode))
            {
                if (!stageLookup.TryGetValue(predecessorCode, out var predecessor)
                    || predecessor.Status is not StageStatus.Completed and not StageStatus.Skipped)
                {
                    missingPredecessors.Add(predecessorCode);
                }
            }

            var effectiveStartDate = desiredStatus == StageStatus.InProgress
                ? targetDate
                : requestedStartDate ?? stage.ActualStart;

            if (effectiveStartDate.HasValue
                && suggestedAutoStart.HasValue
                && effectiveStartDate.Value < suggestedAutoStart.Value)
            {
                var source = string.IsNullOrWhiteSpace(suggestion.SourceStageName)
                    ? "the effective predecessor"
                    : suggestion.SourceStageName;
                errors.Add(
                    $"Start date must be on or after {suggestedAutoStart.Value:yyyy-MM-dd}, " +
                    $"the day after {source} completion.");
            }

            if (desiredStatus == StageStatus.Completed && targetDate.HasValue)
            {
                if (effectiveStartDate.HasValue && targetDate.Value < effectiveStartDate.Value)
                {
                    errors.Add(
                        $"Completion date cannot be before the selected start date " +
                        $"({effectiveStartDate.Value:yyyy-MM-dd}).");
                }
                else if (!effectiveStartDate.HasValue
                    && suggestedAutoStart.HasValue
                    && targetDate.Value < suggestedAutoStart.Value)
                {
                    var source = string.IsNullOrWhiteSpace(suggestion.SourceStageName)
                        ? "the effective predecessor"
                        : suggestion.SourceStageName;
                    errors.Add(
                        $"Completion date must be on or after {suggestedAutoStart.Value:yyyy-MM-dd}, " +
                        $"the day after {source} completion.");
                }
            }
        }

        return BuildResult();

        StageValidationResult BuildResult()
        {
            var normalizedMissing = missingPredecessors
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var isValid = errors.Count == 0 && normalizedMissing.Length == 0;
            return new StageValidationResult(
                isValid,
                errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                normalizedMissing,
                suggestedAutoStart);
        }
    }
}
