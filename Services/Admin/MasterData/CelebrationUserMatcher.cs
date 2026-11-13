namespace ProjectManagement.Services.Admin.MasterData;

/// <summary>
/// Suggests the account a celebration probably belongs to, from the name written on it. Only a
/// suggestion: a person confirms it before anything is linked, and nothing is suggested when
/// more than one account fits.
/// </summary>
public static class CelebrationUserMatcher
{
    private static readonly char[] Separators = { ' ', '.', ',', '-', '(', ')', '/', '\t' };

    public static string Label(string? rank, string? fullName, string? userName)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? userName ?? string.Empty : fullName.Trim();
        return string.IsNullOrWhiteSpace(rank) || name.StartsWith(rank.Trim() + " ", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{rank.Trim()} {name}";
    }

    public static CelebrationUserOption? Suggest(string? celebrationName, IEnumerable<CelebrationUserOption> users)
    {
        var written = Words(celebrationName);
        if (written.Count == 0) return null;

        CelebrationUserOption? match = null;
        foreach (var user in users)
        {
            var account = Words(user.Label);
            if (account.Count == 0) continue;

            // Every word of the shorter name appears in the longer one: "Sharma" on the calendar
            // fits "Col R Sharma" the account, and the other way round.
            var fits = written.Count <= account.Count ? written.IsSubsetOf(account) : account.IsSubsetOf(written);
            if (!fits) continue;
            if (match is not null) return null;
            match = user;
        }

        return match;
    }

    private static HashSet<string> Words(string? text) =>
        (text ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
