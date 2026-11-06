using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Admin;

/// <summary>
/// Turns an audit action code such as <c>Arpp.IpaStageStartupReconciled</c> into a readable,
/// sentence-case title ("ARPP IPA stage startup reconciled") for actions the catalogue does
/// not name. Known short forms keep their usual casing.
/// </summary>
public static partial class AuditActionTitleFormatter
{
    public const string DefaultTitle = "Administrative action";

    // Words written with a fixed casing wherever they appear. Keys match whole words,
    // case-insensitively, after the code is split.
    private static readonly IReadOnlyDictionary<string, string> FixedWords =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARPP"] = "ARPP",
            ["IPA"] = "IPA",
            ["PDF"] = "PDF",
            ["ToT"] = "ToT",
            ["FFC"] = "FFC",
            ["IPR"] = "IPR",
            ["AON"] = "AON",
            ["PNC"] = "PNC",
            ["SOW"] = "SOW",
            ["LPP"] = "LPP",
            ["OCR"] = "OCR",
            ["CSV"] = "CSV",
            ["IP"] = "IP",
            ["ID"] = "ID",
            ["HoD"] = "HoD",
            ["CoE"] = "CoE",
            ["PowerPoint"] = "PowerPoint",
            ["PRISM"] = "PRISM"
        };

    // Mixed-case words that the PascalCase split would otherwise break apart.
    private static readonly (string Code, string Words)[] Compounds =
    {
        ("PowerPoint", "PowerPoint"),
        ("ToT", "ToT"),
        ("HoD", "HoD"),
        ("CoE", "CoE"),
        ("DocRepo", "document repository")
    };

    // Leading namespaces that only say which module wrote the event.
    private static readonly string[] NoisePrefixes =
    {
        "ProjectOfficeReports.",
        "Admin."
    };

    // Marks a compound placeholder; the split never breaks "#0#".
    private const char Marker = '#';

    public static string Format(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return DefaultTitle;

        var text = action.Trim().Replace(Marker, ' ');
        foreach (var prefix in NoisePrefixes)
        {
            if (text.Length > prefix.Length && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..];
                break;
            }
        }

        for (var index = 0; index < Compounds.Length; index++)
        {
            text = Regex.Replace(
                text,
                $"(?<![a-z]){Compounds[index].Code}(?![a-z])",
                $" {Marker}{index}{Marker} ");
        }

        text = text.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        text = WordBoundaryRegex().Replace(text, " ");

        var words = MultiSpaceRegex()
            .Split(text.Trim())
            .Where(word => word.Length > 0)
            .SelectMany(ExpandPlaceholder)
            .Where(word => word.Length > 0)
            .ToList();
        if (words.Count == 0) return DefaultTitle;

        // "Projects.ProjectCreated" and similar codes repeat a word across the dot.
        for (var index = words.Count - 1; index > 0; index--)
        {
            if (string.Equals(words[index], words[index - 1], StringComparison.OrdinalIgnoreCase))
            {
                words.RemoveAt(index);
            }
        }

        return string.Join(' ', words.Select((word, index) => CaseWord(word, index == 0)));
    }

    private static IEnumerable<string> ExpandPlaceholder(string word)
    {
        if (word.Length > 2 && word[0] == Marker && word[^1] == Marker
            && int.TryParse(word[1..^1], out var index) && index >= 0 && index < Compounds.Length)
        {
            return Compounds[index].Words.Split(' ');
        }

        return new[] { word.Trim(Marker) };
    }

    private static string CaseWord(string word, bool first)
    {
        if (FixedWords.TryGetValue(word, out var fixedWord)) return fixedWord;

        // Any other all-capitals word of two or more letters is a short form; keep it.
        if (word.Length > 1 && word.All(ch => !char.IsLetter(ch) || char.IsUpper(ch))) return word;

        var lower = word.ToLowerInvariant();
        return first ? char.ToUpperInvariant(lower[0]) + lower[1..] : lower;
    }

    // Splits "IpaStage" into "Ipa Stage" and "FFCRecord" into "FFC Record".
    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex WordBoundaryRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiSpaceRegex();
}
