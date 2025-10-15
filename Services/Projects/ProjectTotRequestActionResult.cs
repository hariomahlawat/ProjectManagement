namespace ProjectManagement.Services.Projects;

public enum ProjectTotRequestActionStatus
{
    Success,
    NotFound,
    ValidationFailed,
    Conflict
}

public sealed record ProjectTotRequestActionResult(ProjectTotRequestActionStatus Status, string? ErrorMessage = null)
{
    public bool IsSuccess => Status == ProjectTotRequestActionStatus.Success;

    public static ProjectTotRequestActionResult Success() =>
        new(ProjectTotRequestActionStatus.Success);

    public static ProjectTotRequestActionResult NotFound() =>
        new(ProjectTotRequestActionStatus.NotFound);

    public static ProjectTotRequestActionResult ValidationFailed(string message) =>
        new(ProjectTotRequestActionStatus.ValidationFailed, message);

    public static ProjectTotRequestActionResult Conflict(string message) =>
        new(ProjectTotRequestActionStatus.Conflict, message);
}
