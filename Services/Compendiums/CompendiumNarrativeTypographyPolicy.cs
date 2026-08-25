using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Semantic narrative regions used by the Compendium typography policy. Alignment is an
/// editorial choice; physical width remains a planning/measurement concern and must not silently
/// override a publisher-selected Justified treatment.
/// </summary>
public enum CompendiumNarrativeSegment
{
    FullWidth = 0,
    BalancedSide = 1,
    BelowImage = 2,
    Continuation = 3,
    AdditionalNote = 4
}

/// <summary>
/// Shared publication typography safeguards. Phase 44 keeps measurement and alignment concerns
/// separate: Skia/QuestPDF own physical text measurement while this policy applies the publisher's
/// requested alignment consistently to normal prose across semantic narrative regions.
/// </summary>
public static class CompendiumNarrativeTypographyPolicy
{
    public const float MinimumScale = 1f;
    public const float MaximumScale = CompendiumLayoutMetrics.ProjectBodyMaximumNarrativeScale;
    public const float BodyFontSizePoints = CompendiumLayoutMetrics.ProjectBodyFontSize;
    public const float BodyLineHeightMultiplier = CompendiumLayoutMetrics.ProjectBodyLineHeightMultiplier;
    public const float ParagraphSpacingPoints = 6f;
    public const float NarrativeHeadingReservePoints = 26f;

    public static float NormalizeScale(float scale)
        => Math.Clamp(scale, MinimumScale, MaximumScale);

    public static CompendiumNarrativeAlignment Normalize(CompendiumNarrativeAlignment alignment)
        => Enum.IsDefined(alignment) ? alignment : CompendiumNarrativeAlignment.Left;

    /// <summary>
    /// Resolves the effective prose alignment for a semantic narrative region. Phase 44 deliberately
    /// does not width-gate justification: the dossier planner already protects paragraph/sentence
    /// boundaries, while QuestPDF keeps the final line of each paragraph natural.
    /// </summary>
    public static CompendiumNarrativeAlignment ResolveAlignment(
        CompendiumNarrativeAlignment requested,
        CompendiumNarrativeSegment segment)
    {
        _ = segment; // Reserved for future region-specific editorial rules without changing callers.
        return Normalize(requested);
    }

    /// <summary>
    /// Compatibility wrapper retained for existing integrations and tests. Side-column width is
    /// still used by the physical measurement planner, but no longer changes the editorial choice.
    /// </summary>
    public static CompendiumNarrativeAlignment ResolveSideAlignment(
        CompendiumNarrativeAlignment requested,
        float sideColumnWidthPoints)
    {
        _ = sideColumnWidthPoints;
        return ResolveAlignment(requested, CompendiumNarrativeSegment.BalancedSide);
    }

    public static CompendiumNarrativeAlignment ResolveFullWidthAlignment(
        CompendiumNarrativeAlignment requested)
        => ResolveAlignment(requested, CompendiumNarrativeSegment.FullWidth);
}
