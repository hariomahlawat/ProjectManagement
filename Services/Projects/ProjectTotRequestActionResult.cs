namespace ProjectManagement.Services.Projects;

// SECTION: Status values
public enum ProjectTotRequestActionStatus
{
    Success,
    NotFound,
    Forbidden,
    ValidationFailed,
    Conflict
}

// SECTION: Result helpers
public sealed record ProjectTotRequestActionResult(ProjectTotRequestActionStatus Status, string? ErrorMessage = null)
{
    public bool IsSuccess => Status == ProjectTotRequestActionStatus.Success;

    public static ProjectTotRequestActionResult Success() =>
        new(ProjectTotRequestActionStatus.Success);

    public static ProjectTotRequestActionResult NotFound() =>
        new(ProjectTotRequestActionStatus.NotFound);

    public static ProjectTotRequestActionResult Forbidden(string message) =>
        new(ProjectTotRequestActionStatus.Forbidden, message);

    public static ProjectTotRequestActionResult ValidationFailed(string message) =>
        new(ProjectTotRequestActionStatus.ValidationFailed, message);

    public static ProjectTotRequestActionResult Conflict(string message) =>
        new(ProjectTotRequestActionStatus.Conflict, message);
}
