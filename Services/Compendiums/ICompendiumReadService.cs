namespace ProjectManagement.Services.Compendiums;

public interface ICompendiumReadService
{
    // Default implementations preserve source compatibility for older test doubles and
    // integrations that only implement the historic automatic-proliferation operation.
    Task<IReadOnlyList<CompendiumCandidateProjectVm>> GetCandidateProjectsAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CompendiumCandidateProjectVm>>(Array.Empty<CompendiumCandidateProjectVm>());

    Task<CompendiumPdfDataDto> GetPublicationAsync(
        CompendiumPublicationRequest request,
        CancellationToken cancellationToken = default)
        => GetProliferationCompendiumAsync(cancellationToken);

    Task<CompendiumPdfDataDto> GetProliferationCompendiumAsync(
        CancellationToken cancellationToken = default);
}
