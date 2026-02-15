using ProjectManagement.Models;

namespace ProjectManagement.Models.Projects;

// SECTION: Canonical lifecycle status storage tokens
public static class ProjectLifecycleStatusTokens
{
    public const string Active = nameof(ProjectLifecycleStatus.Active);
    public const string Completed = nameof(ProjectLifecycleStatus.Completed);
    public const string Cancelled = nameof(ProjectLifecycleStatus.Cancelled);
}
