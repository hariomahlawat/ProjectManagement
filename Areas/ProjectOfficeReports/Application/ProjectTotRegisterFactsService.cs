using System;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

/// <summary>
/// Reads the ToT register once and resolves each project's facts and the position counts
/// (<see cref="ProjectTotRegisterFactsBuilder"/>), with "today" taken in IST.
/// </summary>
public sealed class ProjectTotRegisterFactsService
{
    private readonly ProjectTotTrackerReadService _tracker;
    private readonly IClock _clock;

    public ProjectTotRegisterFactsService(ProjectTotTrackerReadService tracker, IClock clock)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProjectTotRegister> GetAsync(
        ProjectTotTrackerFilter filter,
        CancellationToken cancellationToken = default)
    {
        var rows = await _tracker.GetAsync(filter, cancellationToken);
        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow.UtcDateTime));
        return ProjectTotRegisterFactsBuilder.Build(rows, today);
    }
}
