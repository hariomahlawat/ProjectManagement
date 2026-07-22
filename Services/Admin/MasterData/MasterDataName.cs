using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Admin.MasterData;

public static partial class MasterDataName
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Whitespace().Replace(value.Trim(), " ");
    }

    public static string Canonical(string? value) => Normalize(value).ToUpperInvariant();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
