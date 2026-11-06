using System.Security.Cryptography;
using System.Text;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Services;

/// <summary>
/// A fingerprint of the fields the admin Edit user form changes: name, rank, account
/// classification and roles. The form carries the fingerprint it was opened with; a save is
/// refused when the account no longer matches, so a second administrator's edit cannot
/// silently undo the first one's.
///
/// Identity's own ConcurrencyStamp is not used because sign-ins, lockouts and security stamp
/// changes also rotate it, which would refuse a save for changes the form never showed.
/// </summary>
public static class UserEditVersion
{
    public static string Compute(
        string? fullName,
        string? rank,
        UserAccountKind accountKind,
        IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var canonicalRoles = roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => RoleNames.Canonicalize(role.Trim()).ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal);

        var text = string.Join(
            '\u001f',
            new[] { fullName?.Trim() ?? string.Empty, rank?.Trim() ?? string.Empty, ((int)accountKind).ToString() }
                .Concat(canonicalRoles));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public static bool Matches(string? expected, string actual) =>
        string.Equals(expected?.Trim(), actual, StringComparison.OrdinalIgnoreCase);
}
