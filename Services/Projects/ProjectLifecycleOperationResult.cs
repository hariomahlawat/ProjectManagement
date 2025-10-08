namespace ProjectManagement.Services.Projects;

public enum ProjectLifecycleOperationStatus
{
    Success,
    NotFound,
    InvalidStatus,
    ValidationFailed
}

public sealed record ProjectLifecycleOperationResult(ProjectLifecycleOperationStatus Status, string? ErrorMessage = null)
{
    public bool IsSuccess => Status == ProjectLifecycleOperationStatus.Success;

    public static ProjectLifecycleOperationResult Success() => new(ProjectLifecycleOperationStatus.Success);

    public static ProjectLifecycleOperationResult NotFound() => new(ProjectLifecycleOperationStatus.NotFound);

    public static ProjectLifecycleOperationResult InvalidStatus(string message) => new(ProjectLifecycleOperationStatus.InvalidStatus, message);

    public static ProjectLifecycleOperationResult ValidationFailed(string message) => new(ProjectLifecycleOperationStatus.ValidationFailed, message);
}
