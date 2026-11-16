using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Tests.ProjectBriefings;

// Writes one section of a deck's stored settings for a test, through the codec's public writer
// (the single-section writers were used only by tests; review 2 fixes, PR 8).
internal static class CodecTestWrites
{
    public static string WithUpdateSheetOptions(string? json, ProjectBriefingUpdateSheetOptions options)
    {
        var current = ProjectBriefingDeckConfigurationCodec.Read(json);
        return ProjectBriefingDeckConfigurationCodec.WithPresentationOptions(
            json, options, current.StandardSlideOptions, current.ClosingSlideType);
    }

    public static string WithStandardSlideOptions(string? json, ProjectBriefingStandardSlideOptions options)
    {
        var current = ProjectBriefingDeckConfigurationCodec.Read(json);
        return ProjectBriefingDeckConfigurationCodec.WithPresentationOptions(
            json, current.UpdateSheetOptions, options, current.ClosingSlideType);
    }

    public static string WithInstitutionalProfileOptions(string? json, ProjectBriefingInstitutionalProfileOptions options)
    {
        var current = ProjectBriefingDeckConfigurationCodec.Read(json);
        return ProjectBriefingDeckConfigurationCodec.WithPresentationOptions(
            json, current.UpdateSheetOptions, current.StandardSlideOptions, current.ClosingSlideType, options);
    }
}
