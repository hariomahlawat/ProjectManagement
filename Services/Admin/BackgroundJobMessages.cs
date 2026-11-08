using System.Globalization;

namespace ProjectManagement.Services.Admin;

/// <summary>
/// The last-run lines background jobs report to System health. Written once here so every job
/// reads the same way: plain words, counts with the right noun, dates as "6 Nov"
/// (docs/ux/admin-review-3-fixes.md, PR 6).
/// </summary>
public static class BackgroundJobMessages
{
    public const string NothingToRemove = "Nothing to remove";

    /// <summary>"Removed 3 items from the notebook bin", or "Nothing to remove" when the count is 0.</summary>
    public static string Removed(int count, string one, string many, string? where = null)
    {
        if (count <= 0) return NothingToRemove;
        var noun = count == 1 ? one : many;
        return string.IsNullOrWhiteSpace(where)
            ? $"Removed {Count(count)} {noun}"
            : $"Removed {Count(count)} {noun} {where}";
    }

    /// <summary>
    /// Items a job could not remove this run, for example "1 document could not be removed and
    /// stays in the bin; the server log says why". The next run tries them again.
    /// </summary>
    public static string CouldNotRemove(int count, string one, string many, string staysOne, string staysMany)
    {
        var single = count == 1;
        return $"{Count(count)} {(single ? one : many)} could not be removed and {(single ? staysOne : staysMany)}; the server log says why";
    }

    /// <summary>Notification clean-up: old ones removed, stale unread ones marked as read.</summary>
    public static string NotificationsTidied(int removed, int markedRead)
    {
        if (removed <= 0 && markedRead <= 0) return NothingToRemove;
        var parts = new List<string>(2);
        if (removed > 0) parts.Add(Removed(removed, "old notification", "old notifications"));
        if (markedRead > 0) parts.Add($"{(parts.Count == 0 ? "Marked" : "marked")} {Count(markedRead)} old unread {(markedRead == 1 ? "notification" : "notifications")} as read");
        return string.Join("; ", parts);
    }

    public static string SignInSummaryUpToDate(DateOnly day) =>
        $"Sign-in summary for {Day(day)} is up to date";

    public static string SignInSummaryAdded(DateOnly day, int signIns) =>
        $"Sign-in summary for {Day(day)} added: {Count(signIns)} {(signIns == 1 ? "sign-in" : "sign-ins")}";

    /// <summary>A calendar day as System health writes it: "6 Nov".</summary>
    public static string Day(DateOnly day) => day.ToString("d MMM", CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
