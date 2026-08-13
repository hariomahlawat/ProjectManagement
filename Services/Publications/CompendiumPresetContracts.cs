using ProjectManagement.Services.Compendiums;

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

public sealed record CompendiumPresetProjectConfiguration(
    int ProjectId,
    int? PrimaryPhotoId = null,
    double PrimaryFocalX = .5d,
    double PrimaryFocalY = .5d,
    CompendiumImageSelectionMode ImageSelectionMode = CompendiumImageSelectionMode.Automatic);

public sealed record CompendiumPresetConfiguration(
    string Title,
    string Subtitle,
    string Edition,
    string? HandlingMarking,
    IReadOnlyList<CompendiumPresetProjectConfiguration> Projects)
{
    public CompendiumPresetConfiguration(
        string title,
        string subtitle,
        string edition,
        string? handlingMarking,
        IReadOnlyList<int> projectIds)
        : this(
            title,
            subtitle,
            edition,
            handlingMarking,
            projectIds
                .Where(projectId => projectId > 0)
                .Select(projectId => new CompendiumPresetProjectConfiguration(projectId))
                .ToArray())
    {
    }

    public IReadOnlyList<int> ProjectIds => Projects.Select(project => project.ProjectId).ToArray();
}

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
