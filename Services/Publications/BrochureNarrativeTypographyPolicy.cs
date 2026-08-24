namespace ProjectManagement.Services.Publications;

/// <summary>
/// Centralises brochure body-alignment decisions without changing the measured page plan.
/// A forced float continuation is intentionally kept ragged-right because it begins mid-sentence;
/// treating that fragment as a new justified paragraph can create an objectionably stretched line.
/// </summary>
public static class BrochureNarrativeTypographyPolicy
{
    public static BrochureNarrativeAlignment Normalize(BrochureNarrativeAlignment alignment)
        => Enum.IsDefined(alignment) ? alignment : BrochureNarrativeAlignment.Left;

    public static bool ShouldJustify(
        BrochureNarrativeAlignment requested,
        BrochureNarrativeSegment segment)
    {
        requested = Normalize(requested);
        return requested == BrochureNarrativeAlignment.Justified
               && segment != BrochureNarrativeSegment.Continuation;
    }
}
