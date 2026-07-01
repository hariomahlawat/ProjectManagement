namespace ProjectManagement.Models.ProjectIdeas;

public static class ProjectIdeaSorts
{
    public const string LatestActivity = "latest";
    public const string NewestCreated = "newest";
    public const string Title = "title";
    public const string ProjectOfficer = "officer";

    public static readonly IReadOnlyList<string> All =
    [
        LatestActivity,
        NewestCreated,
        Title,
        ProjectOfficer
    ];

    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return LatestActivity;
        }

        return All.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))
            ?? LatestActivity;
    }

    public static string ToDisplay(string value) => Normalise(value) switch
    {
        NewestCreated => "Recently created",
        Title => "Title A–Z",
        ProjectOfficer => "Project Officer",
        _ => "Latest activity"
    };
}
