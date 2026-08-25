namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Authoritative physical-geometry contract for the A4 portrait Compendium. Phase 40 closes the
/// pagination loop by sharing the same header/content/footer geometry between planning and QuestPDF
/// composition instead of allowing each layer to maintain independent height assumptions.
/// </summary>
public static class CompendiumLayoutMetrics
{
    public const float PageWidthPoints = 595.28f;
    public const float PageHeightPoints = 841.89f;

    public const float HorizontalMarginPoints = 38f;
    public const float TopMarginPoints = 28f;
    public const float FooterHeightPoints = 35f;
    public const float RunningHeaderHeightPoints = 28f;
    public const float ProjectContentTopPaddingPoints = 8f;
    public const float SecondaryContentTopPaddingPoints = 12f;

    // Physical body-text contract shared with the semantic typography policy. Keeping these values
    // beside the A4 geometry prevents the planner's safety envelope from drifting away from the
    // actual maximum first-page narrative typography.
    public const float ProjectBodyFontSize = 10f;
    public const float ProjectBodyMinimumFontSize = 9.5f;
    public const float ProjectBodyMaximumNarrativeScale = 1.10f;
    public const float ProjectBodyLineHeightMultiplier = 1.25f;
    public const float MaximumProjectBodyLineHeightPoints =
        ProjectBodyFontSize * ProjectBodyMaximumNarrativeScale * ProjectBodyLineHeightMultiplier;

    // SkiaSharp is used for deterministic planning while QuestPDF performs the final shaping pass.
    // Even with the same bundled DM Sans faces, native text shaping, justification and floating-point
    // rounding can move a boundary word onto one additional QuestPDF line on a deployment machine.
    // Reserve one *maximum-scale* body line plus a small native-shaping tolerance. This reserve is
    // deliberately unavailable to editorial content and is the safety gap protected by ShowEntire().
    public const float PhysicalPaginationNativeShapingTolerancePoints = 2.25f;
    public const float PhysicalPaginationReservePoints =
        MaximumProjectBodyLineHeightPoints + PhysicalPaginationNativeShapingTolerancePoints;
    public const float ContentWidthPoints = PageWidthPoints - (2f * HorizontalMarginPoints);

    /// <summary>
    /// Maximum height available to the inner project-page column after all fixed A4 chrome is
    /// removed. The production shaping reserve is part of the physical pagination contract and absorbs
    /// cross-engine/native shaping variance; it is deliberately not available for editorial content.
    /// </summary>
    public const float ProjectContentHeightPoints =
        PageHeightPoints
        - TopMarginPoints
        - RunningHeaderHeightPoints
        - FooterHeightPoints
        - ProjectContentTopPaddingPoints
        - PhysicalPaginationReservePoints;

    /// <summary>
    /// Maximum height available to index and continuation inner columns.
    /// </summary>
    public const float SecondaryContentHeightPoints =
        PageHeightPoints
        - TopMarginPoints
        - RunningHeaderHeightPoints
        - FooterHeightPoints
        - SecondaryContentTopPaddingPoints
        - PhysicalPaginationReservePoints;

    public const float ProjectImageWidthPoints = ContentWidthPoints;
    public const float ProjectImageLongHeightPoints = 190f;
    public const float ProjectImageMediumHeightPoints = 240f;
    public const float ProjectImageShortHeightPoints = 300f;

    public const float ProjectTitleFontSize = 20f;
    public const float ProjectTitleLineHeightMultiplier = 1.08f;
    public const float ProjectKickerFontSize = 7.3f;
    public const float ProjectKickerLineHeightMultiplier = 1.20f;
    public const float ProjectKickerLetterSpacingPoints = .5f;
    public const float ProjectColumnSpacingPoints = 9f;
    public const float ProjectHeadingRuleHeightPoints = 2f;

    public const float ContinuationTitleFontSize = 17f;
    public const float ContinuationTitleLineHeightMultiplier = 1.06f;
    public const float ContinuationColumnSpacingPoints = 10f;
    public const float ContinuationLabelLineHeightPoints = 10.1f;
    public const float ContinuationHeadingRuleHeightPoints = 2f;

    public const float ContinuationBodyFontSize = 10f;

    // Index geometry mirrors ComposeIndexPage / ComposeIndexGroup exactly.
    public const float IndexColumnSpacingPoints = 12f;
    public const float IndexHeadingTitleFontSize = 22f;
    public const float IndexHeadingTitleLineHeightMultiplier = 1.20f;
    public const float IndexPublicationTitleFontSize = 9.5f;
    public const float IndexPublicationTitleLineHeightMultiplier = 1.20f;
    public const float IndexRuleHeightPoints = 2f;
    public const float IndexGroupHorizontalPaddingPoints = 10f;
    public const float IndexGroupLeftBorderPoints = 4f;
    public const float IndexGroupVerticalPaddingPoints = 7f;
    public const float IndexGroupTitleFontSize = 11.5f;
    public const float IndexGroupTitleLineHeightMultiplier = 1.20f;
    public const float IndexGroupCountReserveWidthPoints = 92f;
    public const float IndexRowHorizontalPaddingPoints = 10f;
    public const float IndexRowVerticalPaddingPoints = 6f;
    public const float IndexRowBorderBottomPoints = 1f;
    public const float IndexProjectNameFontSize = 9.3f;
    public const float IndexProjectNameLineHeightMultiplier = 1.20f;
    public const float IndexLifecycleWidthPoints = 76f;
    public const float IndexPageNumberWidthPoints = 34f;
    public const float IndexMinimumTextLineHeightPoints = 10.8f;

    // Legacy row-unit constants are retained for backward source compatibility. Phase 40 planning
    // no longer uses them; index membership is measured in physical points.
    public const int IndexPageRowUnits = 22;
    public const int IndexCategoryHeaderUnits = 2;
    public const int IndexProjectRowUnits = 1;

    public const int FirstPageDescriptionBudgetPhotoLong = 2250;
    public const int FirstPageDescriptionBudgetPhotoMedium = 1500;
    public const int FirstPageDescriptionBudgetPhotoShort = 650;
    public const int FirstPageDescriptionBudgetWithoutPhoto = 2850;
    public const int ContinuationDescriptionBudget = 3300;

    public static float ResolveProjectTitleFontSize(string? title)
    {
        var length = title?.Trim().Length ?? 0;
        return length switch
        {
            > 105 => 17.5f,
            > 76 => 19f,
            > 54 => 20.5f,
            _ => 22f
        };
    }

    public static float ProjectImageHeightPoints(CompendiumProjectLayoutVariant variant)
        => variant switch
        {
            CompendiumProjectLayoutVariant.PhotoShort => ProjectImageShortHeightPoints,
            CompendiumProjectLayoutVariant.PhotoMedium => ProjectImageMediumHeightPoints,
            CompendiumProjectLayoutVariant.PhotoLong => ProjectImageLongHeightPoints,
            _ => 0f
        };

    public static int FirstPageDescriptionBudget(CompendiumProjectLayoutVariant variant)
        => variant switch
        {
            CompendiumProjectLayoutVariant.PhotoShort => FirstPageDescriptionBudgetPhotoShort,
            CompendiumProjectLayoutVariant.PhotoMedium => FirstPageDescriptionBudgetPhotoMedium,
            CompendiumProjectLayoutVariant.PhotoLong => FirstPageDescriptionBudgetPhotoLong,
            _ => FirstPageDescriptionBudgetWithoutPhoto
        };
}
