namespace ProjectManagement.Pages.IndustryPartners;

// SECTION: Organisation initials and colour tone
// Presentation only: the same organisation name always gets the same initials and one of
// eight tones (ip-tone-0 … ip-tone-7 in industry-partners.css), so rows are easy to scan.
public static class IndustryPartnerAvatar
{
    public const int ToneCount = 8;

    private static readonly string[] HonorificPrefixes = { "M/s.", "M/s", "Messrs.", "Messrs" };

    public static string Initials(string? name)
    {
        var words = StripPrefix(name)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.FirstOrDefault(char.IsLetterOrDigit))
            .Where(letter => letter != default)
            .Take(2)
            .Select(char.ToUpperInvariant)
            .ToArray();

        return words.Length == 0 ? "?" : new string(words);
    }

    public static int Tone(string? name)
    {
        // FNV-1a over the trimmed, lower-case name: stable across processes and restarts
        // (string.GetHashCode is randomised per process).
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in (name ?? string.Empty).Trim().ToLowerInvariant())
            {
                hash = (hash ^ character) * 16777619u;
            }

            return (int)(hash % ToneCount);
        }
    }

    public static string ToneClass(string? name) => $"ip-tone-{Tone(name)}";

    private static string StripPrefix(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        foreach (var prefix in HonorificPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (value.Length == prefix.Length || !char.IsLetter(value[prefix.Length])))
            {
                return value[prefix.Length..].TrimStart(' ', '.', '-', ':');
            }
        }

        return value;
    }
}
