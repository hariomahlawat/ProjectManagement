using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;

using ProjectManagement.Services.Scheduling;

namespace ProjectManagement.Services.Stages;

public sealed class PlanGenerationService
{
    private readonly ApplicationDbContext _db;
    private readonly IOfficeCalendarService? _officeCalendar;

    public PlanGenerationService(ApplicationDbContext db, IOfficeCalendarService? officeCalendar = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _officeCalendar = officeCalendar;
    }

    public async Task GenerateAsync(int projectId, CancellationToken ct = default)
    {
        var settings = await _db.ProjectScheduleSettings
            .SingleOrDefaultAsync(s => s.ProjectId == projectId, ct)
            ?? throw new InvalidOperationException("Configure schedule settings before generating the plan.");

        if (settings.AnchorStart is null)
        {
            throw new InvalidOperationException("Set an anchor start date before generating the plan.");
        }

        var durations = await _db.ProjectPlanDurations
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(ct);

        // SECTION: Workflow Resolution
        var workflowVersion = await _db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => p.WorkflowVersion)
            .SingleAsync(ct);
        workflowVersion ??= PlanConstants.DefaultStageTemplateVersion;

        var templateSequence = ProcurementWorkflow.BuildOrderLookup(workflowVersion);
        var configuredSequence = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == workflowVersion)
            .ToDictionaryAsync(t => t.Code, t => t.Sequence, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var (code, sequence) in configuredSequence)
        {
            templateSequence[code] = sequence;
        }

        var stages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(ct);

        var holidays = _officeCalendar is not null
            ? (await _officeCalendar.GetNonWorkingDatesAsync(
                settings.AnchorStart.Value.AddYears(-1),
                settings.AnchorStart.Value.AddYears(10),
                ct)).ToList()
            : await _db.Holidays
                .AsNoTracking()
                .Where(h => h.Type == ProjectManagement.Models.Scheduling.HolidayType.Gazetted || h.IsObservedAsOfficeHoliday)
                .Select(h => h.Date)
                .Distinct()
                .ToListAsync(ct);

        var calendar = new WorkingCalendar(holidays, settings.IncludeWeekends, settings.SkipHolidays);
        var durationMap = durations
            .Where(d => !string.IsNullOrWhiteSpace(d.StageCode))
            .ToDictionary(d => d.StageCode!, StringComparer.OrdinalIgnoreCase);

        var orderedStages = stages
            .OrderBy(s => ResolveSortOrder(s.StageCode, durationMap, templateSequence))
            .ThenBy(s => s.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cursor = settings.AnchorStart.Value;
        var isFirstStage = true;

        foreach (var stage in orderedStages)
        {
            if (string.IsNullOrWhiteSpace(stage.StageCode))
            {
                continue;
            }

            if (!durationMap.TryGetValue(stage.StageCode, out var duration))
            {
                // No Duration input owns this stage; preserve its manual plan.
                continue;
            }

            if (duration.DurationDays is not int days || days <= 0)
            {
                // An explicit cleared input removes the previous working plan,
                // without changing lifecycle status or observed actual dates.
                stage.PlannedStart = null;
                stage.PlannedDue = null;
                continue;
            }

            stage.SortOrder = ResolveSortOrder(stage.StageCode, durationMap, templateSequence);

            var start = isFirstStage
                ? cursor
                : settings.NextStageStartPolicy == NextStageStartPolicies.SameDay
                    ? cursor
                    : calendar.NextWorkingDay(cursor);

            var end = calendar.AddWorkingDays(start, days - 1);

            stage.PlannedStart = start;
            stage.PlannedDue = end;

            cursor = end;
            isFirstStage = false;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task GenerateDraftAsync(int projectId, int planVersionId, CancellationToken ct = default)
    {
        var plan = await _db.PlanVersions
            .Include(v => v.StagePlans)
            .SingleAsync(v => v.Id == planVersionId && v.ProjectId == projectId, ct);
        if (plan.AnchorDate is null)
        {
            throw new InvalidOperationException("Set an anchor start date before generating the plan.");
        }

        var workflowVersion = await _db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => p.WorkflowVersion)
            .SingleAsync(ct);
        workflowVersion ??= PlanConstants.DefaultStageTemplateVersion;
        var templates = await _db.StageTemplates.AsNoTracking()
            .Where(t => t.Version == workflowVersion)
            .OrderBy(t => t.Sequence).ThenBy(t => t.Id)
            .Select(t => t.Code)
            .ToListAsync(ct);
        var workflowCodes = ProcurementWorkflow.StageCodesFor(workflowVersion);

        var holidays = _officeCalendar is not null
            ? (await _officeCalendar.GetNonWorkingDatesAsync(
                plan.AnchorDate.Value.AddYears(-1),
                plan.AnchorDate.Value.AddYears(10), ct)).ToList()
            : await _db.Holidays.AsNoTracking()
                .Where(h => h.Type == ProjectManagement.Models.Scheduling.HolidayType.Gazetted || h.IsObservedAsOfficeHoliday)
                .Select(h => h.Date).Distinct().ToListAsync(ct);
        var calendar = new WorkingCalendar(holidays, !plan.SkipWeekends, plan.SkipHolidays);
        var stagePlans = plan.StagePlans
            .Where(sp => !string.IsNullOrWhiteSpace(sp.StageCode))
            .ToDictionary(sp => sp.StageCode, StringComparer.OrdinalIgnoreCase);
        var orderedCodes = BuildOrderedCodes(templates, workflowCodes, stagePlans.Values);
        var cursor = plan.AnchorDate.Value;
        var isFirstStage = true;
        foreach (var code in orderedCodes)
        {
            if (!stagePlans.TryGetValue(code, out var row))
            {
                continue;
            }
            if (row.DurationDays <= 0)
            {
                row.PlannedStart = null;
                row.PlannedDue = null;
                row.DurationDays = 0;
                continue;
            }
            var start = !isFirstStage && plan.TransitionRule == PlanTransitionRule.NextWorkingDay
                ? calendar.NextWorkingDay(cursor) : cursor;
            var end = calendar.AddWorkingDays(start, row.DurationDays - 1);
            row.PlannedStart = start;
            row.PlannedDue = end;
            cursor = end;
            isFirstStage = false;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<string> BuildOrderedCodes(
        IEnumerable<string> templates,
        IEnumerable<string> workflowCodes,
        IEnumerable<StagePlan> stagePlans)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        void AddRange(IEnumerable<string> codes)
        {
            foreach (var code in codes)
            {
                if (!string.IsNullOrWhiteSpace(code) && seen.Add(code))
                {
                    ordered.Add(code);
                }
            }
        }
        // Known workflow metadata owns known-stage order. Only extensions need
        // proposal-owned order; they cannot consult mutable compatibility rows.
        AddRange(templates);
        AddRange(workflowCodes);
        AddRange(stagePlans.OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ThenBy(s => s.StageCode, StringComparer.OrdinalIgnoreCase).Select(s => s.StageCode));
        return ordered;
    }

    private static int ResolveSortOrder(string? stageCode, IReadOnlyDictionary<string, ProjectPlanDuration>? durationMap, IReadOnlyDictionary<string, int>? templateSequence = null)
    {
        if (stageCode is null)
        {
            return int.MaxValue;
        }

        // Workflow metadata is authoritative for known stages. Duration rows are
        // used only as a fallback for legacy or extension stages outside the
        // configured workflow.
        if (templateSequence is not null && templateSequence.TryGetValue(stageCode, out var templateOrder))
        {
            return templateOrder;
        }

        if (durationMap is not null && durationMap.TryGetValue(stageCode, out var duration))
        {
            return duration.SortOrder;
        }

        return int.MaxValue;
    }
}
