namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Conservative cover-identity fitting policy shared by browser proof and PDF composition.
/// It reduces typography only when wording pressure warrants it and never silently truncates text.
/// </summary>
public static class CompendiumCoverTypographyPolicy
{
    public const int TitleSoftLength = 42;
    public const int TitleMediumLength = 70;
    public const int TitleLongLength = 95;
    public const float MinimumTitleSize = 21f;
    public const int SubtitleSoftLength = 105;
    public const int SubtitleLongLength = 145;
    public const float MinimumSubtitleSize = 12.5f;

    public static float ResolveTitleSize(string? title, float preferredSize)
    {
        var length = NormalizedLength(title);
        var reduction = length switch
        {
            > TitleLongLength => 6f,
            > TitleMediumLength => 4f,
            > TitleSoftLength => 2f,
            _ => 0f
        };
        return Math.Max(MinimumTitleSize, preferredSize - reduction);
    }

    public static float ResolveSubtitleSize(string? subtitle, float preferredSize = 14f)
    {
        var length = NormalizedLength(subtitle);
        var reduction = length switch
        {
            > SubtitleLongLength => 1.5f,
            > SubtitleSoftLength => 1f,
            _ => 0f
        };
        return Math.Max(MinimumSubtitleSize, preferredSize - reduction);
    }

    public static bool NeedsAdvisory(string? title, string? subtitle)
        => NormalizedLength(title) > TitleLongLength
           || NormalizedLength(subtitle) > SubtitleLongLength;

    public static object BuildClientContract()
        => new
        {
            titleSoftLength = TitleSoftLength,
            titleMediumLength = TitleMediumLength,
            titleLongLength = TitleLongLength,
            minimumTitleSize = MinimumTitleSize,
            subtitleSoftLength = SubtitleSoftLength,
            subtitleLongLength = SubtitleLongLength,
            minimumSubtitleSize = MinimumSubtitleSize,
            frontTitleSizes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                [CompendiumFrontCoverTemplate.InstitutionalHero.ToString()] = 34f,
                [CompendiumFrontCoverTemplate.FullBleedHero.ToString()] = 33f,
                [CompendiumFrontCoverTemplate.EditorialSplit.ToString()] = 31f,
                [CompendiumFrontCoverTemplate.Triptych.ToString()] = 29f,
                [CompendiumFrontCoverTemplate.PortfolioQuartet.ToString()] = 28f,
                [CompendiumFrontCoverTemplate.Minimal.ToString()] = 34f
            },
            backTitleSizes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                [CompendiumBackCoverTemplate.MinimalInstitutional.ToString()] = 24f,
                [CompendiumBackCoverTemplate.ImageEcho.ToString()] = 25f,
                [CompendiumBackCoverTemplate.PortfolioStrip.ToString()] = 25f,
                [CompendiumBackCoverTemplate.TypographyOnly.ToString()] = 25f,
                [CompendiumBackCoverTemplate.Clean.ToString()] = 27f
            }
        };

    private static int NormalizedLength(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? 0
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Length;
}
