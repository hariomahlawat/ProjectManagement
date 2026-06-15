namespace ProjectManagement.Models.ProjectIdeas;

public static class ProjectIdeaStatuses
{
    public const string Active = "Active";
    public const string OnHold = "OnHold";
    public const string Archived = "Archived";

    public static readonly IReadOnlyList<string> All = new[] { Active, OnHold, Archived };

    public static string ToDisplay(string status) => status == OnHold ? "On Hold" : status;
}
