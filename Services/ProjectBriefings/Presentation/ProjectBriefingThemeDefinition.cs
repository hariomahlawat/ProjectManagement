using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed record ProjectBriefingThemeDefinition(
    string Canvas,
    string Surface,
    string SurfaceRaised,
    string SurfaceMuted,
    string TextPrimary,
    string TextSecondary,
    string TextMuted,
    string TextOnAccent,
    string Border,
    string Divider,
    string Accent,
    string AccentSoft,
    string SecondaryAccent,
    string SecondaryAccentSoft,
    string Positive,
    string PositiveSoft,
    string Warning,
    string WarningSoft,
    string Critical,
    string CriticalSoft,
    string TableHeader,
    string TableRow,
    string TableAlternateRow,
    string Placeholder,
    string BrandingPlate,
    string BrandingPlateBorder,
    string CoverCanvas,
    string CoverSurface,
    string CoverText,
    string CoverMuted)
{
    public bool IsDark { get; init; }
}

public static class ProjectBriefingThemeCatalog
{
    private static readonly ProjectBriefingThemeDefinition EditorialLight = new(
        Canvas: "F7F7F5",
        Surface: "FFFFFF",
        SurfaceRaised: "FFFFFF",
        SurfaceMuted: "F0F1F2",
        TextPrimary: "191B20",
        TextSecondary: "4F5661",
        TextMuted: "667085",
        TextOnAccent: "FFFFFF",
        Border: "D7DADE",
        Divider: "C4C9D0",
        Accent: "315FA8",
        AccentSoft: "E8EEF7",
        SecondaryAccent: "2D7F82",
        SecondaryAccentSoft: "E7F1F0",
        Positive: "39765A",
        PositiveSoft: "E8F2EC",
        Warning: "B36B20",
        WarningSoft: "F7EDDF",
        Critical: "B34A4A",
        CriticalSoft: "F8E8E8",
        TableHeader: "202732",
        TableRow: "FFFFFF",
        TableAlternateRow: "F3F4F5",
        Placeholder: "E6E8EA",
        BrandingPlate: "F4F4F2",
        BrandingPlateBorder: "D7DADE",
        CoverCanvas: "F7F7F5",
        CoverSurface: "FFFFFF",
        CoverText: "191B20",
        CoverMuted: "5F6670")
    {
        IsDark = false
    };

    private static readonly ProjectBriefingThemeDefinition GraphiteDark = new(
        Canvas: "15181E",
        Surface: "1D222B",
        SurfaceRaised: "242A34",
        SurfaceMuted: "191D24",
        TextPrimary: "F3F4F6",
        TextSecondary: "C4C9D1",
        TextMuted: "929AA6",
        TextOnAccent: "FFFFFF",
        Border: "353C47",
        Divider: "444C59",
        Accent: "5B7CFA",
        AccentSoft: "263659",
        SecondaryAccent: "4FA6A8",
        SecondaryAccentSoft: "203B3C",
        Positive: "69B889",
        PositiveSoft: "23382D",
        Warning: "D9A54C",
        WarningSoft: "453A24",
        Critical: "DD6B6B",
        CriticalSoft: "482A2A",
        TableHeader: "252C37",
        TableRow: "1D222B",
        TableAlternateRow: "191E26",
        Placeholder: "252B34",
        BrandingPlate: "F4F4F2",
        BrandingPlateBorder: "6B7280",
        CoverCanvas: "101319",
        CoverSurface: "1A1F28",
        CoverText: "F7F8FA",
        CoverMuted: "ACB4C0")
    {
        IsDark = true
    };

    public static ProjectBriefingThemeDefinition Resolve(ProjectBriefingPresentationTheme theme)
        => theme switch
        {
            ProjectBriefingPresentationTheme.GraphiteDark => GraphiteDark,
            _ => EditorialLight
        };
}
