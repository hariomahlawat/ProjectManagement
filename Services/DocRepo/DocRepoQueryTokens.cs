using System.Text;
using System.Text.RegularExpressions;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Search shortcuts
// The library search box accepts filters typed with the words: type:minutes office:TEC
// tag:radar year:2025 from:DRDO. Values with spaces go in quotes (office:"Test Range"). The page
// turns them into the ordinary filters, so the address bar and the chips show what applies.
public sealed record DocRepoQueryTokens(
    string? Text,
    string? Type,
    string? Office,
    string? Tag,
    int? Year,
    string? From)
{
    private static readonly Regex Token = new(
        @"(?<!\S)(?<key>type|office|tag|year|from):(?:""(?<value>[^""]*)""|(?<value>[^\s""]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    public bool HasShortcuts => Type is not null || Office is not null || Tag is not null || Year is not null || From is not null;

    public static DocRepoQueryTokens Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new DocRepoQueryTokens(null, null, null, null, null, null);
        }

        string? type = null, office = null, tag = null, from = null;
        int? year = null;
        var text = new StringBuilder();
        var last = 0;
        foreach (Match match in Token.Matches(query))
        {
            var value = match.Groups["value"].Value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            var recognised = true;
            switch (match.Groups["key"].Value.ToLowerInvariant())
            {
                case "type": type = value; break;
                case "office": office = value; break;
                case "tag": tag = value; break;
                case "from": from = value; break;
                case "year":
                    if (int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    {
                        year = parsed;
                    }
                    else
                    {
                        recognised = false;
                    }
                    break;
            }

            if (!recognised)
            {
                continue;
            }

            text.Append(query, last, match.Index - last).Append(' ');
            last = match.Index + match.Length;
        }

        text.Append(query, last, query.Length - last);
        var remaining = Regex.Replace(text.ToString(), @"\s+", " ").Trim();
        return new DocRepoQueryTokens(remaining.Length == 0 ? null : remaining, type, office, tag, year, from);
    }
}
