using System;
using System.Collections.Generic;
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
            .ToListAsync(cancellationToken);

        var viewModel = new PlanEditVm { ProjectId = projectId };
        var stageMap = stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.StageCode))
            .ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);

        var knownCodes = new HashSet<string>(StageCodes.All, StringComparer.OrdinalIgnoreCase);

        foreach (var code in StageCodes.All)
        {
            stageMap.TryGetValue(code, out var stage);

            viewModel.Rows.Add(new PlanEditVm.PlanEditRow
            {
                Code = code,
                Name = StageCodes.DisplayNameOf(code),
                PlannedStart = stage?.PlannedStart,
                PlannedDue = stage?.PlannedDue,
            });
        }

        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.StageCode) || knownCodes.Contains(stage.StageCode))
            {
                continue;
            }

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
