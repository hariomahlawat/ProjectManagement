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

    Task<CompendiumReviewProjectDto?> GetReviewProjectAsync(
        CompendiumProjectSelection selection,
        CancellationToken cancellationToken = default)
        => GetReviewProjectAsync(selection, CompendiumNarrativeSource.ProjectBrief, cancellationToken);

    Task<CompendiumReviewProjectDto?> GetReviewProjectAsync(
        CompendiumProjectSelection selection,
        CompendiumNarrativeSource narrativeSource,
        CancellationToken cancellationToken = default)
        => GetReviewProjectAsync(selection, narrativeSource, CompendiumNarrativeAlignment.Left, cancellationToken);

    Task<CompendiumReviewProjectDto?> GetReviewProjectAsync(
        CompendiumProjectSelection selection,
        CompendiumNarrativeSource narrativeSource,
        CompendiumNarrativeAlignment defaultNarrativeAlignment,
        CancellationToken cancellationToken = default)
        => GetReviewProjectAsync(
            selection,
            narrativeSource,
            defaultNarrativeAlignment,
            CompendiumProjectParticularsStyle.Panel,
            cancellationToken);

    Task<CompendiumReviewProjectDto?> GetReviewProjectAsync(
        CompendiumProjectSelection selection,
        CompendiumNarrativeSource narrativeSource,
        CompendiumNarrativeAlignment defaultNarrativeAlignment,
        CompendiumProjectParticularsStyle projectParticularsStyle,
        CancellationToken cancellationToken = default)
        => GetReviewProjectAsync(
            selection,
            narrativeSource,
            new CompendiumDossierPresentationDefaults { NarrativeAlignment = defaultNarrativeAlignment },
            projectParticularsStyle,
            cancellationToken);

    Task<CompendiumReviewProjectDto?> GetReviewProjectAsync(
        CompendiumProjectSelection selection,
        CompendiumNarrativeSource narrativeSource,
        CompendiumDossierPresentationDefaults dossierDefaults,
        CompendiumProjectParticularsStyle projectParticularsStyle,
        CancellationToken cancellationToken = default)
        => Task.FromResult<CompendiumReviewProjectDto?>(null);

    Task<CompendiumPdfDataDto> GetProliferationCompendiumAsync(
        CancellationToken cancellationToken = default);
}
