using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

// The design every project slide in a deck uses (docs/ux/briefing-decks.md, "Plan: Blocks, Project
// Officer decks and focus view", PR 5). A design is not stored on its own: it is read from, and
// written as, the settings decks already have (layout, what each project gets, key points), so
// older builds still generate every deck. Key points only is the one new setting; a build that
// does not know it shows the Key points design instead.
public enum ProjectBriefingProjectSlideDesign
{
    Capability = 1,
    KeyPoints = 2,
    Brief = 3,
    FactSheet = 4,
    KeyPointsOnly = 5,
    // A capability slide and a brief slide for each project. Offered only to decks that already
    // use it; the five designs above are the choices (owner, 16 Nov 2026).
    CapabilityAndBrief = 6
}

public sealed record ProjectBriefingProjectSlideDesignSettings(
    ProjectBriefingLayout Layout,
    ProjectBriefingNarrativeMode NarrativeMode,
    ProjectBriefingKeyPointPlacement KeyPoints,
    bool KeyPointsOnly);

public static class ProjectBriefingProjectSlideDesigns
{
    // The choices, in the order the Deck tab shows them.
    public static IReadOnlyList<ProjectBriefingProjectSlideDesign> Choices { get; } = new[]
    {
        ProjectBriefingProjectSlideDesign.Capability,
        ProjectBriefingProjectSlideDesign.KeyPoints,
        ProjectBriefingProjectSlideDesign.Brief,
        ProjectBriefingProjectSlideDesign.FactSheet,
        ProjectBriefingProjectSlideDesign.KeyPointsOnly
    };

    public static ProjectBriefingProjectSlideDesign Of(
        ProjectBriefingLayout layout,
        ProjectBriefingNarrativeMode narrativeMode,
        ProjectBriefingStandardSlideOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (layout == ProjectBriefingLayout.ProjectUpdateSheet)
        {
            return ProjectBriefingProjectSlideDesign.FactSheet;
        }

        return narrativeMode switch
        {
            ProjectBriefingNarrativeMode.Both => ProjectBriefingProjectSlideDesign.CapabilityAndBrief,
            ProjectBriefingNarrativeMode.ProjectBrief => ProjectBriefingProjectSlideDesign.Brief,
            _ when options.KeyPoints == ProjectBriefingKeyPointPlacement.None => ProjectBriefingProjectSlideDesign.Capability,
            _ when options.KeyPointsOnly => ProjectBriefingProjectSlideDesign.KeyPointsOnly,
            _ => ProjectBriefingProjectSlideDesign.KeyPoints
        };
    }

