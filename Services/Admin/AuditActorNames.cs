using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Admin;

/// <summary>A person found in the user directory, keyed for actor lookups.</summary>
public sealed record AuditActorDirectoryEntry(string Id, string? UserName, string? FullName)
{
    public string? DisplayName => !string.IsNullOrWhiteSpace(FullName)
        ? FullName.Trim()
        : string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim();
}

/// <summary>
/// One rule for the name shown as the actor of an audit event. Audit rows were written in
/// several ways over time: some keep only the user id, some keep the sign-in name, some keep
/// a system marker ("System", "PRISM system"). The audit log shows the person's display name
/// (full name, then user name) for every row that names a person, and "PRISM" only for rows
/// the application itself wrote.
/// </summary>
public static class AuditActorNames
{
    /// <summary>The one word shown for events PRISM wrote by itself.</summary>
    public const string SystemActor = "PRISM";

    /// <summary>Shown when the row names a user account that no longer exists.</summary>
    public const string UnknownPerson = "Removed user";

    // Markers older code wrote instead of a person. Compared case-insensitively.
    public static readonly IReadOnlyCollection<string> SystemMarkers = new[]
    {
        "System",
        "PRISM",
        "PRISM system",
        "PRISM-system"
    };

    public static bool IsSystemMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && SystemMarkers.Any(marker => string.Equals(marker, value.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves the display name for one audit row from the person directory.
    /// </summary>
    /// <param name="userId">The user id stored on the row or in its payload.</param>
    /// <param name="storedName">The name stored on the row or in its payload.</param>
    /// <param name="byId">People keyed by user id.</param>
    /// <param name="byUserName">People keyed by user name (case-insensitive).</param>
    public static string Resolve(
        string? userId,
        string? storedName,
        IReadOnlyDictionary<string, AuditActorDirectoryEntry> byId,
        IReadOnlyDictionary<string, AuditActorDirectoryEntry> byUserName)
    {
        var id = IsSystemMarker(userId) ? null : userId?.Trim();
        var name = IsSystemMarker(storedName) ? null : storedName?.Trim();

        if (!string.IsNullOrEmpty(id)
            && byId.TryGetValue(id, out var person)
            && person.DisplayName is { } fromId)
        {
            return fromId;
        }

        if (!string.IsNullOrEmpty(name))
        {
            return byUserName.TryGetValue(name, out var named) && named.DisplayName is { } fromName
                ? fromName
                : name;
        }

        return string.IsNullOrEmpty(id) ? SystemActor : UnknownPerson;
    }

    /// <summary>Loads the people named by a set of audit rows in one query.</summary>
    public static async Task<(IReadOnlyDictionary<string, AuditActorDirectoryEntry> ById,
            IReadOnlyDictionary<string, AuditActorDirectoryEntry> ByUserName)>
        LoadDirectoryAsync(
            ApplicationDbContext db,
            IEnumerable<string?> userIds,
            IEnumerable<string?> userNames,
            CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && !IsSystemMarker(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var normalizedNames = userNames
            .Where(name => !string.IsNullOrWhiteSpace(name) && !IsSystemMarker(name))
            .Select(name => name!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var byId = new Dictionary<string, AuditActorDirectoryEntry>(StringComparer.Ordinal);
        var byUserName = new Dictionary<string, AuditActorDirectoryEntry>(StringComparer.OrdinalIgnoreCase);
        if (ids.Length == 0 && normalizedNames.Length == 0)
        {
            return (byId, byUserName);
        }

        var people = await db.Users.AsNoTracking()
            .Where(user => ids.Contains(user.Id)
                || (user.NormalizedUserName != null && normalizedNames.Contains(user.NormalizedUserName)))
            .Select(user => new AuditActorDirectoryEntry(user.Id, user.UserName, user.FullName))
            .ToListAsync(cancellationToken);

        foreach (var person in people)
        {
            byId[person.Id] = person;
            if (!string.IsNullOrWhiteSpace(person.UserName))
            {
                byUserName[person.UserName.Trim()] = person;
            }
        }

        return (byId, byUserName);
    }
}
