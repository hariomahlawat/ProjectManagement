namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Shared publication typography safeguards. Phase 37.1 makes these physical limits the single
/// source of truth for planning, browser proof and QuestPDF rendering.
/// </summary>
public static class CompendiumNarrativeTypographyPolicy
{
    public const float MinimumScale = 1f;
    public const float MaximumScale = 1.10f;
    public const float BodyFontSizePoints = 10f;
    public const float BodyLineHeightMultiplier = 1.25f;
    public const float ParagraphSpacingPoints = 6f;
    public const float NarrativeHeadingReservePoints = 26f;

    /// <summary>
    /// Narrow justified columns create objectionable inter-word expansion without deterministic
    /// hyphenation. Below this width the side segment stays left aligned while the full-width
    /// continuation still honours the publisher's Justified choice.
    /// </summary>
    public const float MinimumSafeJustifiedColumnWidthPoints = 245f;

    public static float NormalizeScale(float scale)
        => Math.Clamp(scale, MinimumScale, MaximumScale);

    public static CompendiumNarrativeAlignment Normalize(CompendiumNarrativeAlignment alignment)
        => Enum.IsDefined(alignment) ? alignment : CompendiumNarrativeAlignment.Left;

    public static CompendiumNarrativeAlignment ResolveSideAlignment(
        CompendiumNarrativeAlignment requested,
        float sideColumnWidthPoints)
    {
        requested = Normalize(requested);
        return requested == CompendiumNarrativeAlignment.Justified
               && sideColumnWidthPoints >= MinimumSafeJustifiedColumnWidthPoints
            ? CompendiumNarrativeAlignment.Justified
            : CompendiumNarrativeAlignment.Left;
    }

    public static CompendiumNarrativeAlignment ResolveFullWidthAlignment(
        CompendiumNarrativeAlignment requested)
        => Normalize(requested);
}
