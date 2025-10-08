using System;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

public enum ProjectTotUpdateStatus
{
    Success = 0,
    NotFound = 1,
    ValidationFailed = 2
}

public sealed record ProjectTotUpdateRequest(
    ProjectTotStatus Status,
    DateOnly? StartedOn,
    DateOnly? CompletedOn,
    string? Remarks);

public sealed record ProjectTotUpdateResult(ProjectTotUpdateStatus Status, string? ErrorMessage = null)
{
    public bool IsSuccess => Status == ProjectTotUpdateStatus.Success;

    public static ProjectTotUpdateResult Success() => new(ProjectTotUpdateStatus.Success);

    public static ProjectTotUpdateResult NotFound() => new(ProjectTotUpdateStatus.NotFound);

    public static ProjectTotUpdateResult ValidationFailed(string message) =>
        new(ProjectTotUpdateStatus.ValidationFailed, message);
}
