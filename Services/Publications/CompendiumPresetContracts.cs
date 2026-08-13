namespace ProjectManagement.Services.Publications;

public enum CompendiumPresetDiagnosticSeverity
{
    Information = 0,
    Warning = 1
}

public sealed record CompendiumPresetDiagnostic(
    CompendiumPresetDiagnosticSeverity Severity,
    string Code,
    string Message,
    int? ProjectId = null,
    string? ProjectName = null);

public sealed record CompendiumPresetSummaryVm(
    long Id,
    string Name,
    string? Description,
    int ProjectCount,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedByDisplay,
    string RowVersion);

public sealed record CompendiumPresetConfiguration(
    string Title,
    string Subtitle,
    string Edition,
    string? HandlingMarking,
    IReadOnlyList<int> ProjectIds);

public sealed record CompendiumPresetLoadResult(
    CompendiumPresetSummaryVm Preset,
    CompendiumPresetConfiguration Configuration,
    IReadOnlyList<CompendiumPresetDiagnostic> Diagnostics);

public sealed record CompendiumPresetMutationResult(CompendiumPresetSummaryVm Preset);

public sealed class CompendiumPresetConcurrencyException : Exception
{
    public CompendiumPresetConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
