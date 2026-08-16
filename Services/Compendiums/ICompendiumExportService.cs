namespace ProjectManagement.Services.Compendiums;

public sealed record CompendiumExportRequest(
    string? HandlingMarking = null,
    IReadOnlyList<int>? SelectedProjectIds = null,
    string? Title = null,
    string? Subtitle = null,
    string? Edition = null,
    IReadOnlyList<CompendiumProjectSelection>? ProjectSelections = null,
    bool RequireAllReviewed = false,
    CompendiumCoverImageMode CoverImageMode = CompendiumCoverImageMode.Automatic,
    int? CoverHeroProjectId = null,
    int? CoverHeroPhotoId = null,
    double CoverFocalX = .5d,
    double CoverFocalY = .5d)
{
    public CompendiumNarrativeSource NarrativeSource { get; init; } = CompendiumNarrativeSource.ProjectBrief;
    public CompendiumNarrativeAlignment DefaultNarrativeAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public CompendiumProjectParticularsStyle ProjectParticularsStyle { get; init; } = CompendiumProjectParticularsStyle.Panel;
    public CompendiumGroupingMode GroupingMode { get; init; } = CompendiumGroupingMode.TechnicalCategory;
    public CompendiumSortMode SortMode { get; init; } = CompendiumSortMode.Manual;
    public IReadOnlyList<CompendiumPublicationSection> Sections { get; init; } = Array.Empty<CompendiumPublicationSection>();
    public CompendiumCoverDesign? CoverDesign { get; init; }
    public IReadOnlyList<CompendiumPhotoPreference> PhotoPreferences { get; init; } = Array.Empty<CompendiumPhotoPreference>();
}

public sealed record CompendiumExportResult(
    byte[] Bytes,
    string FileName,
    int ProjectCount = 0,
    int CategoryCount = 0)
{
    public bool IsCompositionVerified { get; init; }
    public int PhysicalPageCount { get; init; }
}

public interface ICompendiumExportService
{
    Task<CompendiumExportResult> GenerateAsync(
        CancellationToken cancellationToken = default);

    // Default implementation preserves older test doubles/integrations that only implement the
    // historic parameterless export. Production authored exports override this implementation.
    Task<CompendiumExportResult> GenerateAsync(
        CompendiumExportRequest request,
        CancellationToken cancellationToken = default)
        => GenerateAsync(cancellationToken);
}
