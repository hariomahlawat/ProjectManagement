namespace ProjectManagement.Services.Projects;

public enum ProjectModerationStatus
{
    Success,
    NotFound,
    InvalidState,
    ValidationFailed
}

public sealed record ProjectModerationResult(ProjectModerationStatus Status, string? Error = null)
{
    public static ProjectModerationResult Success() => new(ProjectModerationStatus.Success);

    public static ProjectModerationResult NotFound() => new(ProjectModerationStatus.NotFound);

    public static ProjectModerationResult InvalidState(string message) =>
        new(ProjectModerationStatus.InvalidState, message);

    public static ProjectModerationResult ValidationFailed(string message) =>
        new(ProjectModerationStatus.ValidationFailed, message);
}
