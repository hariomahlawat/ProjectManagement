namespace ProjectManagement.Models.Projects;

// SECTION: Completed project technology status codes
public static class ProjectTechStatusCodes
{
    // SECTION: Status literals
    public const string Current = "Current";
    public const string Outdated = "Outdated";
    public const string Obsolete = "Obsolete";

    // SECTION: Helper collections
    public static readonly string[] All =
    {
        Current,
        Outdated,
        Obsolete
    };
}
