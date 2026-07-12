namespace ProjectManagement.Models.ProjectIdeas;

public static class ProjectIdeaAssignmentFilters
{
    public const string All = "all";
    public const string Assigned = "assigned";
    public const string Unassigned = "unassigned";

    public static readonly IReadOnlyList<string> Values =
    [
        All,
        Assigned,
        Unassigned
    ];

    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return All;
        }

        return Values.FirstOrDefault(candidate =>
                   string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
               ?? All;
    }

    public static string ToDisplay(string? value) => Normalise(value) switch
    {
        Assigned => "Assigned",
        Unassigned => "Unassigned",
        _ => "All assignments"
    };
}

public sealed record ProjectIdeaOfficerOption(string UserId, string DisplayName);
