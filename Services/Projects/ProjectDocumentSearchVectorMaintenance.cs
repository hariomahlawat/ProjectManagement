using System.Data;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Performs a lightweight structural validation of the PostgreSQL project-document search
/// infrastructure. Creation, repair and historical vector backfill are migration-owned.
/// </summary>
public static class ProjectDocumentSearchVectorMaintenance
{
    public static async Task ValidateAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!db.Database.IsNpgsql())
        {
            return;
        }

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    to_regprocedure(format(
                        '%I.project_documents_build_search_vector(integer,text,text,integer,text)',
                        current_schema())) IS NOT NULL,
                    to_regprocedure(format(
                        '%I.project_documents_search_vector_trigger()',
                        current_schema())) IS NOT NULL,
                    to_regprocedure(format(
                        '%I.project_document_texts_search_vector_trigger()',
                        current_schema())) IS NOT NULL,
                    EXISTS (
                        SELECT 1
                        FROM pg_trigger trigger_row
                        JOIN pg_class relation_row ON relation_row.oid = trigger_row.tgrelid
                        JOIN pg_namespace namespace_row ON namespace_row.oid = relation_row.relnamespace
                        WHERE trigger_row.tgname = 'project_documents_search_vector_trigger'
                          AND relation_row.relname = 'ProjectDocuments'
                          AND namespace_row.nspname = current_schema()
                          AND NOT trigger_row.tgisinternal
                    ),
                    EXISTS (
                        SELECT 1
                        FROM pg_trigger trigger_row
                        JOIN pg_class relation_row ON relation_row.oid = trigger_row.tgrelid
                        JOIN pg_namespace namespace_row ON namespace_row.oid = relation_row.relnamespace
                        WHERE trigger_row.tgname = 'project_document_texts_search_vector_after'
                          AND relation_row.relname = 'ProjectDocumentTexts'
                          AND namespace_row.nspname = current_schema()
                          AND NOT trigger_row.tgisinternal
                    ),
                    EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = current_schema()
                          AND tablename = 'ProjectDocuments'
                          AND indexname IN (
                              'IX_ProjectDocuments_SearchVector',
                              'idx_project_documents_search')
                    );
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Unable to inspect the project-document search infrastructure.");
            }

            var components = new (string Name, bool Exists)[]
            {
                ("project_documents_build_search_vector", reader.GetBoolean(0)),
                ("project_documents_search_vector_trigger function", reader.GetBoolean(1)),
                ("project_document_texts_search_vector_trigger function", reader.GetBoolean(2)),
                ("project_documents_search_vector_trigger trigger", reader.GetBoolean(3)),
                ("project_document_texts_search_vector_after trigger", reader.GetBoolean(4)),
                ("IX_ProjectDocuments_SearchVector index", reader.GetBoolean(5))
            };

            var missing = components
                .Where(component => !component.Exists)
                .Select(component => component.Name)
                .ToArray();

            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "Project-document search infrastructure is incomplete after migration. Missing: " +
                    string.Join(", ", missing));
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Backward-compatible wrapper for older callers. This method intentionally validates only;
    /// it never performs recurring DDL or rewrites document vectors during normal startup.
    /// </summary>
    public static Task EnsureUpToDateAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken = default) =>
        ValidateAsync(db, cancellationToken);
}
