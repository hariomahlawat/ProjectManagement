using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;

namespace ProjectManagement.Infrastructure;

/// <summary>
/// Verifies production-critical physical schema invariants that migration history alone
/// cannot prove. This detects manual drift or legacy startup SQL that changed the database
/// after a migration had already been recorded.
/// </summary>
public static class ApplicationDatabaseSchemaValidator
{
    public const string ProjectStageCompletionConstraint = "CK_ProjectStages_CompletedHasDate";

    public static async Task ValidateAsync(
        ApplicationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        if (!db.Database.IsRelational())
        {
            return;
        }

        if (!db.Database.IsNpgsql())
        {
            logger.LogInformation(
                "Physical project-stage schema validation is migration-owned for provider {Provider}.",
                db.Database.ProviderName);
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
            var columnState = await ReadProjectStageColumnStateAsync(connection, cancellationToken);
            var missingColumns = new[] { "ActualStart", "CompletedOn", "RequiresBackfill" }
                .Where(column => !columnState.ContainsKey(column))
                .ToArray();

            if (missingColumns.Length > 0)
            {
                throw new InvalidOperationException(
                    "ProjectStages schema is incomplete after migration. Missing columns: " +
                    string.Join(", ", missingColumns));
            }

            var nonNullableDateColumns = new[] { "ActualStart", "CompletedOn" }
                .Where(column => !string.Equals(columnState[column], "YES", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (nonNullableDateColumns.Length > 0)
            {
                throw new InvalidOperationException(
                    "ProjectStages date columns must be nullable after migration. Non-nullable columns: " +
                    string.Join(", ", nonNullableDateColumns));
            }

            if (!string.Equals(
                    columnState["RequiresBackfill"],
                    "NO",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "ProjectStages.RequiresBackfill must be non-nullable after migration.");
            }

            var definition = await ReadConstraintDefinitionAsync(connection, cancellationToken);
            if (string.IsNullOrWhiteSpace(definition))
            {
                throw new InvalidOperationException(
                    $"Required database constraint {ProjectStageCompletionConstraint} is missing.");
            }

            var normalized = NormalizeConstraintDefinition(definition);
            var requiresCompletedOn = normalized.Contains(
                "\"CompletedOn\"ISNOTNULL",
                StringComparison.OrdinalIgnoreCase);
            var permitsBackfill = normalized.Contains(
                "\"RequiresBackfill\"ISTRUE",
                StringComparison.OrdinalIgnoreCase)
                || normalized.Contains(
                    "\"RequiresBackfill\"=TRUE",
                    StringComparison.OrdinalIgnoreCase);
            var incorrectlyRequiresActualStart = normalized.Contains(
                "\"ActualStart\"ISNOTNULL",
                StringComparison.OrdinalIgnoreCase);

            if (!requiresCompletedOn || !permitsBackfill || incorrectlyRequiresActualStart)
            {
                throw new InvalidOperationException(
                    $"Database constraint {ProjectStageCompletionConstraint} is incompatible after migration. " +
                    "Completed stages must require CompletedOn or RequiresBackfill, while ActualStart remains optional. " +
                    $"Current definition: {definition}");
            }

            logger.LogInformation(
                "Validated {ConstraintName}: completed stages require CompletedOn unless backfill is pending; ActualStart is optional.",
                ProjectStageCompletionConstraint);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<Dictionary<string, string>> ReadProjectStageColumnStateAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name, is_nullable
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = 'ProjectStages'
              AND column_name = ANY (@columns);
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "columns";
        parameter.Value = new[] { "ActualStart", "CompletedOn", "RequiresBackfill" };
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            state[reader.GetString(0)] = reader.GetString(1);
        }

        return state;
    }

    private static async Task<string?> ReadConstraintDefinitionAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pg_get_constraintdef(constraint_row.oid)
            FROM pg_constraint AS constraint_row
            WHERE constraint_row.conrelid = to_regclass(format('%I.%I', current_schema(), 'ProjectStages'))
              AND constraint_row.conname = @constraintName;
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "constraintName";
        parameter.Value = ProjectStageCompletionConstraint;
        command.Parameters.Add(parameter);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static string NormalizeConstraintDefinition(string definition)
        => string.Concat(definition.Where(character => !char.IsWhiteSpace(character)))
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal);
}
