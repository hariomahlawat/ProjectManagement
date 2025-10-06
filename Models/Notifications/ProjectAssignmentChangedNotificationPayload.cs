namespace ProjectManagement.Models.Notifications;

public sealed record ProjectAssignmentChangedNotificationPayload(
    int ProjectId,
    string ProjectName,
    string? PreviousProjectOfficerUserId,
    string? PreviousProjectOfficerName,
    string? CurrentProjectOfficerUserId,
    string? CurrentProjectOfficerName);
