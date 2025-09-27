using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
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
            .OrderBy(s => ResolveSortOrder(s.StageCode, durationMap))
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

            stage.SortOrder = ResolveSortOrder(stage.StageCode, durationMap);

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

    private static int ResolveSortOrder(string? stageCode, IReadOnlyDictionary<string, ProjectPlanDuration> durationMap)
    {
        if (stageCode is not null && durationMap.TryGetValue(stageCode, out var duration))
        {
            return duration.SortOrder;
        }

        if (stageCode is null)
        {
            return int.MaxValue;
        }

        var index = Array.IndexOf(StageCodes.All, stageCode);
        return index >= 0 ? index : int.MaxValue;
    }
}
