using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Records Project Officer stage-update proposals. Proposals are visible immediately,
/// but the approved project lifecycle is changed only by the HoD decision workflow.
/// Multiple stages may be proposed together and existing pending proposals on other
/// stages do not block further updates.
/// </summary>
public class StageRequestService
{
    private const string PendingDecisionStatus = "Pending";
    private const string RequestedLogAction = "Requested";
    private const string SupersededDecisionStatus = "Superseded";
    private const string SupersededLogAction = "Superseded";
    private const string SupersededNote = "Superseded by newer stage update";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IStageValidationService _validationService;
    private readonly IProjectStageWorkflowPolicy? _workflowPolicy;

    public StageRequestService(
        ApplicationDbContext db,
        IClock clock,
        IStageValidationService validationService,
        IProjectStageWorkflowPolicy? workflowPolicy = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _validationService = validationService
            ?? throw new ArgumentNullException(nameof(validationService));
        _workflowPolicy = workflowPolicy;
    }

    public async Task<StageRequestResult> CreateAsync(
        StageChangeRequestInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var batch = await CreateBatchAsync(
            new BatchStageChangeRequestInput
            {
                ProjectId = input.ProjectId,
                Stages = new[]
                {
                    new StageChangeRequestItemInput
                    {
                        StageCode = input.StageCode,
                        RequestedStatus = input.RequestedStatus,
                        RequestedDate = input.RequestedDate,
                        Note = input.Note
                    }
                }
            },
            userId,
            cancellationToken);

        var first = batch.Items.FirstOrDefault();
        if (first is not null)
        {
            return first.Result;
        }

        return batch.Outcome switch
        {
            BatchStageRequestOutcome.NotProjectOfficer => StageRequestResult.NotProjectOfficer(),
            BatchStageRequestOutcome.StageNotFound => StageRequestResult.StageNotFound(),
            _ => StageRequestResult.ValidationFailed(batch.Errors)
        };
    }

    public async Task<BatchStageRequestResult> CreateBatchAsync(
        BatchStageChangeRequestInput input,
        string userId,
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
            return BatchStageRequestResult.Invalid(new[] { "A valid project is required." });
        }

        var submittedItems = input.Stages?.ToArray() ?? Array.Empty<StageChangeRequestItemInput>();
        if (submittedItems.Length == 0)
        {
            return BatchStageRequestResult.Invalid(new[] { "Add at least one stage update." });
        }

        var preparedItems = new List<PreparedStageRequest>(submittedItems.Length);
        var inputErrors = new List<string>();

        for (var index = 0; index < submittedItems.Length; index++)
        {
            var item = submittedItems[index];
            var stageCode = item.StageCode?.Trim().ToUpperInvariant() ?? string.Empty;

            if (stageCode.Length == 0)
            {
                inputErrors.Add($"Update {index + 1}: select a stage.");
                continue;
            }

            if (!Enum.TryParse<StageStatus>(item.RequestedStatus?.Trim(), ignoreCase: true, out var requestedStatus))
            {
                inputErrors.Add($"{stageCode}: select a valid stage status.");
                continue;
            }

            preparedItems.Add(new PreparedStageRequest(
                index,
                stageCode,
                requestedStatus,
                item.RequestedDate,
                string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim()));
        }

        var duplicateStageCodes = preparedItems
            .GroupBy(item => item.StageCode, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateStageCodes.Length > 0)
        {
            inputErrors.Add(
                $"Include each stage once in a submission. You may revise it again at any time after submission: {string.Join(", ", duplicateStageCodes)}.");
        }

        if (inputErrors.Count > 0)
        {
            return BatchStageRequestResult.Invalid(DistinctMessages(inputErrors));
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Where(item => item.Id == input.ProjectId)
            .Select(item => new { item.Id, item.LeadPoUserId })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return BatchStageRequestResult.StageNotFoundResult();
        }

        if (!string.Equals(project.LeadPoUserId, userId, StringComparison.Ordinal))
        {
            return BatchStageRequestResult.NotProjectOfficerResult();
        }

