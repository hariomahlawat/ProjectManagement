using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaLibrarySchemaStatus(
    bool IsAvailable,
    bool IsOperational,
    bool IsCurrent,
    bool MigrationHistoryConsistent,
    IReadOnlyList<string> PendingMigrations,
    string? Error,
    string DiagnosticReference)
{
    public static MediaLibrarySchemaStatus Unavailable(string error, string reference)
        => new(false, false, false, false, Array.Empty<string>(), error, reference);
}

public interface IMediaLibrarySchemaService
{
    Task<MediaLibrarySchemaStatus> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns media-catalogue schema readiness. The current state is established from both
/// EF migration closure and representative physical objects required by Photos,
/// classification, face processing and identity governance.
/// </summary>
public sealed class MediaLibrarySchemaService : IMediaLibrarySchemaService
{
    private static readonly string[] RequiredTables =
    {
        "MediaLibrarySources",
        "MediaAssets",
        "MediaProcessingJobs",
        "MediaClassificationAudits",
        "MediaClassificationRuns",
        "MediaFaces",
        "MediaFaceEmbeddings",
        "MediaPersons",
        "MediaPersonFaces",
        "MediaFaceReviewDecisions",
        "MediaIdentityAudits"
    };

    private static readonly (string Table, string Column)[] RequiredColumns =
    {
        ("MediaLibrarySources", "IsDeleted"),
        ("MediaLibrarySources", "IsVisibleInLibrary"),
        ("MediaAssets", "AvailabilityStatus"),
        ("MediaAssets", "ClassificationDecisionStatus"),
        ("MediaAssets", "ClassificationConcurrencyToken"),
        ("MediaAssets", "FaceAnalysisStatus"),
        ("MediaFaces", "ConcurrencyToken"),
        ("MediaFaces", "CandidateSearchStatus"),
        ("MediaPersonFaces", "ConcurrencyToken"),
        ("MediaPersonFaces", "ReferenceStatus"),
        ("MediaFaceReviewDecisions", "ConcurrencyToken"),
        ("MediaFaceReviewDecisions", "ConfidenceLevel")
    };

    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibrarySchemaStatusCache _cache;
    private readonly ILogger<MediaLibrarySchemaService> _logger;

    public MediaLibrarySchemaService(
        MediaLibraryDbContext db,
        MediaLibrarySchemaStatusCache cache,
        ILogger<MediaLibrarySchemaService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaLibrarySchemaStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGet(out var cached))
        {
            return cached;
        }

        var reference = CreateReference("MLS");

        try
        {
            if (!await _db.Database.CanConnectAsync(cancellationToken))
            {
                return Cache(MediaLibrarySchemaStatus.Unavailable(
                    $"The media catalogue database is not reachable. Reference {reference}.",
                    reference));
            }

            if (!_db.Database.IsRelational())
            {
                return Cache(new MediaLibrarySchemaStatus(
                    IsAvailable: true,
                    IsOperational: true,
                    IsCurrent: true,
                    MigrationHistoryConsistent: true,
                    PendingMigrations: Array.Empty<string>(),
                    Error: null,
                    DiagnosticReference: reference));
            }

            var knownMigrations = _db.Database.GetMigrations().ToArray();
            var latestRequiredMigrationId = ResolveLatestRequiredMigrationId(knownMigrations);
            var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            var physical = await VerifyPhysicalSchemaAsync(
                latestRequiredMigrationId,
                cancellationToken);
            var operational = physical.MissingObjects.Count == 0;

            if (operational)
            {
                try
                {
                    await RunRepresentativeQueriesAsync(cancellationToken);
                }
                catch (Exception exception) when (IsSchemaInspectionException(exception))
                {
                    operational = false;
                    _logger.LogWarning(
                        exception,
                        "Media catalogue representative schema query failed. Reference={Reference}",
                        reference);
                }
            }

            var historyConsistent =
                physical.HistoryTablePresent && physical.LatestMigrationRecorded;
            var current = operational && historyConsistent && pending.Length == 0;

            string? error = null;
            if (!operational)
            {
                error = physical.MissingObjects.Count == 0
                    ? $"The media catalogue schema could not execute the deployed model. Reference {reference}."
                    : $"The media catalogue schema is incomplete. Missing: {string.Join(", ", physical.MissingObjects)}. Reference {reference}.";
            }
            else if (!historyConsistent)
            {
                error =
                    $"The media catalogue is physically available, but migration {latestRequiredMigrationId} is not recorded in {MediaLibraryDbContext.MigrationsHistoryTable}. Reference {reference}.";
            }
            else if (pending.Length > 0)
            {
                error =
                    $"The media catalogue has {pending.Length} pending migration(s): {string.Join(", ", pending)}. Reference {reference}.";
            }

            if (!current)
            {
                _logger.LogWarning(
                    "Media catalogue schema is not current. Reference={Reference}; Operational={Operational}; HistoryTablePresent={HistoryTablePresent}; LatestMigrationRecorded={LatestMigrationRecorded}; Pending={Pending}; Missing={Missing}",
                    reference,
                    operational,
                    physical.HistoryTablePresent,
                    physical.LatestMigrationRecorded,
                    string.Join(',', pending),
                    string.Join(',', physical.MissingObjects));
            }

            return Cache(new MediaLibrarySchemaStatus(
                IsAvailable: true,
                IsOperational: operational,
                IsCurrent: current,
                MigrationHistoryConsistent: historyConsistent,
                PendingMigrations: pending,
                Error: error,
                DiagnosticReference: reference));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsSchemaInspectionException(exception))
        {
            _logger.LogWarning(
                exception,
                "Unable to inspect the media catalogue schema. Reference={Reference}",
                reference);
            return Cache(MediaLibrarySchemaStatus.Unavailable(
                $"The media catalogue schema could not be verified. Reference {reference}.",
                reference));
        }
    }