    // The settings a design stands for. Brief, and capability and brief, keep the person's key point
    // choice (a band on the slide); a fact sheet keeps the deck's other settings for when it changes back.
    public static ProjectBriefingProjectSlideDesignSettings Settings(
        ProjectBriefingProjectSlideDesign design,
        ProjectBriefingNarrativeMode currentNarrativeMode,
        ProjectBriefingKeyPointPlacement currentKeyPoints,
        bool showKeyPoints,
        ProjectBriefingKeyPointPlacement keyPointSlide)
        => design switch
        {
            ProjectBriefingProjectSlideDesign.Capability => new(
                ProjectBriefingLayout.StandardBriefing,
                ProjectBriefingNarrativeMode.CapabilityOverview,
                ProjectBriefingKeyPointPlacement.None,
                false),
            ProjectBriefingProjectSlideDesign.KeyPoints => new(
                ProjectBriefingLayout.StandardBriefing,
                ProjectBriefingNarrativeMode.CapabilityOverview,
                ProjectBriefingKeyPointPlacement.CapabilitySlide,
                false),
            ProjectBriefingProjectSlideDesign.KeyPointsOnly => new(
                ProjectBriefingLayout.StandardBriefing,
                ProjectBriefingNarrativeMode.CapabilityOverview,
                ProjectBriefingKeyPointPlacement.CapabilitySlide,
                true),
            ProjectBriefingProjectSlideDesign.Brief => new(
                ProjectBriefingLayout.StandardBriefing,
                ProjectBriefingNarrativeMode.ProjectBrief,
                showKeyPoints ? ProjectBriefingKeyPointPlacement.ProjectBriefSlide : ProjectBriefingKeyPointPlacement.None,
                false),
            ProjectBriefingProjectSlideDesign.CapabilityAndBrief => new(
                ProjectBriefingLayout.StandardBriefing,
                ProjectBriefingNarrativeMode.Both,
                !showKeyPoints
                    ? ProjectBriefingKeyPointPlacement.None
                    : keyPointSlide == ProjectBriefingKeyPointPlacement.CapabilitySlide
                        ? ProjectBriefingKeyPointPlacement.CapabilitySlide
                        : ProjectBriefingKeyPointPlacement.ProjectBriefSlide,
                false),
            ProjectBriefingProjectSlideDesign.FactSheet => new(
                ProjectBriefingLayout.ProjectUpdateSheet,
                Enum.IsDefined(currentNarrativeMode) ? currentNarrativeMode : ProjectBriefingNarrativeMode.CapabilityOverview,
                Enum.IsDefined(currentKeyPoints) ? currentKeyPoints : ProjectBriefingKeyPointPlacement.None,
                false),
            _ => throw new InvalidOperationException("Choose a design for the project slides.")
        };

    // Where key points go when the person switches them on: the only project slide the content
    // draws, or with both slides, the one chosen.
    public static ProjectBriefingKeyPointPlacement KeyPointPlacement(
        ProjectBriefingNarrativeMode narrativeMode,
        bool showKeyPoints,
        ProjectBriefingKeyPointPlacement keyPointSlide)
    {
        if (!showKeyPoints)
        {
            return ProjectBriefingKeyPointPlacement.None;
        }

        return narrativeMode switch
        {
            ProjectBriefingNarrativeMode.CapabilityOverview => ProjectBriefingKeyPointPlacement.CapabilitySlide,
            ProjectBriefingNarrativeMode.ProjectBrief => ProjectBriefingKeyPointPlacement.ProjectBriefSlide,
            _ => keyPointSlide == ProjectBriefingKeyPointPlacement.CapabilitySlide
                ? ProjectBriefingKeyPointPlacement.CapabilitySlide
                : ProjectBriefingKeyPointPlacement.ProjectBriefSlide
        };
    }

    // The settings a Deck tab save stands for: the posted design, or for a page that posts none
    // (an older page during a deploy), the design its own fields describe.
    public static ProjectBriefingProjectSlideDesignSettings Resolve(
        ProjectBriefingProjectSlideDesign? design,
        ProjectBriefingLayout layout,
        ProjectBriefingNarrativeMode narrativeMode,
        bool showKeyPoints,
        ProjectBriefingKeyPointPlacement keyPointSlide)
    {
        var keyPoints = KeyPointPlacement(narrativeMode, showKeyPoints, keyPointSlide);
        return Settings(
            design ?? Of(layout, narrativeMode, ProjectBriefingStandardSlideOptions.Default with { KeyPoints = keyPoints }),
            narrativeMode,
            keyPoints,
            showKeyPoints,
            keyPointSlide);
    }

    // The Deck tab's rules for each design. They are written on the design's card, so the page
    // script does not repeat them (review 2 fixes, PR 5).

    // The layout a design draws; a fact sheet is the update sheet.
    public static ProjectBriefingLayout Layout(ProjectBriefingProjectSlideDesign design)
        => design == ProjectBriefingProjectSlideDesign.FactSheet
            ? ProjectBriefingLayout.ProjectUpdateSheet
            : ProjectBriefingLayout.StandardBriefing;

