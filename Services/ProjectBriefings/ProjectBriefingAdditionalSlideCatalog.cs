using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Registry of approved optional briefing-slide types. The deck stores only the stable
/// enum identifier and ordered instance list; presentation-specific configuration remains
/// owned by the corresponding slide type.
/// </summary>
public sealed record ProjectBriefingAdditionalSlideDefinition(
    ProjectBriefingAdditionalSlideType Type,
    string DisplayName,
    string Description,
    string IconCssClass,
    bool AllowMultiple = false);

public static class ProjectBriefingAdditionalSlideCatalog
{
    private static readonly IReadOnlyDictionary<ProjectBriefingAdditionalSlideType, ProjectBriefingAdditionalSlideDefinition> Definitions =
        new Dictionary<ProjectBriefingAdditionalSlideType, ProjectBriefingAdditionalSlideDefinition>
        {
            [ProjectBriefingAdditionalSlideType.InstitutionalProfile] = new(
                ProjectBriefingAdditionalSlideType.InstitutionalProfile,
                "SDD Institutional Profile",
                "Growth, capability and institutional output",
                "bi-building"),
            [ProjectBriefingAdditionalSlideType.RoleAndCharter] = new(
                ProjectBriefingAdditionalSlideType.RoleAndCharter,
                "Role & Charter",
                "Authorised organisational role and functions",
                "bi-card-checklist")
        };

    public static IReadOnlyList<ProjectBriefingAdditionalSlideDefinition> All { get; } =
        Definitions.Values.OrderBy(definition => (int)definition.Type).ToArray();

    public static ProjectBriefingAdditionalSlideDefinition Get(ProjectBriefingAdditionalSlideType type)
        => Definitions.TryGetValue(type, out var definition)
            ? definition
            : throw new InvalidOperationException($"Additional slide type '{type}' is not registered.");

    public static bool IsRegistered(ProjectBriefingAdditionalSlideType type)
        => Definitions.ContainsKey(type);
}
