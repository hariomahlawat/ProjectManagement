using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Plans;

public class PlanDraftService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<PlanDraftService> _logger;

    public PlanDraftService(ApplicationDbContext db, IClock clock, ILogger<PlanDraftService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PlanVersion> CreateDraftAsync(int projectId, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required to create a draft.", nameof(userId));
        }

        var existing = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .Where(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.Draft)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            if (!existing.StagePlans.Any())
            {
                await _db.Entry(existing).Collection(p => p.StagePlans).LoadAsync(cancellationToken);
            }
            return existing;
        }

        var latestVersion = await _db.PlanVersions
            .Where(p => p.ProjectId == projectId)
            .Select(p => (int?)p.VersionNo)
            .MaxAsync(cancellationToken) ?? 0;

        var stageCodes = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == PlanConstants.StageTemplateVersion)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Code)
            .ToListAsync(cancellationToken);

        if (stageCodes.Count == 0)
        {
            _logger.LogWarning("No stage templates found for version {Version}. Creating an empty draft for project {ProjectId}.",
                PlanConstants.StageTemplateVersion, projectId);
        }

        var plan = new PlanVersion
        {
            ProjectId = projectId,
            VersionNo = latestVersion + 1,
            Title = PlanVersion.ProjectTimelineTitle,
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = userId,
            CreatedOn = _clock.UtcNow,
            AnchorStageCode = PlanConstants.DefaultAnchorStageCode,
            AnchorDate = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            SkipWeekends = true,
            TransitionRule = PlanTransitionRule.NextWorkingDay,
            PncApplicable = true
        };

        foreach (var code in stageCodes)
        {
            plan.StagePlans.Add(new StagePlan
            {
                StageCode = code
            });
        }

        _db.PlanVersions.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);

        return plan;
    }

    public Task<PlanVersion?> GetDraftAsync(int projectId, CancellationToken cancellationToken = default)
    {
        return _db.PlanVersions
            .Include(p => p.StagePlans)
            .Where(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.Draft)
            .OrderByDescending(p => p.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
