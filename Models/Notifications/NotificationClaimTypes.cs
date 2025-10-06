namespace ProjectManagement.Models.Notifications;

public static class NotificationClaimTypes
{
    public const string RemarkCreatedOptOut = "notifications:remark-created";
    public const string MentionOptOut = "notifications:mentions";
    public const string PlanEventsOptOut = "notifications:plans";
    public const string StageEventsOptOut = "notifications:stages";
    public const string DocumentEventsOptOut = "notifications:documents";
    public const string RoleChangesOptOut = "notifications:roles";
    public const string ProjectAssignmentOptOut = "notifications:project-assignment";
    public const string OptOutValue = "opt-out";
}
