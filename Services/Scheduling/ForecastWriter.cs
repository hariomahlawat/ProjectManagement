using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Scheduling;

public class ForecastWriter : IForecastWriter
{
    private readonly ApplicationDbContext _db;
    private readonly IScheduleEngine _engine;
    private readonly IClock _clock;

    public ForecastWriter(ApplicationDbContext db, IScheduleEngine engine, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task RecomputeAsync(int projectId, string? causeStageCode, string causeType, string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(causeType))
        {
            throw new ArgumentException("Cause type is required.", nameof(causeType));
        }

        var project = await _db.Projects
            .Include(p => p.Stages)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project == null)
        {
            throw new InvalidOperationException($"Project {projectId} was not found.");
        }

        if (!project.ActivePlanVersionNo.HasValue)
        {
            return;
        }

        var planVersion = await _db.PlanVersions
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.VersionNo == project.ActivePlanVersionNo.Value)
            .Select(p => new
            {
                p.Id,
                p.SkipWeekends,
                p.TransitionRule,
                p.PncApplicable
            })
            .FirstOrDefaultAsync(ct);

        if (planVersion == null)
        {
            return;
        }

        var templates = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == PlanConstants.StageTemplateVersion)
            .OrderBy(t => t.Sequence)
            .ToListAsync(ct);

        var dependencies = await _db.StageDependencyTemplates
            .AsNoTracking()
            .Where(d => d.Version == PlanConstants.StageTemplateVersion)
            .ToListAsync(ct);

        var durations = await _db.StagePlans
            .AsNoTracking()
            .Where(sp => sp.PlanVersionId == planVersion.Id)
            .ToDictionaryAsync(sp => sp.StageCode, sp => sp.DurationDays, StringComparer.OrdinalIgnoreCase, ct);

        var execution = project.Stages.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);

        var options = new ScheduleOptions(
            planVersion.SkipWeekends,
            planVersion.TransitionRule,
            planVersion.PncApplicable,
            DateOnly.FromDateTime(_clock.UtcNow.DateTime));

        var forecast = _engine.ComputeForecast(templates, dependencies, durations, execution, options);

        foreach (var (code, window) in forecast)
        {
            if (!execution.TryGetValue(code, out var stage))
            {
                continue;
            }

            var previousDue = stage.ForecastDue;

            stage.ForecastStart = window.start;
            stage.ForecastDue = window.due;

            if (previousDue != stage.ForecastDue)
            {
                var delta = previousDue.HasValue
                    ? stage.ForecastDue.Value.DayNumber - previousDue.Value.DayNumber
                    : 0;

                _db.StageShiftLogs.Add(new StageShiftLog
                {
                    ProjectId = projectId,
                    StageCode = code,
                    OldForecastDue = previousDue,
                    NewForecastDue = stage.ForecastDue!.Value,
                    DeltaDays = delta,
                    CauseStageCode = causeStageCode ?? code,
                    CauseType = causeType,
                    CreatedOn = _clock.UtcNow,
                    CreatedByUserId = userId
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
