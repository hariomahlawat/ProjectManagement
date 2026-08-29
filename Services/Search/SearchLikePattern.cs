namespace ProjectManagement.Services.Search;

/// <summary>
/// Builds a literal LIKE/ILIKE contains pattern. User-entered %, _ and the
/// escape character are treated as ordinary characters, not wildcard syntax.
/// </summary>
internal static class SearchLikePattern
{
    public const string EscapeCharacter = "\\";

    public static string Contains(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
