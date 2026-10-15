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
using ProjectManagement.Services.Arpp;

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
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;

    public StageValidationService(
        ApplicationDbContext db,
        IClock clock,
        IProjectStageWorkflowPolicy workflowPolicy,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db);
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

        var ipaAuthority = await _arppIpaStageAuthority.ResolveAsync(projectId, ct);

        var stageLookup = stages
            .Where(item => !string.IsNullOrWhiteSpace(item.StageCode))
            .ToDictionary(item => item.StageCode, StringComparer.OrdinalIgnoreCase);

        if (!stageLookup.TryGetValue(normalizedStageCode, out var stage))
        {
            errors.Add("The requested stage was not found for this project.");
            return BuildResult();
        }

        if (desiredStatus == StageStatus.Skipped &&
            (stage.ActualStart.HasValue || stage.CompletedOn.HasValue || stage.Status is StageStatus.InProgress or StageStatus.Completed))
        {
            errors.Add("A stage that actually started or completed cannot be skipped. Correct its factual state through an authorised path first.");
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

        if (desiredStatus == StageStatus.InProgress && !targetDate.HasValue && !stage.ActualStart.HasValue)
        {
            errors.Add("Actual start is required before a stage can become InProgress. Supply and confirm a start date.");
        }

        if (desiredStatus == StageStatus.InProgress && stage.Status is StageStatus.Completed or StageStatus.Blocked
            && stage.ActualStart.HasValue && targetDate.HasValue && targetDate != stage.ActualStart)
        {
            errors.Add("Reopening preserves the recorded ActualStart. Correct the start separately through Actual Dates.");
        }

        var finalStart = desiredStatus == StageStatus.InProgress
            ? stage.ActualStart ?? targetDate
            : desiredStatus == StageStatus.Blocked ? stage.ActualStart : null;
        if (finalStart > today)
        {
            errors.Add("Recorded start date cannot be in the future under the operational IST date.");
        }

        if (desiredStatus == StageStatus.Completed && !targetDate.HasValue && !isHoD)
        {
            errors.Add("Completion date is required.");
        }

        if (desiredStatus is StageStatus.InProgress or StageStatus.Completed)
        {
            var workflow = await _workflowPolicy.GetAsync(projectId, ct);
            var suggestion = StageDateSuggestionResolver.Resolve(workflow, stages, stage.StageCode, ipaAuthority);
            suggestedAutoStart = suggestion.SuggestedStartDate;

            foreach (var predecessorCode in workflow.RequiredPredecessorClosure(stage.StageCode))
            {
                stageLookup.TryGetValue(predecessorCode, out var predecessor);
                var effectivePredecessor = StageEffectiveState.Resolve(predecessorCode,
                    predecessor is null ? null : new StageChronologyState(predecessor.Status, predecessor.CompletedOn), ipaAuthority);
                if (effectivePredecessor is null
                    || effectivePredecessor.Status is not StageStatus.Completed and not StageStatus.Skipped)
                {
                    missingPredecessors.Add(predecessorCode);
                }
            }

            var effectiveStartDate = desiredStatus == StageStatus.InProgress
                ? stage.ActualStart ?? targetDate
                : requestedStartDate ?? stage.ActualStart;

            var earliestAllowedStart = suggestion.EarliestAllowedStartDate;

            if (effectiveStartDate.HasValue
                && earliestAllowedStart.HasValue
                && effectiveStartDate.Value < earliestAllowedStart.Value)
            {
                var source = string.IsNullOrWhiteSpace(suggestion.SourceStageName)
                    ? "the effective predecessor"
                    : suggestion.SourceStageName;
                errors.Add(
                    $"Start date must be on or after {earliestAllowedStart.Value:yyyy-MM-dd}, " +
                    $"when {source} was completed. Same-day commencement is permitted.");
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
                    && earliestAllowedStart.HasValue
                    && targetDate.Value < earliestAllowedStart.Value)
                {
                    var source = string.IsNullOrWhiteSpace(suggestion.SourceStageName)
                        ? "the effective predecessor"
                        : suggestion.SourceStageName;
                    errors.Add(
                        $"Completion date must be on or after {earliestAllowedStart.Value:yyyy-MM-dd}, " +
                        $"when {source} was completed. Same-day completion is permitted.");
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
