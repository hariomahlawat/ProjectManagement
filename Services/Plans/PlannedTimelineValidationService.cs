using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Services.Plans;

public sealed record PlannedTimelineStage(
    string StageCode,
    DateOnly? PlannedStart,
    DateOnly? PlannedDue,
    bool IsSkipped = false,
    int SortOrder = 0);

public sealed record PlannedTimelineCandidate(
    int ProjectId,
    string? AnchorStageCode,
    PlanTransitionRule TransitionRule,
    bool IncludeWeekends,
    bool SkipHolidays,
    bool PncApplicable,
    IReadOnlyList<PlannedTimelineStage> Stages,
    bool RequireConfiguredStages = true);

public sealed record PlannedTimelineValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public interface IPlannedTimelineValidationService
{
    Task<PlannedTimelineValidationResult> ValidateAsync(
        PlannedTimelineCandidate candidate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Canonical planned-date and workflow chronology validation. This service validates
/// a complete candidate projection and is intentionally separate from execution
/// lifecycle validation.
/// </summary>
public sealed class PlannedTimelineValidationService : IPlannedTimelineValidationService
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IOfficeCalendarService _officeCalendar;

    public PlannedTimelineValidationService(
        ApplicationDbContext db,
        IProjectStageWorkflowPolicy workflowPolicy,
        IOfficeCalendarService officeCalendar)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _officeCalendar = officeCalendar ?? throw new ArgumentNullException(nameof(officeCalendar));
    }

    public async Task<PlannedTimelineValidationResult> ValidateAsync(
        PlannedTimelineCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidate.Stages);

        var workflow = await _workflowPolicy.GetAsync(
            candidate.ProjectId,
            candidate.PncApplicable,
            cancellationToken);

        var configuredStages = await _db.StageTemplates
            .AsNoTracking()
            .Where(template => template.Version == workflow.WorkflowVersion)
            .OrderBy(template => template.Sequence)
            .ThenBy(template => template.Id)
            .ToListAsync(cancellationToken);
        var configuredNames = configuredStages.ToDictionary(
            template => template.Code,
            template => template.Name,
            StringComparer.OrdinalIgnoreCase);
        foreach (var stage in workflow.Stages)
        {
            configuredNames.TryAdd(stage.Code, stage.Name);
        }

