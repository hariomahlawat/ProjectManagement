using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

public sealed record ProjectKeyPointDefinition(ProjectKeyPointKind Kind, string Heading, string Hint);

/// <summary>
/// The one definition of the key point headings: their order, wording and the hint shown to the
/// writer. Rewording a heading here applies everywhere; never change what a heading asks.
/// </summary>
public static class ProjectKeyPointCatalog
{
    public static IReadOnlyList<ProjectKeyPointDefinition> All { get; } = new[]
    {
        new ProjectKeyPointDefinition(
            ProjectKeyPointKind.TrainingGap,
            "Training gap",
            "What cannot be trained today without this product."),
        new ProjectKeyPointDefinition(
            ProjectKeyPointKind.OpNecessity,
            "Op necessity",
            "Why the product is operationally required."),
        new ProjectKeyPointDefinition(
            ProjectKeyPointKind.TrainingRealism,
            "Training realism",
            "How close training on it is to the real thing."),
        new ProjectKeyPointDefinition(
            ProjectKeyPointKind.EaseOfLearning,
            "Ease of learning",
            "How quickly a trainee picks it up."),
        new ProjectKeyPointDefinition(
            ProjectKeyPointKind.ProliferationPotential,
            "Proliferation potential",
            "Which other units or users could use it.")
    };

    public static string Heading(ProjectKeyPointKind kind)
        => All.First(definition => definition.Kind == kind).Heading;
}
