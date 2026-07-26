using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectFactsReadService
{
    private readonly ApplicationDbContext _db;

    public ProjectFactsReadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<bool> HasRequiredFactsAsync(
        int projectId,
        string stageCode,
        CancellationToken ct = default)
        => stageCode switch
        {
            StageCodes.IPA => HasIpaPositionAsync(projectId, ct),
            StageCodes.SOW => _db.ProjectSowFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.AON => _db.ProjectAonFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.BM => _db.ProjectBenchmarkFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.COB => _db.ProjectCommercialFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.PNC => _db.ProjectPncFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            StageCodes.SO => _db.ProjectSupplyOrderFacts.AnyAsync(x => x.ProjectId == projectId, ct),
            _ => Task.FromResult(true)
        };

    private async Task<bool> HasIpaPositionAsync(int projectId, CancellationToken ct)
    {
        if (await _db.ArppEntries
                .AsNoTracking()
                .AnyAsync(entry => entry.ProjectId == projectId, ct))
        {
            return true;
        }

        return await _db.ProjectIpaFacts
            .AsNoTracking()
            .AnyAsync(fact => fact.ProjectId == projectId, ct);
    }
}
