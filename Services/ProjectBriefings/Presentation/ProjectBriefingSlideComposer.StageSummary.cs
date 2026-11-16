using System.Globalization;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

// Colour and number helpers the summary slides share (the stage-wise summary they were written for
// was retired in blocks plan PR 7).
public sealed partial class ProjectBriefingSlideComposer
{
    private static string BlendStageSummaryColor(
        string foreground,
        string background,
        double backgroundWeight)
    {
        var foregroundHex = foreground.Trim().TrimStart('#');
        var backgroundHex = background.Trim().TrimStart('#');
        if (foregroundHex.Length != 6 || backgroundHex.Length != 6)
        {
            return foreground;
        }

        backgroundWeight = Math.Clamp(backgroundWeight, 0d, 1d);

        static int Channel(string value, int start)
            => int.Parse(value.Substring(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        static int Mix(int foregroundValue, int backgroundValue, double backgroundRatio)
            => (int)Math.Round((foregroundValue * (1d - backgroundRatio)) + (backgroundValue * backgroundRatio));

        var red = Mix(Channel(foregroundHex, 0), Channel(backgroundHex, 0), backgroundWeight);
        var green = Mix(Channel(foregroundHex, 2), Channel(backgroundHex, 2), backgroundWeight);
        var blue = Mix(Channel(foregroundHex, 4), Channel(backgroundHex, 4), backgroundWeight);
        return $"{red:X2}{green:X2}{blue:X2}";
    }

    private static string FormatPercentage(int count, int total)
        => total <= 0
            ? "0%"
            : (count * 100d / total).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
