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
    ProjectBriefingAdditionalSlidePlacement Placement,
    bool AllowMultiple = false,
    bool CanReorder = true);

public static class ProjectBriefingAdditionalSlideCatalog
{
    private static readonly IReadOnlyDictionary<ProjectBriefingAdditionalSlideType, ProjectBriefingAdditionalSlideDefinition> Definitions =
        new Dictionary<ProjectBriefingAdditionalSlideType, ProjectBriefingAdditionalSlideDefinition>
        {
            [ProjectBriefingAdditionalSlideType.InstitutionalProfile] = new(
                ProjectBriefingAdditionalSlideType.InstitutionalProfile,
                "SDD Institutional Profile",
                "Growth, capability and institutional output",
                "bi-building",
                ProjectBriefingAdditionalSlidePlacement.AfterCover),
            [ProjectBriefingAdditionalSlideType.RoleAndCharter] = new(
                ProjectBriefingAdditionalSlideType.RoleAndCharter,
                "Role & Charter",
                "Authorised organisational role and functions",
                "bi-card-checklist",
                ProjectBriefingAdditionalSlidePlacement.AfterCover),
            [ProjectBriefingAdditionalSlideType.FfcGlobalFootprint] = new(
                ProjectBriefingAdditionalSlideType.FfcGlobalFootprint,
                "FFC Global Footprint",
                "Country footprint, project quantities and delivery position",
                "bi-globe-asia-australia",
                ProjectBriefingAdditionalSlidePlacement.BeforeClosing,
                CanReorder: false)
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
