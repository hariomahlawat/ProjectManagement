using System;
using System.Collections.Generic;

namespace ProjectManagement.Services.ProjectBriefings;

// Aptos letter widths (in em, from the font's own advance widths) so slide text is measured as
// PowerPoint sets it, not as an average letter: the status table measured short cells such as
// "₹54.86 Lakh · L1" a line too long and left rows half empty. Letters not listed count as .60 em.
internal static class AptosMetrics
{
    private const char First = ' ';
    private const double Unknown = .60;

    // ' ' (32) to '~' (126).
    private static readonly double[] Regular =
    {
        0.203, 0.293, 0.370, 0.537, 0.534, 0.826, 0.643, 0.210, 0.293, 0.293, 0.457, 0.534,
        0.286, 0.340, 0.286, 0.339, 0.534, 0.534, 0.534, 0.534, 0.534, 0.534, 0.534, 0.534,
        0.534, 0.534, 0.286, 0.286, 0.534, 0.534, 0.534, 0.501, 0.895, 0.589, 0.604, 0.692,
        0.686, 0.556, 0.524, 0.709, 0.707, 0.260, 0.331, 0.568, 0.500, 0.790, 0.706, 0.732,
        0.577, 0.732, 0.606, 0.566, 0.479, 0.681, 0.585, 0.892, 0.553, 0.540, 0.516, 0.294,
        0.339, 0.294, 0.534, 0.460, 0.552, 0.531, 0.561, 0.525, 0.561, 0.527, 0.301, 0.484,
        0.551, 0.239, 0.239, 0.487, 0.260, 0.853, 0.551, 0.552, 0.561, 0.561, 0.334, 0.486,
        0.323, 0.559, 0.452, 0.721, 0.442, 0.452, 0.438, 0.294, 0.270, 0.294, 0.534,
    };

    private static readonly double[] Bold =
    {
        0.203, 0.293, 0.420, 0.537, 0.534, 0.841, 0.677, 0.232, 0.293, 0.293, 0.457, 0.534,
        0.300, 0.340, 0.300, 0.387, 0.534, 0.534, 0.534, 0.534, 0.534, 0.534, 0.534, 0.534,
        0.534, 0.534, 0.300, 0.300, 0.534, 0.534, 0.534, 0.518, 0.900, 0.620, 0.619, 0.707,
        0.707, 0.573, 0.537, 0.718, 0.729, 0.294, 0.367, 0.623, 0.512, 0.821, 0.723, 0.737,
        0.605, 0.737, 0.634, 0.598, 0.505, 0.696, 0.617, 0.940, 0.599, 0.586, 0.547, 0.337,
        0.387, 0.337, 0.534, 0.460, 0.552, 0.552, 0.586, 0.549, 0.586, 0.556, 0.335, 0.511,
        0.579, 0.268, 0.268, 0.534, 0.296, 0.889, 0.579, 0.573, 0.586, 0.586, 0.369, 0.516,
        0.355, 0.582, 0.494, 0.761, 0.495, 0.494, 0.466, 0.337, 0.270, 0.337, 0.534,
    };

    private static readonly Dictionary<char, (double Regular, double Bold)> Other = new()
    {
        ['₹'] = (0.534, 0.534),
        ['·'] = (0.286, 0.300),
        ['–'] = (0.460, 0.460),
        ['—'] = (0.920, 0.920),
        ['…'] = (0.818, 0.850),
        ['’'] = (0.270, 0.298),
        ['‘'] = (0.270, 0.298),
        ['“'] = (0.452, 0.519),
        ['”'] = (0.453, 0.520),
        ['•'] = (0.470, 0.488),
    };

    // The width of the text in inches at this size.
    public static double Width(string text, double fontSize, bool bold)
    {
        var em = 0d;
        foreach (var character in text ?? string.Empty)
        {
            em += Em(character, bold);
        }

        return em * fontSize / 72d;
    }

    public static double Em(char character, bool bold)
    {
        var index = character - First;
        if (index >= 0 && index < Regular.Length)
        {
            return bold ? Bold[index] : Regular[index];
        }

        return Other.TryGetValue(character, out var widths) ? (bold ? widths.Bold : widths.Regular) : Unknown;
    }
}
