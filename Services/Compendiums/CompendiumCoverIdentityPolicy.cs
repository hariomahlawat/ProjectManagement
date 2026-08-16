using System.Globalization;
using System.Text;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Authoritative curated cover identity policy shared by the Cover Editor and QuestPDF export.
/// Layout, publication colour theme and background treatment remain independent concerns.
/// Pattern geometry is deterministic so a saved publication renders identically on every export.
/// </summary>
public static class CompendiumCoverIdentityPolicy
{
    public const string Gold = "#C9A646";
    public const string GoldSoft = "#E9D9A7";
    public const string White = "#FFFFFF";

    public sealed record ThemeDefinition(
        CompendiumPublicationTheme Theme,
        string DisplayName,
        string ShortName,
        string Primary,
        string Secondary,
        string Surface,
        string Foreground,
        string MutedForeground,
        string PatternLight,
        string PatternDark,
        bool SupportsCamouflage);

    public sealed record BackgroundDefinition(
        CompendiumCoverBackgroundTreatment Treatment,
        string DisplayName,
        string ShortName,
        string Description);

    private static readonly IReadOnlyDictionary<CompendiumPublicationTheme, ThemeDefinition> Themes
        = new Dictionary<CompendiumPublicationTheme, ThemeDefinition>
        {
            [CompendiumPublicationTheme.InstitutionalGreen] = new(
                CompendiumPublicationTheme.InstitutionalGreen,
                "Institutional Green", "Green",
                "#102A23", "#17382F", "#21483D", White, "#D7E3DE",
                "#769A8C", "#091D18", true),
            [CompendiumPublicationTheme.DeepNavy] = new(
                CompendiumPublicationTheme.DeepNavy,
                "Deep Navy", "Navy",
                "#0E2238", "#142D49", "#173A5A", White, "#D7E0E9",
                "#6F8EAD", "#0A192A", true),
            [CompendiumPublicationTheme.Burgundy] = new(
                CompendiumPublicationTheme.Burgundy,
                "Burgundy", "Burgundy",
                "#3A1620", "#4A1E2B", "#5A2636", White, "#E7D9DC",
                "#B27584", "#2A0F16", false),
            [CompendiumPublicationTheme.Graphite] = new(
                CompendiumPublicationTheme.Graphite,
                "Graphite", "Graphite",
                "#22272B", "#2D3439", "#3A4248", White, "#DDE1E4",
                "#8B969D", "#15191C", true),
            [CompendiumPublicationTheme.DeepTeal] = new(
                CompendiumPublicationTheme.DeepTeal,
                "Deep Teal", "Teal",
                "#103A3C", "#164A4D", "#1D5A5E", White, "#D7E7E6",
                "#6EA3A4", "#0A292B", false),
            [CompendiumPublicationTheme.Slate] = new(
                CompendiumPublicationTheme.Slate,
                "Slate", "Slate",
                "#263543", "#324657", "#42596B", White, "#DEE5EA",
                "#8FA7B8", "#1A2630", true)
        };

    private static readonly IReadOnlyList<BackgroundDefinition> Backgrounds =
    [
        new(CompendiumCoverBackgroundTreatment.Solid, "Solid", "Solid", "Clean institutional colour field"),
        new(CompendiumCoverBackgroundTreatment.SubtleGradient, "Subtle Gradient", "Gradient", "Restrained tonal depth"),
        new(CompendiumCoverBackgroundTreatment.TopographicContours, "Topographic Contours", "Contours", "Low-contrast terrain contour lines"),
        new(CompendiumCoverBackgroundTreatment.TechnicalGrid, "Technical Grid", "Grid", "Sparse engineering-grid geometry"),
        new(CompendiumCoverBackgroundTreatment.GeometricMesh, "Geometric Mesh", "Mesh", "Abstract simulation and network geometry"),
        new(CompendiumCoverBackgroundTreatment.Camouflage, "Camouflage", "Camouflage", "Abstract low-contrast military texture")
    ];

    public static ThemeDefinition ResolveTheme(CompendiumPublicationTheme theme)
        => Themes.TryGetValue(NormalizeTheme(theme), out var definition)
            ? definition
            : Themes[CompendiumPublicationTheme.InstitutionalGreen];

    public static CompendiumPublicationTheme NormalizeTheme(CompendiumPublicationTheme theme)
        => Enum.IsDefined(theme) ? theme : CompendiumPublicationTheme.InstitutionalGreen;

    public static CompendiumCoverBackgroundTreatment NormalizeTreatment(CompendiumCoverBackgroundTreatment treatment)
        => Enum.IsDefined(treatment) ? treatment : CompendiumCoverBackgroundTreatment.Solid;

