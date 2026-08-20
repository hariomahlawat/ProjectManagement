using System.Collections.ObjectModel;

namespace ProjectManagement.Configuration;

/// <summary>
/// Canonical PRISM Identity role names. New assignments must use the canonical
/// names in <see cref="AssignableRoles"/>. Legacy aliases are retained only for
/// compatibility with existing installations and are normalised on new admin writes.
/// </summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string HoD = "HoD";
    public const string ProjectOfficer = "Project Officer";
    public const string ProjectOffice = "Project Office";
    public const string ProjectOfficeAlternate = "ProjectOffice";
    public const string Comdt = "Comdt";
    public const string Mco = "MCO";
    public const string Ta = "TA";
    public const string Ito = "ITO";

    public const string MainOfficeClerk = "Main_Office_Clerk";
    public const string MainOfficeAlternate = "Main Office";

    public const string McCellClerk = "MC_Cell_Clerk";
    public const string ItCellClerk = "IT_Cell_Clerk";

    private static readonly string[] AssignableRoleArray =
    {
        Admin,
        Comdt,
        HoD,
        ProjectOfficer,
        ProjectOffice,
        Mco,
        Ta,
        Ito,
        MainOfficeClerk,
        McCellClerk,
        ItCellClerk
    };

    private static readonly IReadOnlyDictionary<string, string> LegacyAliasMap =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProjectOfficeAlternate] = ProjectOffice,
                [MainOfficeAlternate] = MainOfficeClerk
            });

    /// <summary>
    /// The institutional role catalogue shown for new assignments in Administration.
    /// Compatibility aliases are deliberately excluded.
    /// </summary>
    public static IReadOnlyList<string> AssignableRoles { get; } =
        Array.AsReadOnly(AssignableRoleArray);

    public static bool IsAssignable(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return false;
        }

        var normalized = roleName.Trim();
        return AssignableRoleArray.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsLegacyAlias(string? roleName) =>
        !string.IsNullOrWhiteSpace(roleName)
        && LegacyAliasMap.ContainsKey(roleName.Trim());

    /// <summary>
    /// Converts a known legacy role alias to its canonical role name. Unknown role
    /// names are returned trimmed so compatibility callers do not lose information.
    /// </summary>
    public static string Canonicalize(string? roleName)
    {
        var normalized = roleName?.Trim() ?? string.Empty;
        return LegacyAliasMap.TryGetValue(normalized, out var canonical)
            ? canonical
            : normalized;
    }
}
