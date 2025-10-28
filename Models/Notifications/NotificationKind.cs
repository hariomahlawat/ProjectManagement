namespace ProjectManagement.Models.Notifications;

public enum NotificationKind
{
    RemarkCreated = 1,
    MentionedInRemark = 2,
    PlanSubmitted = 10,
    PlanApproved = 11,
    PlanRejected = 12,
    StageStatusChanged = 20,
    StageAssigned = 21,
    DocumentPublished = 30,
    DocumentReplaced = 31,
    DocumentArchived = 32,
    DocumentRestored = 33,
    DocumentDeleted = 34,
    RoleAssignmentsChanged = 40,
    ProjectAssignmentChanged = 41,
    TrainingDeleteRequested = 90,
    TrainingDeleteApproved = 91,
    TrainingDeleteRejected = 92,
    ActivityDeleteRequested = 93,
    ActivityDeleteApproved = 94,
    ActivityDeleteRejected = 95
}
