using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectFactsReadService
{
    private readonly ApplicationDbContext _db;

    public ProjectFactsReadService(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> HasRequiredFactsAsync(int projectId, string stageCode, CancellationToken ct = default)
        => stageCode switch
        {
            StageCodes.IPA => _db.ProjectIpaFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.SOW => _db.ProjectSowFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.AON => _db.ProjectAonFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.BM => _db.ProjectBenchmarkFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.COB => _db.ProjectCommercialFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.PNC => _db.ProjectPncFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.SO => _db.ProjectSupplyOrderFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            _ => Task.FromResult(true)
        };
}
