namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public enum ProjectBriefingNarrativeDensity
{
    Sparse = 1,
    Standard = 2,
    Dense = 3
}

public sealed record ProjectBriefingCapabilityTypography(
    double FontSize,
    double LineHeight,
    double SpaceAfter);

public sealed record ProjectBriefingNarrativeTypographyProfile(
    ProjectBriefingNarrativeDensity Density,
    double BodyFontSize,
    double LineSpacingPoints,
    double SpaceAfterPoints);

public static class ProjectBriefingNarrativeTypography
{
    public static ProjectBriefingNarrativeDensity ResolveCapabilityDensity(
        IReadOnlyList<ProjectBriefingCapabilityBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var meaningful = blocks.Where(block => !block.IsMuted).ToArray();
        if (meaningful.Length == 0)
        {
            return ProjectBriefingNarrativeDensity.Standard;
        }

        var characters = meaningful.Sum(block => block.Text?.Trim().Length ?? 0);
        var listItems = meaningful.Count(block => block.Type is ProjectBriefingCapabilityBlockType.Bullet
            or ProjectBriefingCapabilityBlockType.NumberedItem
            or ProjectBriefingCapabilityBlockType.LetteredItem);

        if (meaningful.Length <= 6 && listItems <= 6 && characters <= 520)
        {
            return ProjectBriefingNarrativeDensity.Sparse;
        }

        if (meaningful.Length > 12 || characters > 1_200)
        {
            return ProjectBriefingNarrativeDensity.Dense;
        }

        return ProjectBriefingNarrativeDensity.Standard;
    }

    public static ProjectBriefingCapabilityTypography ResolveCapabilityBlock(
        ProjectBriefingCapabilityBlockType type,
        bool isMuted,
        ProjectBriefingNarrativeDensity density)
    {
        var profile = (type, density) switch
        {
            (ProjectBriefingCapabilityBlockType.Heading, ProjectBriefingNarrativeDensity.Sparse) => (15.0, .252, .120),
            (ProjectBriefingCapabilityBlockType.Heading, ProjectBriefingNarrativeDensity.Dense) => (12.7, .218, .082),
            (ProjectBriefingCapabilityBlockType.Heading, _) => (13.6, .235, .105),

            (ProjectBriefingCapabilityBlockType.Bullet or ProjectBriefingCapabilityBlockType.NumberedItem or ProjectBriefingCapabilityBlockType.LetteredItem,
                ProjectBriefingNarrativeDensity.Sparse) => (14.4, .238, .082),
            (ProjectBriefingCapabilityBlockType.Bullet or ProjectBriefingCapabilityBlockType.NumberedItem or ProjectBriefingCapabilityBlockType.LetteredItem,
                ProjectBriefingNarrativeDensity.Dense) => (11.8, .198, .046),
            (ProjectBriefingCapabilityBlockType.Bullet or ProjectBriefingCapabilityBlockType.NumberedItem or ProjectBriefingCapabilityBlockType.LetteredItem,
                _) => (12.7, .215, .060),

            (_, ProjectBriefingNarrativeDensity.Sparse) => (isMuted ? 13.3 : 14.6, .242, .120),
            (_, ProjectBriefingNarrativeDensity.Dense) => (isMuted ? 11.7 : 12.0, .202, .072),
            _ => (isMuted ? 12.5 : 13.0, .222, .105)
        };

        return new ProjectBriefingCapabilityTypography(profile.Item1, profile.Item2, profile.Item3);
    }

    public static int AdjustCapabilityCharactersPerLine(
        int standardCharactersPerLine,
        ProjectBriefingNarrativeDensity density)
        => density switch
        {
            ProjectBriefingNarrativeDensity.Sparse => Math.Max(24, standardCharactersPerLine - 7),
            ProjectBriefingNarrativeDensity.Dense => standardCharactersPerLine + 8,
            _ => standardCharactersPerLine
        };

    public static ProjectBriefingNarrativeTypographyProfile ResolveProjectBrief(string? value)
    {
        var (characters, paragraphs) = Measure(value);
        if (characters <= 700 && paragraphs <= 4)
        {
            return new ProjectBriefingNarrativeTypographyProfile(
                ProjectBriefingNarrativeDensity.Sparse,
                15.0,
                19.2,
                10.5);
        }

        if (characters <= 1_400 && paragraphs <= 7)
        {
            return new ProjectBriefingNarrativeTypographyProfile(
                ProjectBriefingNarrativeDensity.Standard,
                13.2,
                17.2,
                9.0);
        }

        return new ProjectBriefingNarrativeTypographyProfile(
            ProjectBriefingNarrativeDensity.Dense,
            characters > 2_200 ? 10.4 : 11.6,
            characters > 2_200 ? 12.8 : 14.5,
            6.0);
    }

    public static ProjectBriefingNarrativeTypographyProfile ResolveUpdateSheetBrief(string? value)
    {
        var (characters, paragraphs) = Measure(value);
        if (characters <= 650 && paragraphs <= 4)
        {
            return new ProjectBriefingNarrativeTypographyProfile(
                ProjectBriefingNarrativeDensity.Sparse,
                12.2,
                15.2,
                5.5);
        }

        if (characters <= 1_350 && paragraphs <= 7)
        {
            return new ProjectBriefingNarrativeTypographyProfile(
                ProjectBriefingNarrativeDensity.Standard,
                10.4,
                12.9,
                4.0);
        }

        var denseFont = characters switch
        {
            <= 1_900 => 8.9,
            <= 2_500 => 7.9,
            _ => 7.1
        };
        return new ProjectBriefingNarrativeTypographyProfile(
            ProjectBriefingNarrativeDensity.Dense,
            denseFont,
            Math.Max(denseFont + 1.4, 8.2),
            2.8);
    }

    private static (int Characters, int Paragraphs) Measure(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (0, 0);
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        var paragraphs = normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
        return (normalized.Length, Math.Max(1, paragraphs));
    }
}
