namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    // The category distributions are drawn as donut charts (ProjectBriefingSlideComposer.CategoryDonuts.cs).
    private static bool IsUncategorisedCategory(string label)
    {
        var normalised = label.Trim().ToLowerInvariant();
        return normalised.Contains("not categorised", StringComparison.Ordinal)
               || normalised.Contains("not categorized", StringComparison.Ordinal)
               || normalised.Contains("uncategorised", StringComparison.Ordinal)
               || normalised.Contains("uncategorized", StringComparison.Ordinal);
    }
}