    public static bool IsCompatible(
        CompendiumPublicationTheme theme,
        CompendiumCoverBackgroundTreatment treatment)
    {
        theme = NormalizeTheme(theme);
        treatment = NormalizeTreatment(treatment);
        return treatment != CompendiumCoverBackgroundTreatment.Camouflage
               || ResolveTheme(theme).SupportsCamouflage;
    }

    public static CompendiumCoverBackgroundTreatment NormalizeTreatmentForTheme(
        CompendiumPublicationTheme theme,
        CompendiumCoverBackgroundTreatment treatment)
        => IsCompatible(theme, treatment)
            ? NormalizeTreatment(treatment)
            : CompendiumCoverBackgroundTreatment.Solid;

    public static CompendiumCoverBackgroundTreatment ResolveEffectiveTreatment(
        CompendiumCoverSurface surface,
        CompendiumBackCoverTemplate backTemplate,
        CompendiumPublicationTheme theme,
        CompendiumCoverBackgroundTreatment treatment)
    {
        if (surface == CompendiumCoverSurface.Back && backTemplate == CompendiumBackCoverTemplate.Clean)
        {
            return CompendiumCoverBackgroundTreatment.Solid;
        }

        return NormalizeTreatmentForTheme(theme, treatment);
    }

    public static object BuildClientContract()
        => new
        {
            themes = Themes.Values
                .OrderBy(item => (int)item.Theme)
                .Select(item => new
                {
                    key = item.Theme.ToString(),
                    item.DisplayName,
                    item.ShortName,
                    primary = item.Primary,
                    secondary = item.Secondary,
                    surface = item.Surface,
                    foreground = item.Foreground,
                    mutedForeground = item.MutedForeground,
                    patternLight = item.PatternLight,
                    patternDark = item.PatternDark,
                    accent = Gold,
                    item.SupportsCamouflage
                })
                .ToArray(),
            backgrounds = Backgrounds.Select(item => new
            {
                key = item.Treatment.ToString(),
                item.DisplayName,
                item.ShortName,
                item.Description
            }).ToArray(),
            compatibility = Themes.Values
                .OrderBy(item => (int)item.Theme)
                .ToDictionary(
                    item => item.Theme.ToString(),
                    item => Backgrounds
                        .Where(background => IsCompatible(item.Theme, background.Treatment))
                        .Select(background => background.Treatment.ToString())
                        .ToArray())
        };

