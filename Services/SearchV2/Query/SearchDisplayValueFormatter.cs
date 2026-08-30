using System.Text.RegularExpressions;

namespace ProjectManagement.Services.SearchV2.Query;

/// <summary>
/// Converts machine-oriented Search V2 display tokens to readable UI labels without
/// changing the indexed/filter value used by PostgreSQL.
/// </summary>
public static partial class SearchDisplayValueFormatter
{
    public static string? Status(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        value = value.Trim();
        if (value.Contains(' ')) return value;
        if (value.Length <= 5 && value.All(character => !char.IsLetter(character) || char.IsUpper(character))) return value;

        var spaced = AcronymBoundaryRegex().Replace(value, "$1 $2");
        spaced = CamelBoundaryRegex().Replace(spaced, "$1 $2");
        return string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static string? Subtitle(string? subtitle, string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(subtitle)) return subtitle;
        if (!string.IsNullOrWhiteSpace(rawStatus)
            && string.Equals(subtitle.Trim(), rawStatus.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Status(rawStatus);
        }

        return subtitle;
    }

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundaryRegex();

    [GeneratedRegex("([A-Z])([A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex AcronymBoundaryRegex();
}
