using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Stages;

public sealed class PlanReadService
{
    private readonly ApplicationDbContext _db;

    public PlanReadService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PlanEditorVm> GetAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var stages = await _db.ProjectStages
            .Where(stage => stage.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        var scheduleSettings = await _db.ProjectScheduleSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.ProjectId == projectId, cancellationToken);

        var durationRows = await _db.ProjectPlanDurations
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(cancellationToken);

        var exactVm = new PlanEditVm { ProjectId = projectId };
        var durationVm = new PlanDurationVm
        {
            ProjectId = projectId,
            AnchorStart = scheduleSettings?.AnchorStart,
            IncludeWeekends = scheduleSettings?.IncludeWeekends ?? false,
            SkipHolidays = scheduleSettings?.SkipHolidays ?? true,
            NextStageStartPolicy = scheduleSettings?.NextStageStartPolicy ?? NextStageStartPolicies.NextWorkingDay
        };

        var stageMap = stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);

        var durationMap = durationRows
            .Where(d => !string.IsNullOrWhiteSpace(d.StageCode))
            .ToDictionary(d => d.StageCode!, StringComparer.OrdinalIgnoreCase);

        var knownCodes = new HashSet<string>(StageCodes.All, StringComparer.OrdinalIgnoreCase);
        var extraStages = new List<(ProjectStage Stage, int Sort)>();

        foreach (var code in StageCodes.All)
        {
            stageMap.TryGetValue(code, out var stage);
            durationMap.TryGetValue(code, out var duration);

            exactVm.Rows.Add(new PlanEditVm.PlanEditRow
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
                PlannedStart = stage?.PlannedStart,
                PlannedDue = stage?.PlannedDue,
            });

            durationVm.Rows.Add(new PlanDurationRowVm
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
                DurationDays = duration?.DurationDays
            });
        }

        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.StageCode) || knownCodes.Contains(stage.StageCode))
            {
                continue;
            }

            var sort = durationMap.TryGetValue(stage.StageCode!, out var duration)
                ? duration.SortOrder
                : StageCodes.All.Length + extraStages.Count;
            extraStages.Add((stage, sort));
        }

        foreach (var (stage, _) in extraStages.OrderBy(s => s.Sort).ThenBy(s => s.Stage.StageCode, StringComparer.OrdinalIgnoreCase))
        {
            var duration = durationMap.TryGetValue(stage.StageCode, out var row) ? row : null;

            exactVm.Rows.Add(new PlanEditVm.PlanEditRow
            {
                Code = stage.StageCode,
                Name = StageCodes.DisplayNameOf(stage.StageCode),
                PlannedStart = stage.PlannedStart,
                PlannedDue = stage.PlannedDue,
            });

            durationVm.Rows.Add(new PlanDurationRowVm
            {
                Code = stage.StageCode,
                Name = StageCodes.DisplayNameOf(stage.StageCode),
                DurationDays = duration?.DurationDays
            });
        }

        var activeMode = scheduleSettings?.AnchorStart is not null
            ? PlanEditorModes.Durations
            : PlanEditorModes.Exact;

        return new PlanEditorVm
        {
            Exact = exactVm,
            Durations = durationVm,
            ActiveMode = activeMode
        };
    }
}
