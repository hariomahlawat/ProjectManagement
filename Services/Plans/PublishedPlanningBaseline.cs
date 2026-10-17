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

namespace ProjectManagement.Services.Plans;

/// <summary>
/// One coherent read of current published state. Legacy compatibility rows are
/// retained in storage but cannot establish authority for another actor's proposal.
/// </summary>
public sealed class PublishedPlanningBaseline
{
    public string? WorkflowVersion { get; private init; }
    public bool PncApplicable { get; private init; }
    public bool IsTrusted { get; private init; }
    public ProjectScheduleSettings? Settings { get; private init; }
    public IReadOnlyList<ProjectPlanDuration> Durations { get; private init; } = Array.Empty<ProjectPlanDuration>();
    public IReadOnlyList<ProjectStage> Stages { get; private init; } = Array.Empty<ProjectStage>();

    public static async Task<PublishedPlanningBaseline> LoadAsync(
        ApplicationDbContext db, int projectId, CancellationToken cancellationToken = default)
    {
        // AsSingleQuery is deliberate: the marker, live PNC, official dates and
        // compatibility inputs must all come from the same statement snapshot.
        var snapshot = await db.Projects.AsNoTracking()
            .Where(project => project.Id == projectId)
            .AsSingleQuery()
            .Select(project => new
            {
                project.WorkflowVersion,
                project.PncApplicable,
                project.PublishedPlanningInputVersion,
                Settings = db.ProjectScheduleSettings.Where(settings => settings.ProjectId == project.Id).FirstOrDefault(),
                Durations = db.ProjectPlanDurations.Where(duration => duration.ProjectId == project.Id)
                    .OrderBy(duration => duration.SortOrder).ThenBy(duration => duration.Id).ToList(),
                Stages = db.ProjectStages.Where(stage => stage.ProjectId == project.Id)
                    .OrderBy(stage => stage.SortOrder).ThenBy(stage => stage.Id).ToList()
            })
            .SingleAsync(cancellationToken);
        var trusted = snapshot.PublishedPlanningInputVersion == PlanningInputVersions.Current;
        return new PublishedPlanningBaseline
        {
            WorkflowVersion = snapshot.WorkflowVersion,
            PncApplicable = snapshot.PncApplicable,
            IsTrusted = trusted,
            Settings = trusted ? snapshot.Settings : null,
            Durations = trusted ? snapshot.Durations : Array.Empty<ProjectPlanDuration>(),
            Stages = snapshot.Stages
        };
    }
}
