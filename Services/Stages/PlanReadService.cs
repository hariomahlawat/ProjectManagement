using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
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

    public async Task<PlanEditVm> GetAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var stages = await _db.ProjectStages
            .Where(stage => stage.ProjectId == projectId)
            .OrderBy(stage => stage.SortOrder)
            .ToListAsync(cancellationToken);

        var viewModel = new PlanEditVm { ProjectId = projectId };
        foreach (var stage in stages)
        {
            viewModel.Rows.Add(new PlanEditVm.PlanEditRow
            {
                Code = stage.StageCode,
                Name = StageCodes.DisplayNameOf(stage.StageCode),
                PlannedStart = stage.PlannedStart,
                PlannedDue = stage.PlannedDue,
            });
        }

        return viewModel;
    }
}