    // Whether choosing the design switches key points on (true) or off (false); null keeps the choice.
    public static bool? KeyPointsWhenChosen(ProjectBriefingProjectSlideDesign design) => design switch
    {
        ProjectBriefingProjectSlideDesign.KeyPoints or ProjectBriefingProjectSlideDesign.KeyPointsOnly => true,
        ProjectBriefingProjectSlideDesign.Capability => false,
        _ => null
    };

    // Key points only always shows them; the chip cannot switch them off.
    public static bool KeyPointsLocked(ProjectBriefingProjectSlideDesign design)
        => design == ProjectBriefingProjectSlideDesign.KeyPointsOnly;

    // The design the key point chip turns this one into: key points on a capability slide is the
    // Key points design, and off is Capability.
    public static ProjectBriefingProjectSlideDesign WithKeyPoints(ProjectBriefingProjectSlideDesign design, bool on)
        => (design, on) switch
        {
            (ProjectBriefingProjectSlideDesign.Capability, true) => ProjectBriefingProjectSlideDesign.KeyPoints,
            (ProjectBriefingProjectSlideDesign.KeyPoints, false) => ProjectBriefingProjectSlideDesign.Capability,
            _ => design
        };

    // A brief slide whose layout the person chooses. The key points layout draws its own, so the
    // choice shows only while key points are off.
    public static bool UsesBriefLayout(ProjectBriefingProjectSlideDesign design)
        => design is ProjectBriefingProjectSlideDesign.Brief or ProjectBriefingProjectSlideDesign.CapabilityAndBrief;

    // Two project slides, so the person chooses which one carries the key points.
    public static bool OffersKeyPointSlide(ProjectBriefingProjectSlideDesign design)
        => design == ProjectBriefingProjectSlideDesign.CapabilityAndBrief;

    // The line under the chips while the design is chosen.
    public static string Hint(ProjectBriefingProjectSlideDesign design) => design switch
    {
        ProjectBriefingProjectSlideDesign.KeyPointsOnly => "No brief or capability text. A project without key points shows the gap, like other missing content.",
        ProjectBriefingProjectSlideDesign.KeyPoints => "Key points sit in a band at the foot of the slide. Switch them off for the Capability design.",
        ProjectBriefingProjectSlideDesign.Capability => "Switch key points on for the Key points design. Anything switched off gives its space to the photo and text.",
        ProjectBriefingProjectSlideDesign.Brief => "Key points add a band at the foot of the brief slide. Anything switched off gives its space to the photo and text.",
        ProjectBriefingProjectSlideDesign.CapabilityAndBrief => "An older choice, kept for this deck: two slides per project.",
        _ => string.Empty
    };

    public static string DisplayName(ProjectBriefingProjectSlideDesign design) => design switch
    {
        ProjectBriefingProjectSlideDesign.Capability => "Capability",
        ProjectBriefingProjectSlideDesign.KeyPoints => "Key points",
        ProjectBriefingProjectSlideDesign.Brief => "Brief",
        ProjectBriefingProjectSlideDesign.FactSheet => "Fact sheet",
        ProjectBriefingProjectSlideDesign.KeyPointsOnly => "Key points only",
        ProjectBriefingProjectSlideDesign.CapabilityAndBrief => "Capability and brief",
        _ => design.ToString()
    };

    public static string Description(ProjectBriefingProjectSlideDesign design) => design switch
    {
        ProjectBriefingProjectSlideDesign.Capability => "Photo beside the capability points.",
        ProjectBriefingProjectSlideDesign.KeyPoints => "Photo and capability text, five key points below.",
        ProjectBriefingProjectSlideDesign.Brief => "The narrative brief with a large photo.",
        ProjectBriefingProjectSlideDesign.FactSheet => "A formal facts table, brief and photo.",
        ProjectBriefingProjectSlideDesign.KeyPointsOnly => "A large photo beside the five key points; no brief or capability text.",
        ProjectBriefingProjectSlideDesign.CapabilityAndBrief => "A capability slide, then a brief slide, for each project.",
        _ => string.Empty
    };
}
