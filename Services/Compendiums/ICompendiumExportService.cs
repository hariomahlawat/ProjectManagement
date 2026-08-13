namespace ProjectManagement.Services.Compendiums;

public sealed record CompendiumExportRequest(
    string? HandlingMarking = null,
    IReadOnlyList<int>? SelectedProjectIds = null,
    string? Title = null,
    string? Subtitle = null,
    string? Edition = null);

public sealed record CompendiumExportResult(
    byte[] Bytes,
    string FileName,
    int ProjectCount = 0,
    int CategoryCount = 0);

public interface ICompendiumExportService
{
    Task<CompendiumExportResult> GenerateAsync(
        CancellationToken cancellationToken = default);

    // Default implementation preserves older test doubles/integrations that only implement the
    // historic parameterless export. The production service overrides this for authored selection.
    Task<CompendiumExportResult> GenerateAsync(
        CompendiumExportRequest request,
        CancellationToken cancellationToken = default)
        => GenerateAsync(cancellationToken);
}