        var finalRows = new Dictionary<string, (PlannedTimelineStage Stage, int Index)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < candidate.Stages.Count; index++)
        {
            var stage = candidate.Stages[index];
            if (!string.IsNullOrWhiteSpace(stage.StageCode))
            {
                finalRows[stage.StageCode] = (stage, index);
            }
        }

        var rows = finalRows.Values
            .OrderBy(item => workflow.OrderOf(item.Stage.StageCode))
            .ThenBy(item => item.Stage.SortOrder)
            .ThenBy(item => item.Index)
            .Select(item => item.Stage)
            .ToArray();
        var rowLookup = rows.ToDictionary(row => row.StageCode, StringComparer.OrdinalIgnoreCase);

        var configuredSequence = configuredStages.ToDictionary(
            template => template.Code,
            template => template.Sequence,
            StringComparer.OrdinalIgnoreCase);
        var anchorCode = string.IsNullOrWhiteSpace(candidate.AnchorStageCode)
            ? PlanConstants.DefaultAnchorStageCode
            : candidate.AnchorStageCode;
        int? configuredAnchorSequence = configuredSequence.TryGetValue(anchorCode, out var anchorSequence)
            ? anchorSequence
            : null;
        var anchorOrder = !configuredAnchorSequence.HasValue && workflow.ContainsStage(anchorCode)
            ? workflow.OrderOf(anchorCode)
            : int.MinValue;
        var officialSkippedStageCodes = (await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => stage.ProjectId == candidate.ProjectId && stage.Status == StageStatus.Skipped)
            .Select(stage => stage.StageCode)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relevantDueDates = rows
            .Where(row => IsIncluded(
                row,
                candidate.PncApplicable,
                workflow,
                configuredSequence,
                configuredAnchorSequence,
                anchorOrder,
                officialSkippedStageCodes))
            .Select(row => row.PlannedDue)
            .OfType<DateOnly>()
            .ToArray();
        IReadOnlySet<DateOnly> holidays = new HashSet<DateOnly>();
        if (candidate.SkipHolidays && relevantDueDates.Length > 0)
        {
            var first = relevantDueDates.Min();
            var last = relevantDueDates.Max();
            holidays = await _officeCalendar.GetNonWorkingDatesAsync(
                first,
                last.AddYears(1),
                cancellationToken);
        }

        var calendar = new WorkingCalendar(holidays, candidate.IncludeWeekends, candidate.SkipHolidays);
        var errors = new List<string>();

        if (candidate.RequireConfiguredStages)
        {
            foreach (var configuredStage in configuredStages)
            {
                var beforeAnchor = configuredAnchorSequence.HasValue
                    ? configuredStage.Sequence < configuredAnchorSequence.Value
                    : workflow.OrderOf(configuredStage.Code) < anchorOrder;
                if (beforeAnchor ||
                    officialSkippedStageCodes.Contains(configuredStage.Code) ||
                    (!candidate.PncApplicable && string.Equals(
                        configuredStage.Code,
                        StageCodes.PNC,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!rowLookup.ContainsKey(configuredStage.Code))
                {
                    errors.Add($"Stage {configuredStage.Name} ({configuredStage.Code}) is missing from the plan.");
                }
            }
        }

        foreach (var stage in rows)
        {
            if (!IsIncluded(
                    stage,
                    candidate.PncApplicable,
                    workflow,
                    configuredSequence,
                    configuredAnchorSequence,
                    anchorOrder,
                    officialSkippedStageCodes))
            {
                continue;
            }

            var stageName = DisplayName(stage.StageCode, configuredNames);
            if (stage.PlannedStart is DateOnly start &&
                stage.PlannedDue is DateOnly due &&
                due < start)
            {
                errors.Add($"{stageName} Planned due must be on or after its Planned start.");
            }

            if (stage.PlannedStart is not DateOnly successorStart)
            {
                continue;
            }

            foreach (var predecessorCode in workflow.RequiredPredecessors(stage.StageCode))
            {
                if (!rowLookup.TryGetValue(predecessorCode, out var predecessor) ||
                    !IsIncluded(
                        predecessor,
                        candidate.PncApplicable,
                        workflow,
                        configuredSequence,
                        configuredAnchorSequence,
                        anchorOrder,
                        officialSkippedStageCodes) ||
                    predecessor.PlannedDue is not DateOnly predecessorDue)
                {
                    continue;
                }

                var minimumStart = candidate.TransitionRule == PlanTransitionRule.SameDay
                    ? predecessorDue
                    : calendar.NextWorkingDay(predecessorDue);
                if (successorStart >= minimumStart)
                {
                    continue;
                }

                var predecessorName = DisplayName(predecessorCode, configuredNames);
                var boundary = candidate.TransitionRule == PlanTransitionRule.SameDay
                    ? "planned due date"
                    : "next working day";
                errors.Add(
                    $"{stageName} Planned start must be on or after {predecessorName}'s {boundary} ({minimumStart:dd MMM yyyy}).");
            }
        }

        return new PlannedTimelineValidationResult(errors);
    }

    private static bool IsIncluded(
        PlannedTimelineStage stage,
        bool pncApplicable,
        ProjectStageWorkflowSnapshot workflow,
        IReadOnlyDictionary<string, int> configuredSequence,
        int? configuredAnchorSequence,
        int anchorOrder,
        IReadOnlySet<string> officialSkippedStageCodes)
    {
        if (stage.IsSkipped ||
            officialSkippedStageCodes.Contains(stage.StageCode) ||
            (!pncApplicable && string.Equals(stage.StageCode, StageCodes.PNC, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (configuredAnchorSequence.HasValue)
        {
            // Preserve the pre-B2 anchor contract: configured StageTemplate
            // sequence owns anchor scope. Rows outside that configured catalog
            // remain independently validatable rather than being guessed into
            // either side of a custom anchor.
            return !configuredSequence.TryGetValue(stage.StageCode, out var stageSequence) ||
                stageSequence >= configuredAnchorSequence.Value;
        }

        return workflow.OrderOf(stage.StageCode) >= anchorOrder;
    }

    private static string DisplayName(
        string stageCode,
        IReadOnlyDictionary<string, string> configuredNames) =>
        configuredNames.TryGetValue(stageCode, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : stageCode;
}