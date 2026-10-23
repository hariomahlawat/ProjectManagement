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

        // Array parameters keep the predicate one fixed shape whatever the number of
        // policies/roles. The search statement repeats it once per retrieval channel, so a
        // compact predicate also keeps PostgreSQL planning time down.
        Add(command, "allowedPolicies", access.AllowedPolicies.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());

        var hasUser = !string.IsNullOrWhiteSpace(access.UserId);
        if (hasUser) Add(command, "currentUserId", access.UserId);
        Add(command, "roles", access.Roles.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());

        var ownerSql = hasUser ? "e.\"OwnerUserId\" = @currentUserId" : "FALSE";
        var userPrincipalSql = hasUser ? "(p.\"PrincipalType\" = 'User' AND p.\"PrincipalValue\" = @currentUserId) OR " : string.Empty;

        return $"""
            AND (e."RequiredPolicy" IS NULL OR e."RequiredPolicy" = ANY(@allowedPolicies))
            AND (
                e."VisibilityMode" = 0
                OR (e."VisibilityMode" = 1 AND {ownerSql})
                OR (e."VisibilityMode" = 2 AND EXISTS (
                    SELECT 1 FROM "SearchEntryPrincipals" p
                    WHERE p."SearchEntryId" = e."Id"
                      AND ({userPrincipalSql}(p."PrincipalType" = 'Role' AND p."PrincipalValue" = ANY(@roles)))
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
