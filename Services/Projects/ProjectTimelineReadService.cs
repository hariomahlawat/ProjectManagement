using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectTimelineReadService
{
    private readonly ApplicationDbContext _db;
    public ProjectTimelineReadService(ApplicationDbContext db) => _db = db;

    public async Task<TimelineVm> GetAsync(int projectId, CancellationToken ct = default)
    {
        var rows = await _db.ProjectStages
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(ct);

        var items = new List<TimelineItemVm>();
        var index = 0;
        foreach (var code in StageCodes.All)
        {
            var r = rows.FirstOrDefault(x => x.StageCode == code);
            items.Add(new TimelineItemVm
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
                Status = r?.Status ?? StageStatus.NotStarted,
                PlannedStart = null,
                PlannedEnd = null,
                ActualStart = r?.ActualStart,
                CompletedOn = r?.CompletedOn,
                IsAutoCompleted = r?.IsAutoCompleted ?? false,
                AutoCompletedFromCode = r?.AutoCompletedFromCode,
                RequiresBackfill = r?.RequiresBackfill ?? false,
                SortOrder = index++
            });
        }

        var completed = items.Count(i => i.Status == StageStatus.Completed);

        return new TimelineVm
        {
            ProjectId = projectId,
            TotalStages = items.Count,
            CompletedCount = completed,
            Items = items
        };
    }
}
