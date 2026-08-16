namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Shared publication typography safeguards. This policy is deliberately deterministic so the
/// browser proof and QuestPDF output can present the same effective alignment decision.
/// </summary>
public static class CompendiumNarrativeTypographyPolicy
{
    /// <summary>
    /// Narrow justified columns create objectionable inter-word expansion without deterministic
    /// hyphenation. Below this width the side segment stays left aligned while the full-width
    /// continuation still honours the publisher's Justified choice.
    /// </summary>
    public const float MinimumSafeJustifiedColumnWidthPoints = 245f;

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
