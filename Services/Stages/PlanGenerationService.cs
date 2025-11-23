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

namespace ProjectManagement.Services.Stages;

public sealed class PlanGenerationService
{
    private readonly ApplicationDbContext _db;

    public PlanGenerationService(ApplicationDbContext db) => _db = db;

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
        workflowVersion ??= PlanConstants.StageTemplateVersionV1;

        var templateSequence = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == workflowVersion)
            .ToDictionaryAsync(t => t.Code, t => t.Sequence, StringComparer.OrdinalIgnoreCase, ct);

        var stages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(ct);

        var holidays = await _db.Holidays
            .AsNoTracking()
            .Select(h => h.Date)
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

        for (var i = 0; i < orderedStages.Count; i++)
        {
            var stage = orderedStages[i];
            if (string.IsNullOrWhiteSpace(stage.StageCode))
            {
                continue;
            }

            if (!durationMap.TryGetValue(stage.StageCode, out var duration) || duration.DurationDays is null)
            {
                continue;
            }

            var days = duration.DurationDays.Value;
            if (days <= 0)
            {
                continue;
            }

            stage.SortOrder = ResolveSortOrder(stage.StageCode, durationMap, templateSequence);

            var start = i == 0
                ? cursor
                : settings.NextStageStartPolicy == NextStageStartPolicies.SameDay
                    ? cursor
                    : calendar.NextWorkingDay(cursor);

            var end = calendar.AddWorkingDays(start, days - 1);

            stage.PlannedStart = start;
            stage.PlannedDue = end;

            cursor = end;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task GenerateDraftAsync(int projectId, int planVersionId, CancellationToken ct = default)
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
        workflowVersion ??= PlanConstants.StageTemplateVersionV1;

        var templates = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == workflowVersion)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Code)
            .ToListAsync(ct);

        var holidays = await _db.Holidays
            .AsNoTracking()
            .Select(h => h.Date)
            .ToListAsync(ct);

        var calendar = new WorkingCalendar(holidays, settings.IncludeWeekends, settings.SkipHolidays);
        var durationMap = durations
            .Where(d => !string.IsNullOrWhiteSpace(d.StageCode))
            .ToDictionary(d => d.StageCode!, StringComparer.OrdinalIgnoreCase);

        var plan = await _db.PlanVersions
            .Include(v => v.StagePlans)
            .SingleAsync(v => v.Id == planVersionId && v.ProjectId == projectId, ct);

        plan.SkipWeekends = !settings.IncludeWeekends;
        plan.TransitionRule = string.Equals(settings.NextStageStartPolicy, NextStageStartPolicies.SameDay, StringComparison.Ordinal)
            ? PlanTransitionRule.SameDay
            : PlanTransitionRule.NextWorkingDay;
        plan.AnchorDate = settings.AnchorStart;

        var stagePlans = plan.StagePlans
            .Where(sp => !string.IsNullOrWhiteSpace(sp.StageCode))
            .ToDictionary(sp => sp.StageCode!, sp => sp, StringComparer.OrdinalIgnoreCase);

        var orderedCodes = BuildOrderedCodes(templates, durationMap, stagePlans.Keys);

        var cursor = settings.AnchorStart.Value;
        var isFirstStage = true;

        foreach (var code in orderedCodes)
        {
            if (!durationMap.TryGetValue(code, out var duration) || duration.DurationDays is null)
            {
                continue;
            }

            var days = duration.DurationDays.Value;
            if (days <= 0)
            {
                continue;
            }

            var start = cursor;
            if (!isFirstStage && settings.NextStageStartPolicy == NextStageStartPolicies.NextWorkingDay)
            {
                start = calendar.NextWorkingDay(cursor);
            }

            var end = calendar.AddWorkingDays(start, days - 1);

            if (!stagePlans.TryGetValue(code, out var row))
            {
                row = new StagePlan
                {
                    PlanVersionId = planVersionId,
                    StageCode = code
                };
                plan.StagePlans.Add(row);
                stagePlans[code] = row;
            }

            row.PlannedStart = start;
            row.PlannedDue = end;
            row.DurationDays = days;

            cursor = end;
            isFirstStage = false;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<string> BuildOrderedCodes(IEnumerable<string> templates, IReadOnlyDictionary<string, ProjectPlanDuration> durationMap, IEnumerable<string> stagePlanKeys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void AddRange(IEnumerable<string> source)
        {
            foreach (var code in source)
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                if (seen.Add(code))
                {
                    ordered.Add(code);
                }
            }
        }

        AddRange(templates);
        var durationOrdered = durationMap.Values
            .Where(d => !string.IsNullOrWhiteSpace(d.StageCode))
            .OrderBy(d => ResolveSortOrder(d.StageCode, durationMap))
            .Select(d => d.StageCode!)
            .ToList();
        AddRange(durationOrdered);
        AddRange(StageCodes.All);
        AddRange(stagePlanKeys);

        return ordered;
    }

    private static int ResolveSortOrder(string? stageCode, IReadOnlyDictionary<string, ProjectPlanDuration>? durationMap, IReadOnlyDictionary<string, int>? templateSequence = null)
    {
        if (stageCode is not null && durationMap is not null && durationMap.TryGetValue(stageCode, out var duration))
        {
            return duration.SortOrder;
        }

        if (stageCode is null)
        {
            return int.MaxValue;
        }

        if (templateSequence is not null && templateSequence.TryGetValue(stageCode, out var templateOrder))
        {
            return templateOrder;
        }

        var index = Array.IndexOf(StageCodes.All, stageCode);
        return index >= 0 ? index : int.MaxValue;
    }
}