    private async Task RunRepresentativeQueriesAsync(CancellationToken cancellationToken)
    {
        await _db.Assets.AsNoTracking()
            .Select(asset => new
            {
                asset.Id,
                asset.AvailabilityStatus,
                asset.ClassificationDecisionStatus,
                asset.ClassificationConcurrencyToken,
                asset.FaceAnalysisStatus
            })
            .Take(1)
            .ToListAsync(cancellationToken);

        await _db.Faces.AsNoTracking()
            .Select(face => new { face.Id, face.ConcurrencyToken, face.CandidateSearchStatus })
            .Take(1)
            .ToListAsync(cancellationToken);

        await _db.PersonFaces.AsNoTracking()
            .Select(assignment => new { assignment.Id, assignment.ConcurrencyToken, assignment.ReferenceStatus })
            .Take(1)
            .ToListAsync(cancellationToken);

        await _db.FaceReviewDecisions.AsNoTracking()
            .Select(decision => new { decision.Id, decision.ConcurrencyToken, decision.ConfidenceLevel })
            .Take(1)
            .ToListAsync(cancellationToken);

        await _db.ClassificationAudits.AsNoTracking().Select(row => row.Id).Take(1).ToListAsync(cancellationToken);
        await _db.ClassificationRuns.AsNoTracking().Select(row => row.Id).Take(1).ToListAsync(cancellationToken);
        await _db.FaceEmbeddings.AsNoTracking().Select(row => row.Id).Take(1).ToListAsync(cancellationToken);
        await _db.Persons.AsNoTracking().Select(row => row.Id).Take(1).ToListAsync(cancellationToken);
        await _db.IdentityAudits.AsNoTracking().Select(row => row.Id).Take(1).ToListAsync(cancellationToken);
    }

    private async Task<PhysicalSchemaState> VerifyPhysicalSchemaAsync(
        string latestRequiredMigrationId,
        CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var existingTables = new HashSet<string>(StringComparer.Ordinal);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT table_name
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = ANY (@tables);
                    """;
                AddArrayParameter(command, "tables", RequiredTables);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingTables.Add(reader.GetString(0));
                }
            }

            var requiredTableNames = RequiredColumns
                .Select(item => item.Table)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var existingColumns = new HashSet<string>(StringComparer.Ordinal);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT table_name, column_name
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = ANY (@tables);
                    """;
                AddArrayParameter(command, "tables", requiredTableNames);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingColumns.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
                }
            }

            var historyTablePresent = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT to_regclass(format('%I.%I', current_schema(), @historyTable)) IS NOT NULL;
                    """;
                AddScalarParameter(command, "historyTable", MediaLibraryDbContext.MigrationsHistoryTable);
                historyTablePresent = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
            }

            var latestMigrationRecorded = false;
            if (historyTablePresent)
            {
                var quotedHistoryTable = QuoteIdentifier(MediaLibraryDbContext.MigrationsHistoryTable);
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT EXISTS (
                        SELECT 1
                        FROM {quotedHistoryTable}
                        WHERE "MigrationId" = @migrationId
                    );
                    """;
                AddScalarParameter(command, "migrationId", latestRequiredMigrationId);
                latestMigrationRecorded = Convert.ToBoolean(
                    await command.ExecuteScalarAsync(cancellationToken));
            }

            var missingObjects = RequiredTables
                .Where(table => !existingTables.Contains(table))
                .Select(table => $"table {table}")
                .Concat(RequiredColumns
                    .Where(item => !existingColumns.Contains($"{item.Table}.{item.Column}"))
                    .Select(item => $"column {item.Table}.{item.Column}"))
                .ToArray();

            return new PhysicalSchemaState(
                HistoryTablePresent: historyTablePresent,
                LatestMigrationRecorded: latestMigrationRecorded,
                MissingObjects: missingObjects);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static string ResolveLatestRequiredMigrationId(IEnumerable<string> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        var latest = migrations.OrderBy(id => id, StringComparer.Ordinal).LastOrDefault();
        if (string.IsNullOrWhiteSpace(latest))
        {
            throw new InvalidOperationException(
                "No MediaLibraryDbContext migrations were discovered in the deployed assembly.");
        }

        return latest;
    }

    private MediaLibrarySchemaStatus Cache(MediaLibrarySchemaStatus status)
    {
        _cache.Store(status);
        return status;
    }

    private static void AddArrayParameter(
        System.Data.Common.DbCommand command,
        string name,
        string[] values)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = values;
        command.Parameters.Add(parameter);
    }

    private static void AddScalarParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static bool IsSchemaInspectionException(Exception exception)
        => exception is NpgsqlException
            or InvalidOperationException
            or TimeoutException
            or DataException;

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string CreateReference(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    private sealed record PhysicalSchemaState(
        bool HistoryTablePresent,
        bool LatestMigrationRecorded,
        IReadOnlyList<string> MissingObjects);
}