        var stages = await _db.ProjectStages
            .Where(item => item.ProjectId == input.ProjectId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.StageCode)
            .ToListAsync(cancellationToken);

        var stageLookup = stages
            .Where(item => !string.IsNullOrWhiteSpace(item.StageCode))
            .ToDictionary(item => item.StageCode, StringComparer.OrdinalIgnoreCase);

        var missingStageItems = preparedItems
            .Where(item => !stageLookup.ContainsKey(item.StageCode))
            .Select(item => new StageRequestItemResult(item.StageCode, StageRequestResult.StageNotFound()))
            .ToArray();

        if (missingStageItems.Length > 0)
        {
            return BatchStageRequestResult.StageNotFoundResult(missingStageItems);
        }

        var pendingRequests = await _db.StageChangeRequests
            .Where(item => item.ProjectId == input.ProjectId && item.DecisionStatus == PendingDecisionStatus)
            .OrderBy(item => item.RequestedOn)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var startProposalHistory = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(item =>
                item.ProjectId == input.ProjectId
                && item.RequestedStatus == StageStatus.InProgress.ToString()
                && item.RequestedDate.HasValue
                && (item.DecisionStatus == PendingDecisionStatus
                    || item.DecisionStatus == SupersededDecisionStatus))
            .OrderBy(item => item.RequestedOn)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var carriedStartByStage = startProposalHistory
            .GroupBy(item => item.StageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.RequestedOn)
                .ThenByDescending(item => item.Id)
                .First())
            .ToDictionary(item => item.StageCode, item => item.RequestedDate, StringComparer.OrdinalIgnoreCase);

        ProjectStageWorkflowSnapshot? workflow = null;
        if (_workflowPolicy is not null)
        {
            workflow = await _workflowPolicy.GetAsync(input.ProjectId, cancellationToken);
        }

        var stageNames = BuildStageNameLookup(workflow, stages);
        var latestPendingByStage = pendingRequests
            .GroupBy(request => request.StageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(request => request.RequestedOn)
                .ThenByDescending(request => request.Id)
                .First())
            .ToDictionary(request => request.StageCode, StringComparer.OrdinalIgnoreCase);
        var projectedStates = BuildProjectedStates(stages, latestPendingByStage.Values, carriedStartByStage);
        var submittedStageCodes = preparedItems
            .Select(item => item.StageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Apply the entire submission to the in-memory projection first. A new
        // proposal replaces the earlier pending proposal for that same stage, so
        // reset that stage to its approved state before applying the replacement.
        // Other pending stages remain in the projection and may satisfy dependencies.
        foreach (var item in preparedItems)
        {
            var projectedStage = projectedStates[item.StageCode];
            var carriedStart = item.RequestedStatus == StageStatus.Completed
                ? projectedStage.ProjectedStartDate
                : null;

            ResetToOfficialProjection(projectedStage);
            if (carriedStart.HasValue && !projectedStage.OfficialActualStart.HasValue)
            {
                projectedStage.ProjectedStartDate = carriedStart;
            }

            ApplyProjection(projectedStage, item.RequestedStatus, item.RequestedDate);
        }

        var validationResults = new List<StageRequestItemResult>(preparedItems.Count);
        var hasValidationFailure = false;

        foreach (var item in preparedItems)
        {
            var stage = stageLookup[item.StageCode];
            var validation = await _validationService.ValidateAsync(
                input.ProjectId,
                item.StageCode,
                item.RequestedStatus.ToString(),
                item.RequestedDate,
                isHoD: false,
                cancellationToken);

            var errors = validation.Errors
                .Where(error => !IsPredecessorError(error))
                .ToList();

            var requiredPredecessors = workflow is not null
                ? workflow.RequiredPredecessorClosure(item.StageCode)
                : validation.MissingPredecessors;

            var unresolvedPredecessors = requiredPredecessors
                .Where(code => !IsProjectedResolved(projectedStates, code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (unresolvedPredecessors.Length > 0
                && item.RequestedStatus is StageStatus.InProgress or StageStatus.Completed)
            {
                var predecessorNames = unresolvedPredecessors
                    .Select(code => DisplayName(stageNames, code))
                    .ToArray();

                errors.Add(
                    $"{DisplayName(stageNames, item.StageCode)} cannot be {RequestedActionPhrase(item.RequestedStatus)} yet. " +
                    $"Complete or skip the required predecessor stage{(predecessorNames.Length == 1 ? string.Empty : "s")}: {string.Join(", ", predecessorNames)}.");
            }

            var requiresDecisionContext = item.RequestedStatus is StageStatus.Blocked or StageStatus.Skipped
                || (item.RequestedStatus == StageStatus.InProgress
                    && stage.Status is StageStatus.Blocked or StageStatus.Completed or StageStatus.Skipped);

            if (requiresDecisionContext && item.Note is null)
            {
                errors.Add("Add a note for a blocked, skipped, resumed or reopened stage update.");
            }

            ValidateProjectedChronology(
                item,
                requiredPredecessors,
                projectedStates,
                stageNames,
                errors);

            var normalizedErrors = DistinctMessages(errors);
            if (normalizedErrors.Count > 0)
            {
                hasValidationFailure = true;
                validationResults.Add(new StageRequestItemResult(
                    item.StageCode,
                    StageRequestResult.ValidationFailed(normalizedErrors, unresolvedPredecessors)));
            }
            else
            {
                validationResults.Add(new StageRequestItemResult(
                    item.StageCode,
                    StageRequestResult.Ready()));
            }
        }

        if (hasValidationFailure)
        {
            return BatchStageRequestResult.ValidationFailed(validationResults);
        }

        var untouchedPendingErrors = ValidateUntouchedPendingProposals(
            latestPendingByStage.Values,
            submittedStageCodes,
            workflow,
            projectedStates,
            stageLookup,
            stageNames);

        if (untouchedPendingErrors.Count > 0)
        {
            return BatchStageRequestResult.Invalid(untouchedPendingErrors);
        }

        var useTransaction = _db.Database.IsRelational();
        await using var transaction = useTransaction
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var now = _clock.UtcNow;
        var createdRequests = new List<(PreparedStageRequest Item, StageChangeRequest Request)>();

        foreach (var item in preparedItems.OrderBy(item => item.InputOrder))
        {
            var stage = stageLookup[item.StageCode];

            var sameStagePending = pendingRequests
                .Where(request => string.Equals(request.StageCode, item.StageCode, StringComparison.OrdinalIgnoreCase)
                    && request.DecisionStatus == PendingDecisionStatus)
                .ToArray();

            foreach (var pending in sameStagePending)
            {
                pending.DecisionStatus = SupersededDecisionStatus;
                pending.DecidedByUserId = userId;
                pending.DecidedOn = now;
                pending.DecisionNote = SupersededNote;

                await _db.StageChangeLogs.AddAsync(
                    new StageChangeLog
                    {
                        ProjectId = stage.ProjectId,
                        StageCode = stage.StageCode,
                        Action = SupersededLogAction,
                        FromStatus = stage.Status.ToString(),
                        ToStatus = pending.RequestedStatus,
                        FromActualStart = stage.ActualStart,
                        ToActualStart = stage.ActualStart,
                        FromCompletedOn = stage.CompletedOn,
                        ToCompletedOn = stage.CompletedOn,
                        UserId = userId,
                        At = now,
                        Note = SupersededNote
                    },
                    cancellationToken);
            }

            var request = new StageChangeRequest
            {
                ProjectId = stage.ProjectId,
                StageCode = stage.StageCode,
                RequestedStatus = item.RequestedStatus.ToString(),
                RequestedDate = item.RequestedDate,
                Note = item.Note,
                RequestedByUserId = userId,
                RequestedOn = now.AddTicks(item.InputOrder),
                DecisionStatus = PendingDecisionStatus
            };

            await _db.StageChangeRequests.AddAsync(request, cancellationToken);
            await _db.StageChangeLogs.AddAsync(
                new StageChangeLog
                {
                    ProjectId = stage.ProjectId,
                    StageCode = stage.StageCode,
                    Action = RequestedLogAction,
                    FromStatus = stage.Status.ToString(),
                    ToStatus = item.RequestedStatus.ToString(),
                    FromActualStart = stage.ActualStart,
                    ToActualStart = item.RequestedStatus == StageStatus.InProgress
                        ? item.RequestedDate
                        : item.RequestedStatus == StageStatus.Completed
                            ? projectedStates[item.StageCode].ProjectedStartDate
                            : stage.ActualStart,
                    FromCompletedOn = stage.CompletedOn,
                    ToCompletedOn = item.RequestedStatus == StageStatus.Completed ? item.RequestedDate : stage.CompletedOn,
                    UserId = userId,
                    At = now.AddTicks(item.InputOrder),
                    Note = item.Note
                },
                cancellationToken);

            createdRequests.Add((item, request));
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var successfulItems = createdRequests
            .OrderBy(pair => pair.Item.InputOrder)
            .Select(pair => new StageRequestItemResult(
                pair.Item.StageCode,
                StageRequestResult.Success(pair.Request.Id)))
            .ToArray();

        return BatchStageRequestResult.Success(successfulItems);
    }

    private static Dictionary<string, ProjectedStageState> BuildProjectedStates(
        IReadOnlyCollection<ProjectStage> stages,
        IReadOnlyCollection<StageChangeRequest> pendingRequests,
        IReadOnlyDictionary<string, DateOnly?> carriedStartByStage)
    {
        var states = stages.ToDictionary(
            stage => stage.StageCode,
            stage => new ProjectedStageState(
                stage.StageCode,
                stage.Status,
                stage.Status,
                stage.ActualStart,
                stage.CompletedOn,
                stage.ActualStart,
                stage.CompletedOn),
            StringComparer.OrdinalIgnoreCase);

        foreach (var state in states.Values)
        {
            if (!state.ProjectedStartDate.HasValue
                && carriedStartByStage.TryGetValue(state.StageCode, out var carriedStart)
                && carriedStart.HasValue)
            {
                state.ProjectedStartDate = carriedStart;
            }
        }

        foreach (var pending in pendingRequests)
        {
            if (!states.TryGetValue(pending.StageCode, out var state)
                || !Enum.TryParse<StageStatus>(pending.RequestedStatus, ignoreCase: true, out var status))
            {
                continue;
            }

            ApplyProjection(state, status, pending.RequestedDate);
        }

        return states;
    }

    private static void ResetToOfficialProjection(ProjectedStageState state)
    {
        state.ProjectedStatus = state.OfficialStatus;
        state.ProjectedStartDate = state.OfficialActualStart;
        state.ProjectedCompletionDate = state.OfficialCompletionDate;
        state.ProjectedDate = null;
    }

    private static void ApplyProjection(
        ProjectedStageState state,
        StageStatus requestedStatus,
        DateOnly? requestedDate)
    {
        state.ProjectedStatus = requestedStatus;
        state.ProjectedDate = requestedDate;

        switch (requestedStatus)
        {
            case StageStatus.Completed:
                state.ProjectedCompletionDate = requestedDate ?? state.OfficialCompletionDate;
                break;
            case StageStatus.InProgress:
                state.ProjectedStartDate = requestedDate ?? state.OfficialActualStart;
                state.ProjectedCompletionDate = null;
                break;
            case StageStatus.Blocked:
                state.ProjectedCompletionDate = null;
                break;
            case StageStatus.NotStarted:
                state.ProjectedStartDate = null;
                state.ProjectedCompletionDate = null;
                break;
            case StageStatus.Skipped:
                state.ProjectedCompletionDate = null;
                break;
        }
    }

    private static bool IsProjectedResolved(
        IReadOnlyDictionary<string, ProjectedStageState> projectedStates,
        string stageCode)
    {
        return projectedStates.TryGetValue(stageCode, out var state)
            && state.ProjectedStatus is StageStatus.Completed or StageStatus.Skipped;
    }

    private static void ValidateProjectedChronology(
        PreparedStageRequest item,
        IReadOnlyList<string> requiredPredecessors,
        IReadOnlyDictionary<string, ProjectedStageState> projectedStates,
        IReadOnlyDictionary<string, string> stageNames,
        ICollection<string> errors)
    {
        if (!item.RequestedDate.HasValue
            || item.RequestedStatus is not StageStatus.InProgress and not StageStatus.Completed)
        {
            return;
        }

        var projectedStage = projectedStates[item.StageCode];
        if (item.RequestedStatus == StageStatus.Completed
            && projectedStage.ProjectedStartDate.HasValue
            && item.RequestedDate.Value < projectedStage.ProjectedStartDate.Value)
        {
            errors.Add(
                $"{DisplayName(stageNames, item.StageCode)} cannot be completed before its recorded or projected start date " +
                $"({projectedStage.ProjectedStartDate.Value:dd MMM yyyy}).");
        }

        var predecessorBoundaries = requiredPredecessors
            .Select(code => projectedStates.TryGetValue(code, out var state)
                ? new { Code = code, Date = state.ProjectedCompletionDate }
                : null)
            .Where(item => item?.Date is not null)
            .Select(item => new { item!.Code, Date = item.Date!.Value })
            .ToArray();

        if (predecessorBoundaries.Length == 0)
        {
            return;
        }

        var latest = predecessorBoundaries
            .OrderByDescending(item => item.Date)
            .First();
        var earliestAllowed = latest.Date.AddDays(1);

        if (item.RequestedDate.Value < earliestAllowed)
        {
            errors.Add(
                $"{DisplayName(stageNames, item.StageCode)} date must be on or after {earliestAllowed:dd MMM yyyy}, " +
                $"the day after {DisplayName(stageNames, latest.Code)} completion.");
        }
    }

    private static IReadOnlyList<string> ValidateUntouchedPendingProposals(
        IEnumerable<StageChangeRequest> latestPendingRequests,
        IReadOnlySet<string> submittedStageCodes,
        ProjectStageWorkflowSnapshot? workflow,
        IReadOnlyDictionary<string, ProjectedStageState> projectedStates,
        IReadOnlyDictionary<string, ProjectStage> officialStages,
        IReadOnlyDictionary<string, string> stageNames)
    {
        if (workflow is null)
        {
            return Array.Empty<string>();
        }

        var errors = new List<string>();

        foreach (var pending in latestPendingRequests)
        {
            if (submittedStageCodes.Contains(pending.StageCode)
                || !officialStages.ContainsKey(pending.StageCode)
                || !Enum.TryParse<StageStatus>(pending.RequestedStatus, ignoreCase: true, out var pendingStatus)
                || pendingStatus is not StageStatus.InProgress and not StageStatus.Completed)
            {
                continue;
            }

            var requiredPredecessors = workflow.RequiredPredecessorClosure(pending.StageCode);
            var unresolvedPredecessors = requiredPredecessors
                .Where(code => !IsProjectedResolved(projectedStates, code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (unresolvedPredecessors.Length > 0)
            {
                var predecessorNames = unresolvedPredecessors
                    .Select(code => DisplayName(stageNames, code))
                    .ToArray();

                errors.Add(
                    $"The pending {DisplayName(stageNames, pending.StageCode)} update conflicts with this revised sequence because " +
                    $"the required predecessor stage{(predecessorNames.Length == 1 ? string.Empty : "s")} would no longer be complete or skipped: " +
                    $"{string.Join(", ", predecessorNames)}. Include {DisplayName(stageNames, pending.StageCode)} in this submission to revise it.");
                continue;
            }

            if (!pending.RequestedDate.HasValue)
            {
                continue;
            }

            var projectedStage = projectedStates[pending.StageCode];
            if (pendingStatus == StageStatus.Completed
                && projectedStage.ProjectedStartDate.HasValue
                && pending.RequestedDate.Value < projectedStage.ProjectedStartDate.Value)
            {
                errors.Add(
                    $"The pending {DisplayName(stageNames, pending.StageCode)} completion dated {pending.RequestedDate.Value:dd MMM yyyy} " +
                    $"is before its recorded start date ({projectedStage.ProjectedStartDate.Value:dd MMM yyyy}). " +
                    $"Include {DisplayName(stageNames, pending.StageCode)} in this submission to revise it.");
                continue;
            }

            var predecessorBoundaries = requiredPredecessors
                .Select(code => projectedStates.TryGetValue(code, out var state)
                    ? new { Code = code, Date = state.ProjectedCompletionDate }
                    : null)
                .Where(item => item?.Date is not null)
                .Select(item => new { item!.Code, Date = item.Date!.Value })
                .ToArray();

            if (predecessorBoundaries.Length == 0)
            {
                continue;
            }

            var latest = predecessorBoundaries
                .OrderByDescending(item => item.Date)
                .First();
            var earliestAllowed = latest.Date.AddDays(1);

            if (pending.RequestedDate.Value < earliestAllowed)
            {
                errors.Add(
                    $"The pending {DisplayName(stageNames, pending.StageCode)} update dated {pending.RequestedDate.Value:dd MMM yyyy} " +
                    $"conflicts with this revised sequence. Its date must be on or after {earliestAllowed:dd MMM yyyy}, " +
                    $"the day after {DisplayName(stageNames, latest.Code)} completion. Include {DisplayName(stageNames, pending.StageCode)} in this submission to revise it.");
            }
        }

        return DistinctMessages(errors);
    }

    private static IReadOnlyDictionary<string, string> BuildStageNameLookup(
        ProjectStageWorkflowSnapshot? workflow,
        IReadOnlyCollection<ProjectStage> stages)
    {
        if (workflow is not null)
        {
            return workflow.Stages.ToDictionary(
                stage => stage.Code,
                stage => stage.Name,
                StringComparer.OrdinalIgnoreCase);
        }

        return stages.ToDictionary(
            stage => stage.StageCode,
            stage => stage.StageCode,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string DisplayName(IReadOnlyDictionary<string, string> names, string code)
        => names.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : code;

    private static string RequestedActionPhrase(StageStatus status) => status switch
    {
        StageStatus.InProgress => "started",
        StageStatus.Completed => "completed",
        StageStatus.Blocked => "blocked",
        StageStatus.Skipped => "skipped",
        StageStatus.NotStarted => "reopened",
        _ => "updated"
    };

    private static bool IsPredecessorError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("predecessor", StringComparison.OrdinalIgnoreCase)
            || message.Contains("required stage", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> DistinctMessages(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record PreparedStageRequest(
        int InputOrder,
        string StageCode,
        StageStatus RequestedStatus,
        DateOnly? RequestedDate,
        string? Note);

    private sealed class ProjectedStageState
    {
        public ProjectedStageState(
            string stageCode,
            StageStatus officialStatus,
            StageStatus projectedStatus,
            DateOnly? officialActualStart,
            DateOnly? officialCompletionDate,
            DateOnly? projectedStartDate,
            DateOnly? projectedCompletionDate)
        {
            StageCode = stageCode;
            OfficialStatus = officialStatus;
            ProjectedStatus = projectedStatus;
            OfficialActualStart = officialActualStart;
            OfficialCompletionDate = officialCompletionDate;
            ProjectedStartDate = projectedStartDate;
            ProjectedCompletionDate = projectedCompletionDate;
        }

        public string StageCode { get; }
        public StageStatus OfficialStatus { get; }
        public StageStatus ProjectedStatus { get; set; }
        public DateOnly? OfficialActualStart { get; }
        public DateOnly? OfficialCompletionDate { get; }
        public DateOnly? ProjectedStartDate { get; set; }
        public DateOnly? ProjectedCompletionDate { get; set; }
        public DateOnly? ProjectedDate { get; set; }
    }
}

public sealed record StageRequestResult
{
    public StageRequestOutcome Outcome { get; init; }
    public string? Error { get; init; }
    public int? RequestId { get; init; }
    public IReadOnlyList<string> MissingPredecessors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    private StageRequestResult(
        StageRequestOutcome outcome,
        string? error,
        int? requestId,
        IReadOnlyList<string> missingPredecessors,
        IReadOnlyList<string> errors)
    {
        Outcome = outcome;
        Error = error;
        RequestId = requestId;
        MissingPredecessors = missingPredecessors;
        Errors = errors;
    }

    public static StageRequestResult Success(int requestId)
        => new(StageRequestOutcome.Success, null, requestId, Array.Empty<string>(), Array.Empty<string>());

    internal static StageRequestResult Ready()
        => new(StageRequestOutcome.Ready, null, null, Array.Empty<string>(), Array.Empty<string>());

    public static StageRequestResult NotProjectOfficer()
        => new(StageRequestOutcome.NotProjectOfficer, null, null, Array.Empty<string>(), Array.Empty<string>());

    public static StageRequestResult StageNotFound()
        => new(StageRequestOutcome.StageNotFound, null, null, Array.Empty<string>(), Array.Empty<string>());

    public static StageRequestResult DuplicatePending()
        => new(
            StageRequestOutcome.DuplicatePending,
            "A pending update already exists for this stage.",
            null,
            Array.Empty<string>(),
            Array.Empty<string>());

    public static StageRequestResult ValidationFailed(
        IReadOnlyList<string>? errors,
        IReadOnlyList<string>? missingPredecessors = null)
    {
        var normalizedErrors = Normalize(errors);
        var normalizedMissing = Normalize(missingPredecessors);
        var message = normalizedErrors.Count > 0
            ? normalizedErrors[0]
            : "Validation failed.";

        return new(StageRequestOutcome.ValidationFailed, message, null, normalizedMissing, normalizedErrors);
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

// SECTION: Batch request result contracts
public sealed record StageRequestItemResult(string StageCode, StageRequestResult Result);

public sealed record BatchStageRequestResult
{
    public BatchStageRequestOutcome Outcome { get; init; }

    public IReadOnlyList<StageRequestItemResult> Items { get; init; }
        = Array.Empty<StageRequestItemResult>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    private BatchStageRequestResult(
        BatchStageRequestOutcome outcome,
        IReadOnlyList<StageRequestItemResult> items,
        IReadOnlyList<string> errors)
    {
        Outcome = outcome;
        Items = items;
        Errors = errors;
    }

    public static BatchStageRequestResult Success(IReadOnlyList<StageRequestItemResult> responses)
        => new(BatchStageRequestOutcome.Success, responses, Array.Empty<string>());

    public static BatchStageRequestResult ValidationFailed(IReadOnlyList<StageRequestItemResult> responses)
    {
        var errors = responses
            .SelectMany(item => item.Result.Errors)
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(BatchStageRequestOutcome.ValidationFailed, responses, errors);
    }

    public static BatchStageRequestResult Invalid(IReadOnlyList<string> errors)
        => new(
            BatchStageRequestOutcome.ValidationFailed,
            Array.Empty<StageRequestItemResult>(),
            errors
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Select(error => error.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());

    public static BatchStageRequestResult NotProjectOfficerResult()
        => new(BatchStageRequestOutcome.NotProjectOfficer, Array.Empty<StageRequestItemResult>(), Array.Empty<string>());

    public static BatchStageRequestResult StageNotFoundResult(IReadOnlyList<StageRequestItemResult>? items = null)
        => new(BatchStageRequestOutcome.StageNotFound, items ?? Array.Empty<StageRequestItemResult>(), Array.Empty<string>());
}

public enum BatchStageRequestOutcome
{
    Success,
    NotProjectOfficer,
    StageNotFound,
    ValidationFailed
}

public enum StageRequestOutcome
{
    Ready,
    Success,
    NotProjectOfficer,
    StageNotFound,
    DuplicatePending,
    ValidationFailed
}