    /// <summary>
    /// Creates the deterministic SVG surface used both by the web proof handler and QuestPDF.
    /// The viewBox is normalized so exactly the same geometry can be stretched into any approved cover region.
    /// </summary>
    public static string BuildSurfaceSvg(
        CompendiumPublicationTheme theme,
        CompendiumCoverBackgroundTreatment treatment,
        bool isBackSurface = false,
        float widthPoints = 595f,
        float heightPoints = 842f)
    {
        var definition = ResolveTheme(theme);
        treatment = NormalizeTreatmentForTheme(definition.Theme, treatment);
        var intensity = isBackSurface ? 0.56d : 1d;
        var patternOpacity = PatternOpacity(treatment) * intensity;
        var sb = new StringBuilder(4096);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(F(widthPoints)).Append("\" height=\"").Append(F(heightPoints))
          .Append("\" viewBox=\"0 0 1000 1000\" preserveAspectRatio=\"none\">");

        if (treatment == CompendiumCoverBackgroundTreatment.SubtleGradient)
        {
            sb.Append("<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">")
              .Append("<stop offset=\"0\" stop-color=\"").Append(definition.Primary).Append("\"/>")
              .Append("<stop offset=\"1\" stop-color=\"").Append(definition.Secondary).Append("\"/>")
              .Append("</linearGradient></defs><rect width=\"1000\" height=\"1000\" fill=\"url(#g)\"/>");
        }
        else
        {
            sb.Append("<rect width=\"1000\" height=\"1000\" fill=\"").Append(definition.Primary).Append("\"/>");
        }

        switch (treatment)
        {
            case CompendiumCoverBackgroundTreatment.TopographicContours:
                AppendContours(sb, definition.PatternLight, patternOpacity);
                break;
            case CompendiumCoverBackgroundTreatment.TechnicalGrid:
                AppendGrid(sb, definition.PatternLight, patternOpacity);
                break;
            case CompendiumCoverBackgroundTreatment.GeometricMesh:
                AppendMesh(sb, definition.PatternLight, patternOpacity);
                break;
            case CompendiumCoverBackgroundTreatment.Camouflage:
                AppendCamouflage(sb, definition, patternOpacity);
                break;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static double PatternOpacity(CompendiumCoverBackgroundTreatment treatment)
        => treatment switch
        {
            CompendiumCoverBackgroundTreatment.TopographicContours => 0.105d,
            CompendiumCoverBackgroundTreatment.TechnicalGrid => 0.075d,
            CompendiumCoverBackgroundTreatment.GeometricMesh => 0.085d,
            CompendiumCoverBackgroundTreatment.Camouflage => 0.115d,
            _ => 0d
        };

    private static void AppendContours(StringBuilder sb, string stroke, double opacity)
    {
        var paths = new[]
        {
            "M-80 160 C120 10 250 250 420 130 S730 -5 1080 170",
            "M-70 235 C135 80 275 325 455 205 S760 70 1080 250",
            "M-60 315 C150 155 300 405 485 285 S790 150 1080 330",
            "M-60 610 C115 465 250 680 430 570 S720 430 1070 625",
            "M-80 690 C120 535 270 765 450 650 S755 515 1080 705",
            "M-65 770 C145 620 300 845 485 730 S805 595 1080 785",
            "M160 -90 C20 120 225 250 120 430 S20 735 175 1090",
            "M805 -70 C660 100 880 255 760 420 S680 735 850 1080"
        };
        sb.Append("<g fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"3.2\" opacity=\"")
            .Append(F(opacity)).Append("\">");
        foreach (var path in paths) sb.Append("<path d=\"").Append(path).Append("\"/>");
        sb.Append("</g>");
    }

    private static void AppendGrid(StringBuilder sb, string stroke, double opacity)
    {
        sb.Append("<g fill=\"none\" stroke=\"").Append(stroke).Append("\" opacity=\"").Append(F(opacity)).Append("\">");
        for (var i = 100; i < 1000; i += 100)
        {
            var major = i % 200 == 0;
            sb.Append("<path d=\"M").Append(i).Append(" 0V1000 M0 ").Append(i).Append("H1000\" stroke-width=\"")
              .Append(major ? "2.4" : "1.1").Append("\"/>");
        }
        sb.Append("<path d=\"M0 500H1000 M500 0V1000\" stroke-width=\"3.2\"/>")
          .Append("<circle cx=\"500\" cy=\"500\" r=\"132\" stroke-width=\"1.4\"/>")
          .Append("</g>");
    }

    private static void AppendMesh(StringBuilder sb, string stroke, double opacity)
    {
        var lines = new[]
        {
            "M20 120L210 55L350 185L540 80L740 190L975 85",
            "M-20 410L175 300L355 410L520 280L720 390L1020 305",
            "M25 720L220 610L385 755L585 600L770 725L1010 610",
            "M210 55L175 300L220 610", "M350 185L355 410L385 755",
            "M540 80L520 280L585 600", "M740 190L720 390L770 725"
        };
        sb.Append("<g fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2.4\" opacity=\"")
          .Append(F(opacity)).Append("\">");
        foreach (var line in lines) sb.Append("<path d=\"").Append(line).Append("\"/>");
        foreach (var point in new[] { (210,55),(350,185),(540,80),(740,190),(175,300),(355,410),(520,280),(720,390),(220,610),(385,755),(585,600),(770,725) })
        {
            sb.Append("<circle cx=\"").Append(point.Item1).Append("\" cy=\"").Append(point.Item2).Append("\" r=\"6\" fill=\"")
              .Append(stroke).Append("\" stroke=\"none\"/>");
        }
        sb.Append("</g>");
    }

    private static void AppendCamouflage(StringBuilder sb, ThemeDefinition theme, double opacity)
    {
        sb.Append("<g opacity=\"").Append(F(opacity)).Append("\">")
          .Append("<path fill=\"").Append(theme.Secondary).Append("\" d=\"M-60 60C80 -20 155 30 260 90C350 140 420 70 515 25C625 -25 715 55 805 115C900 180 980 125 1060 80V330C940 390 865 335 770 285C660 230 590 295 500 350C390 415 300 330 205 285C105 235 35 290 -60 350Z\"/>")
          .Append("<path fill=\"").Append(theme.PatternDark).Append("\" d=\"M-80 405C20 340 125 380 215 455C300 525 395 470 480 420C575 360 660 430 735 500C820 585 920 550 1080 455V700C955 760 850 725 760 665C665 600 590 650 500 720C395 800 295 730 205 670C105 600 30 640 -80 705Z\"/>")
          .Append("<path fill=\"").Append(theme.PatternLight).Append("\" d=\"M-50 760C70 695 165 750 255 820C345 890 430 835 520 785C620 730 710 790 790 855C875 925 960 900 1050 850V1080H-50Z\"/>")
          .Append("<path fill=\"").Append(theme.Surface).Append("\" d=\"M115 160C160 105 245 120 295 180C335 230 315 300 250 325C185 350 105 315 85 250C75 215 90 185 115 160ZM675 185C720 135 795 145 840 205C880 260 850 325 790 345C730 365 660 335 645 275C635 240 650 210 675 185ZM395 520C445 455 530 465 575 530C615 590 580 655 515 675C450 695 375 655 365 595C360 565 375 540 395 520Z\"/>")
          .Append("</g>");
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
