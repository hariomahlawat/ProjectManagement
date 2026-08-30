using System.Data.Common;
using ProjectManagement.Services.SearchV2.Security;

namespace ProjectManagement.Services.SearchV2.Query;

/// <summary>
/// Builds the Search V2 row-level authorization predicate and binds the matching
/// parameters to a command. Search queries, correction vocabulary and other
/// query-assistance paths must share this exact visibility boundary.
/// </summary>
internal static class SearchAuthorizationSql
{
    public static string Build(DbCommand command, SearchAccessContext access)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(access);

        var policyParts = new List<string> { "e.\"RequiredPolicy\" IS NULL" };
        for (var index = 0; index < access.AllowedPolicies.Count; index++)
        {
            var name = $"allowedPolicy{index}";
            Add(command, name, access.AllowedPolicies[index]);
            policyParts.Add($"e.\"RequiredPolicy\" = @{name}");
        }

        var principalParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(access.UserId))
        {
            Add(command, "currentUserId", access.UserId);
            principalParts.Add("(p.\"PrincipalType\" = 'User' AND p.\"PrincipalValue\" = @currentUserId)");
        }

        for (var index = 0; index < access.Roles.Count; index++)
        {
            var name = $"role{index}";
            Add(command, name, access.Roles[index]);
            principalParts.Add($"(p.\"PrincipalType\" = 'Role' AND p.\"PrincipalValue\" = @{name})");
        }

        var principalSql = principalParts.Count == 0 ? "FALSE" : string.Join(" OR ", principalParts);
        var ownerSql = string.IsNullOrWhiteSpace(access.UserId) ? "FALSE" : "e.\"OwnerUserId\" = @currentUserId";

        return $"""
            AND ({string.Join(" OR ", policyParts)})
            AND (
                e."VisibilityMode" = 0
                OR (e."VisibilityMode" = 1 AND {ownerSql})
                OR (e."VisibilityMode" = 2 AND EXISTS (
                    SELECT 1 FROM "SearchEntryPrincipals" p
                    WHERE p."SearchEntryId" = e."Id" AND ({principalSql})
                ))
            )
            """;
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
