using ProjectManagement.Areas.Compendiums.Application.Dto;

namespace ProjectManagement.Areas.Compendiums.Application;

// SECTION: Read contract for compendium pages and exports
public interface ICompendiumReadService
{
    Task<IReadOnlyList<CompendiumProjectCardDto>> GetEligibleProjectsAsync(CancellationToken cancellationToken);
    Task<CompendiumProjectDetailDto?> GetProjectAsync(int projectId, bool includeHistoricalExtras, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompendiumProjectDetailDto>> GetEligibleProjectDetailsAsync(bool includeHistoricalExtras, CancellationToken cancellationToken);
}
