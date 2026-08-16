using ProjectManagement.Services.Remarks;

namespace ProjectManagement.Services.ProjectBriefings;

public sealed record ProjectBriefingExternalStatus(
    int ProjectId,
    int RemarkId,
    string Body,
    DateOnly EventDate,
    DateTime EffectiveAtUtc);

public interface IProjectBriefingExternalStatusService
{
    Task<IReadOnlyDictionary<int, ProjectBriefingExternalStatus>> GetLatestAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Briefing-specific adapter over the shared formal-output external remark reader.
///
/// This type deliberately exposes one public constructor only. The built-in
/// Microsoft.Extensions.DependencyInjection container must have an unambiguous
/// activation path when ValidateOnBuild is enabled.
/// </summary>
public sealed class ProjectBriefingExternalStatusService : IProjectBriefingExternalStatusService
{
    private readonly IProjectLatestExternalRemarkService _remarkService;

    public ProjectBriefingExternalStatusService(IProjectLatestExternalRemarkService remarkService)
        => _remarkService = remarkService ?? throw new ArgumentNullException(nameof(remarkService));

    public async Task<IReadOnlyDictionary<int, ProjectBriefingExternalStatus>> GetLatestAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);

        var rows = await _remarkService.GetLatestAsync(projectIds, cancellationToken);
        return rows.ToDictionary(
            pair => pair.Key,
            pair => new ProjectBriefingExternalStatus(
                pair.Value.ProjectId,
                pair.Value.RemarkId,
                pair.Value.Body,
                pair.Value.EventDate,
                pair.Value.EffectiveAtUtc));
    }
}
