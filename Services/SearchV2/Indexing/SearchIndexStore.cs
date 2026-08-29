using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.SearchV2.Models;

namespace ProjectManagement.Services.SearchV2.Indexing;

public interface ISearchIndexStore
{
    Task<bool> IsReadyAsync(int indexVersion, CancellationToken cancellationToken);
    Task<SearchIndexHealth> GetHealthAsync(CancellationToken cancellationToken);
    Task ReplaceFullGenerationAsync(IReadOnlyList<SearchProjection> projections, int indexVersion, CancellationToken cancellationToken);
    Task ReplaceEntityAsync(string requestedEntityType, string requestedEntityKey, IReadOnlyList<SearchProjection> projections, int indexVersion, CancellationToken cancellationToken);
    Task EnqueueAsync(string entityType, string entityKey, CancellationToken cancellationToken);
    Task<SearchIndexWorkItem?> DequeueAsync(CancellationToken cancellationToken);
    Task CompleteAsync(long workItemId, CancellationToken cancellationToken);
    Task<int> RecoverStaleWorkItemsAsync(TimeSpan leaseTimeout, CancellationToken cancellationToken);
    Task FailAsync(long workItemId, string error, CancellationToken cancellationToken);
    Task RecordReconciliationAsync(CancellationToken cancellationToken);
}

public sealed class SearchIndexStore : ISearchIndexStore
{
    private readonly ApplicationDbContext _db;

    public SearchIndexStore(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<bool> IsReadyAsync(int indexVersion, CancellationToken cancellationToken)
    {
        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COALESCE("ActiveGeneration", 0) > 0 AND "IndexVersion" = @version
                FROM "SearchIndexState"
                WHERE "Id" = 1;
                """;
            Add(command, "version", indexVersion);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool ready && ready;
        }
        catch (Exception ex) when (IsSchemaUnavailable(ex))
        {
            return false;
        }
        finally
        {
            await SafeCloseAsync();
        }
    }

    public async Task<SearchIndexHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT
                    COALESCE(s."ActiveGeneration", 0),
                    COALESCE(s."IndexVersion", 0),
                    s."LastFullRebuildUtc",
                    s."LastReconciliationUtc",
                    s."LastError",
                    COALESCE((SELECT COUNT(*) FROM "SearchEntries" e WHERE e."Generation" = s."ActiveGeneration"), 0),
                    COALESCE((SELECT COUNT(*) FROM "SearchIndexWorkItems" w WHERE w."Status" = 0), 0),
                    COALESCE((SELECT COUNT(*) FROM "SearchIndexWorkItems" w WHERE w."Status" = 3), 0)
                FROM "SearchIndexState" s
                WHERE s."Id" = 1;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new SearchIndexHealth(false, 0, 0, 0, 0, 0, null, null, null);
            }

            var generation = reader.GetInt64(0);
            var version = reader.GetInt32(1);
            return new SearchIndexHealth(
                generation > 0,
                generation,
                version,
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }
        catch (Exception ex) when (IsSchemaUnavailable(ex))
        {
            return new SearchIndexHealth(false, 0, 0, 0, 0, 0, null, null, ex.Message);
        }
        finally
        {
            await SafeCloseAsync();
        }
    }

    public async Task ReplaceFullGenerationAsync(
        IReadOnlyList<SearchProjection> projections,
        int indexVersion,
        CancellationToken cancellationToken)
    {
        await _db.Database.OpenConnectionAsync(cancellationToken);
        var connection = _db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var generation = await NextGenerationAsync(connection, transaction, cancellationToken);
            foreach (var projection in projections)
            {
                await UpsertProjectionAsync(connection, transaction, generation, projection, cancellationToken);
            }

            await using (var activate = connection.CreateCommand())
            {
                activate.Transaction = transaction;
                activate.CommandText = """
                    INSERT INTO "SearchIndexState" ("Id", "ActiveGeneration", "IndexVersion", "LastFullRebuildUtc", "LastReconciliationUtc", "LastError")
                    VALUES (1, @generation, @version, NOW(), NOW(), NULL)
                    ON CONFLICT ("Id") DO UPDATE SET
                        "ActiveGeneration" = EXCLUDED."ActiveGeneration",
                        "IndexVersion" = EXCLUDED."IndexVersion",
                        "LastFullRebuildUtc" = EXCLUDED."LastFullRebuildUtc",
                        "LastReconciliationUtc" = EXCLUDED."LastReconciliationUtc",
                        "LastError" = NULL;
                    DELETE FROM "SearchEntries" WHERE "Generation" <> @generation;
                    """;
                Add(activate, "generation", generation);
                Add(activate, "version", indexVersion);
                await activate.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await SafeCloseAsync();
        }
    }

    public async Task ReplaceEntityAsync(
        string requestedEntityType,
        string requestedEntityKey,
        IReadOnlyList<SearchProjection> projections,
        int indexVersion,
        CancellationToken cancellationToken)
    {
        await _db.Database.OpenConnectionAsync(cancellationToken);
        var connection = _db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var generation = await ActiveGenerationAsync(connection, transaction, indexVersion, cancellationToken);
            if (generation <= 0)
            {
                throw new InvalidOperationException("Search V2 has no active index generation.");
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                var projectId = string.Equals(requestedEntityType, "Project", StringComparison.Ordinal)
                    && int.TryParse(requestedEntityKey, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedProjectId)
                        ? parsedProjectId
                        : (int?)null;

                delete.CommandText = """
                    DELETE FROM "SearchEntries"
                    WHERE "Generation" = @generation
                      AND (
                          ("EntityType" = @entityType AND "EntityKey" = @entityKey)
                          OR (@projectId IS NOT NULL AND "EntityType" = 'ProjectDocument' AND "ParentProjectId" = @projectId)
                      );
                    """;
                Add(delete, "generation", generation);
                Add(delete, "entityType", requestedEntityType);
                Add(delete, "entityKey", requestedEntityKey);
                Add(delete, "projectId", projectId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var projection in projections)
            {
                await UpsertProjectionAsync(connection, transaction, generation, projection, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await SafeCloseAsync();
        }
    }

    public async Task EnqueueAsync(string entityType, string entityKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityKey)) return;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SearchIndexWorkItems" ("EntityType", "EntityKey", "RequestedAtUtc", "Status", "RetryCount", "NextAttemptAtUtc")
            VALUES ({entityType}, {entityKey}, NOW(), 0, 0, NOW())
            ON CONFLICT ("EntityType", "EntityKey") DO UPDATE SET
                "RequestedAtUtc" = NOW(),
                "Status" = 0,
                "RetryCount" = 0,
                "LastError" = NULL,
                "NextAttemptAtUtc" = NOW();
            """, cancellationToken);
    }

    public async Task<SearchIndexWorkItem?> DequeueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                WITH next_item AS (
                    SELECT "Id"
                    FROM "SearchIndexWorkItems"
                    WHERE "Status" = 0 AND "NextAttemptAtUtc" <= NOW()
                    ORDER BY "RequestedAtUtc", "Id"
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                UPDATE "SearchIndexWorkItems" w
                SET "Status" = 1, "StartedAtUtc" = NOW()
                WHERE w."Id" = (SELECT "Id" FROM next_item)
                RETURNING w."Id", w."EntityType", w."EntityKey", w."RetryCount";
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            return new SearchIndexWorkItem(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3));
        }
        catch (Exception ex) when (IsSchemaUnavailable(ex))
        {
            return null;
        }
        finally
        {
            await SafeCloseAsync();
        }
    }

    public Task CompleteAsync(long workItemId, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "SearchIndexWorkItems"
            WHERE "Id" = {workItemId}
              AND ("StartedAtUtc" IS NULL OR "RequestedAtUtc" <= "StartedAtUtc");

            UPDATE "SearchIndexWorkItems"
            SET "Status" = 0,
                "StartedAtUtc" = NULL,
                "NextAttemptAtUtc" = NOW(),
                "LastError" = NULL
            WHERE "Id" = {workItemId}
              AND "RequestedAtUtc" > "StartedAtUtc";
            """, cancellationToken);

    public Task<int> RecoverStaleWorkItemsAsync(TimeSpan leaseTimeout, CancellationToken cancellationToken)
    {
        var seconds = Math.Clamp((int)Math.Ceiling(leaseTimeout.TotalSeconds), 30, 86400);
        return _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SearchIndexWorkItems"
            SET "Status" = 0,
                "StartedAtUtc" = NULL,
                "NextAttemptAtUtc" = NOW(),
                "LastError" = CASE
                    WHEN "LastError" IS NULL OR btrim("LastError") = '' THEN 'Recovered abandoned indexing lease.'
                    ELSE "LastError"
                END
            WHERE "Status" = 1
              AND "StartedAtUtc" IS NOT NULL
              AND "StartedAtUtc" < NOW() - ({seconds} * INTERVAL '1 second');
            """, cancellationToken);
    }

    public Task FailAsync(long workItemId, string error, CancellationToken cancellationToken)
    {
        var compact = string.IsNullOrWhiteSpace(error) ? "Search indexing failed." : error.Trim();
        if (compact.Length > 4000) compact = compact[..4000];
        return _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SearchIndexWorkItems"
            SET "RetryCount" = "RetryCount" + 1,
                "Status" = CASE WHEN "RetryCount" + 1 >= 5 THEN 3 ELSE 0 END,
                "LastError" = {compact},
                "NextAttemptAtUtc" = NOW() + (LEAST(900, 30 * POWER(2, LEAST("RetryCount", 5))) * INTERVAL '1 second')
            WHERE "Id" = {workItemId};
            """, cancellationToken);
    }

    public Task RecordReconciliationAsync(CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync("UPDATE \"SearchIndexState\" SET \"LastReconciliationUtc\" = NOW() WHERE \"Id\" = 1;", cancellationToken);

    private static async Task<long> NextGenerationAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT GREATEST(COALESCE((SELECT MAX(\"Generation\") FROM \"SearchEntries\"), 0), COALESCE((SELECT \"ActiveGeneration\" FROM \"SearchIndexState\" WHERE \"Id\" = 1), 0)) + 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long> ActiveGenerationAsync(DbConnection connection, DbTransaction transaction, int indexVersion, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(\"ActiveGeneration\", 0) FROM \"SearchIndexState\" WHERE \"Id\" = 1 AND \"IndexVersion\" = @version;";
        Add(command, "version", indexVersion);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task UpsertProjectionAsync(
        DbConnection connection,
        DbTransaction transaction,
        long generation,
        SearchProjection projection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "SearchEntries" (
                "Generation", "EntityType", "EntityKey", "CanonicalEntityType", "CanonicalEntityKey", "ParentProjectId",
                "SourceModule", "ResultCategory", "Title", "NormalizedTitle", "Subtitle", "CanonicalUrl",
                "IdentifierText", "AliasText", "StructuredText", "NarrativeText", "FuzzyText", "Status", "FileType",
                "EventDateUtc", "UpdatedAtUtc", "VisibilityMode", "RequiredPolicy", "OwnerUserId", "IndexVersion", "IndexedAtUtc", "MetadataJson")
            VALUES (
                @generation, @entityType, @entityKey, @canonicalEntityType, @canonicalEntityKey, @parentProjectId,
                @sourceModule, @resultCategory, @title, @normalizedTitle, @subtitle, @canonicalUrl,
                @identifierText, @aliasText, @structuredText, @narrativeText, @fuzzyText, @status, @fileType,
                @eventDateUtc, @updatedAtUtc, @visibilityMode, @requiredPolicy, @ownerUserId, @indexVersion, NOW(), CAST(@metadataJson AS jsonb))
            ON CONFLICT ("Generation", "EntityType", "EntityKey") DO UPDATE SET
                "CanonicalEntityType" = EXCLUDED."CanonicalEntityType",
                "CanonicalEntityKey" = EXCLUDED."CanonicalEntityKey",
                "ParentProjectId" = EXCLUDED."ParentProjectId",
                "SourceModule" = EXCLUDED."SourceModule",
                "ResultCategory" = EXCLUDED."ResultCategory",
                "Title" = EXCLUDED."Title",
                "NormalizedTitle" = EXCLUDED."NormalizedTitle",
                "Subtitle" = EXCLUDED."Subtitle",
                "CanonicalUrl" = EXCLUDED."CanonicalUrl",
                "IdentifierText" = EXCLUDED."IdentifierText",
                "AliasText" = EXCLUDED."AliasText",
                "StructuredText" = EXCLUDED."StructuredText",
                "NarrativeText" = EXCLUDED."NarrativeText",
                "FuzzyText" = EXCLUDED."FuzzyText",
                "Status" = EXCLUDED."Status",
                "FileType" = EXCLUDED."FileType",
                "EventDateUtc" = EXCLUDED."EventDateUtc",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc",
                "VisibilityMode" = EXCLUDED."VisibilityMode",
                "RequiredPolicy" = EXCLUDED."RequiredPolicy",
                "OwnerUserId" = EXCLUDED."OwnerUserId",
                "IndexVersion" = EXCLUDED."IndexVersion",
                "IndexedAtUtc" = NOW(),
                "MetadataJson" = EXCLUDED."MetadataJson"
            RETURNING "Id";
            """;

        Add(command, "generation", generation);
        Add(command, "entityType", projection.EntityType);
        Add(command, "entityKey", projection.EntityKey);
        Add(command, "canonicalEntityType", projection.CanonicalEntityType);
        Add(command, "canonicalEntityKey", projection.CanonicalEntityKey);
        Add(command, "parentProjectId", projection.ParentProjectId);
        Add(command, "sourceModule", projection.SourceModule);
        Add(command, "resultCategory", projection.ResultCategory);
        Add(command, "title", projection.Title);
        Add(command, "normalizedTitle", projection.NormalizedTitle);
        Add(command, "subtitle", projection.Subtitle);
        Add(command, "canonicalUrl", projection.CanonicalUrl);
        Add(command, "identifierText", projection.IdentifierText);
        Add(command, "aliasText", projection.AliasText);
        Add(command, "structuredText", projection.StructuredText);
        Add(command, "narrativeText", projection.NarrativeText);
        Add(command, "fuzzyText", projection.FuzzyText);
        Add(command, "status", projection.Status);
        Add(command, "fileType", projection.FileType);
        Add(command, "eventDateUtc", projection.EventDateUtc);
        Add(command, "updatedAtUtc", projection.UpdatedAtUtc);
        Add(command, "visibilityMode", (short)projection.VisibilityMode);
        Add(command, "requiredPolicy", projection.RequiredPolicy);
        Add(command, "ownerUserId", projection.OwnerUserId);
        Add(command, "indexVersion", projection.IndexVersion);
        Add(command, "metadataJson", projection.MetadataJson ?? "{}");

        var searchEntryId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);

        await using (var deleteTerms = connection.CreateCommand())
        {
            deleteTerms.Transaction = transaction;
            deleteTerms.CommandText = "DELETE FROM \"SearchEntryTerms\" WHERE \"SearchEntryId\" = @id;";
            Add(deleteTerms, "id", searchEntryId);
            await deleteTerms.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var term in projection.Terms
                     .Where(term => !string.IsNullOrWhiteSpace(term.NormalizedTerm))
                     .DistinctBy(term => $"{term.Kind}\u001f{term.NormalizedTerm}", StringComparer.OrdinalIgnoreCase))
        {
            await using var termCommand = connection.CreateCommand();
            termCommand.Transaction = transaction;
            termCommand.CommandText = """
                INSERT INTO "SearchEntryTerms" ("SearchEntryId", "Term", "NormalizedTerm", "TermType")
                VALUES (@id, @term, @normalized, @kind)
                ON CONFLICT DO NOTHING;
                """;
            Add(termCommand, "id", searchEntryId);
            Add(termCommand, "term", term.Term);
            Add(termCommand, "normalized", term.NormalizedTerm);
            Add(termCommand, "kind", term.Kind);
            await termCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deletePrincipals = connection.CreateCommand())
        {
            deletePrincipals.Transaction = transaction;
            deletePrincipals.CommandText = "DELETE FROM \"SearchEntryPrincipals\" WHERE \"SearchEntryId\" = @id;";
            Add(deletePrincipals, "id", searchEntryId);
            await deletePrincipals.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var principal in projection.Principals
                     .Where(principal => !string.IsNullOrWhiteSpace(principal.Type) && !string.IsNullOrWhiteSpace(principal.Value))
                     .DistinctBy(principal => $"{principal.Type}\u001f{principal.Value}", StringComparer.OrdinalIgnoreCase))
        {
            await using var principalCommand = connection.CreateCommand();
            principalCommand.Transaction = transaction;
            principalCommand.CommandText = """
                INSERT INTO "SearchEntryPrincipals" ("SearchEntryId", "PrincipalType", "PrincipalValue")
                VALUES (@id, @type, @value)
                ON CONFLICT DO NOTHING;
                """;
            Add(principalCommand, "id", searchEntryId);
            Add(principalCommand, "type", principal.Type);
            Add(principalCommand, "value", principal.Value);
            await principalCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private async Task SafeCloseAsync()
    {
        try { await _db.Database.CloseConnectionAsync(); }
        catch { /* Connection ownership stays with EF; closing is best effort. */ }
    }

    private static bool IsSchemaUnavailable(Exception exception)
    {
        var message = exception.ToString();
        return message.Contains("SearchIndexState", StringComparison.OrdinalIgnoreCase)
               || message.Contains("SearchEntries", StringComparison.OrdinalIgnoreCase)
               || message.Contains("42P01", StringComparison.OrdinalIgnoreCase);
    }
}
