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
    Task<MediaLibrarySchemaStatus> MigrateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns media-catalogue schema readiness. Operational readiness is based on the
/// physical schema and a representative EF query. Migration-history consistency is
/// reported separately so a bookkeeping defect never disables an otherwise usable
/// catalogue.
/// </summary>
public sealed class MediaLibrarySchemaService : IMediaLibrarySchemaService
{
    private const long AdvisoryLockKey = 0x505249534D4D4544; // "PRISMMED"
    private static readonly TimeSpan AdvisoryLockTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AdvisoryLockPollInterval = TimeSpan.FromSeconds(1);
    private const int MigrationCommandTimeoutSeconds = 600;
    private const string AvailabilityMigrationId = "20260628103000_AddMediaAvailabilityState";
    private static readonly string[] RequiredAvailabilityColumns =
    {
        "AvailabilityStatus",
        "UnavailableReason",
        "UnavailableSinceUtc",
        "LastAvailabilityCheckUtc"
    };

    private readonly MediaLibraryDbContext _db;
    private readonly ILogger<MediaLibrarySchemaService> _logger;

    public MediaLibrarySchemaService(
        MediaLibraryDbContext db,
        ILogger<MediaLibrarySchemaService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaLibrarySchemaStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var reference = CreateReference("MLS");

        try
        {
            if (!await _db.Database.CanConnectAsync(cancellationToken))
            {
                return MediaLibrarySchemaStatus.Unavailable(
                    $"The media catalogue database is not reachable. Reference {reference}.", reference);
            }

            var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            var physical = await VerifyPhysicalSchemaAsync(cancellationToken);

            var operational = physical.RequiredColumnsPresent;
            if (operational)
            {
                try
                {
                    await _db.Assets
                        .AsNoTracking()
                        .Select(asset => new { asset.Id, asset.AvailabilityStatus })
                        .Take(1)
                        .ToListAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException or DataException)
                {
                    operational = false;
                    _logger.LogWarning(ex,
                        "Media catalogue representative schema query failed. Reference={Reference}",
                        reference);
                }
            }

            var historyConsistent = physical.HistoryTablePresent && physical.AvailabilityMigrationRecorded;
            var current = operational && historyConsistent && pending.Length == 0;

            string? error = null;
            if (!operational)
            {
                error = $"The media catalogue schema is incomplete. Apply the latest migration. Reference {reference}.";
            }
            else if (!historyConsistent)
            {
                error = $"The media catalogue is operational, but its migration history requires repair. Reference {reference}.";
            }
            else if (pending.Length > 0)
            {
                error = $"The media catalogue is operational, but {pending.Length} migration(s) remain pending.";
            }

            if (!current)
            {
                _logger.LogWarning(
                    "Media catalogue schema is not fully current. Reference={Reference}; Operational={Operational}; HistoryTablePresent={HistoryTablePresent}; MigrationRecorded={MigrationRecorded}; Pending={Pending}; MissingColumns={MissingColumns}",
                    reference,
                    operational,
                    physical.HistoryTablePresent,
                    physical.AvailabilityMigrationRecorded,
                    string.Join(',', pending),
                    string.Join(',', physical.MissingColumns));
            }

            return new MediaLibrarySchemaStatus(
                true,
                operational,
                current,
                historyConsistent,
                pending,
                error,
                reference);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException or DataException)
        {
            _logger.LogWarning(ex,
                "Unable to inspect the media catalogue schema. Reference={Reference}",
                reference);
            return MediaLibrarySchemaStatus.Unavailable(
                $"The media catalogue schema could not be verified. Reference {reference}.", reference);
        }
    }

    public async Task<MediaLibrarySchemaStatus> MigrateAsync(CancellationToken cancellationToken)
    {
        var reference = CreateReference("MLM");
        var lockAcquired = false;
        var originalCommandTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(MigrationCommandTimeoutSeconds);

        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                lockAcquired = await AcquireMigrationLockAsync(reference, cancellationToken);

                var pendingBefore = (await _db.Database
                    .GetPendingMigrationsAsync(cancellationToken))
                    .ToArray();

                if (pendingBefore.Length > 0)
                {
                    _logger.LogWarning(
                        "Applying {Count} pending media-library migration(s). Reference={Reference}; Migrations={Migrations}",
                        pendingBefore.Length,
                        reference,
                        string.Join(", ", pendingBefore));

                    await _db.Database.MigrateAsync(cancellationToken);
                }
                else
                {
                    _logger.LogInformation(
                        "Media-library schema has no pending migrations. Reference={Reference}",
                        reference);
                }

                var pendingAfter = (await _db.Database
                    .GetPendingMigrationsAsync(cancellationToken))
                    .ToArray();

                if (pendingAfter.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"Media-library migration closure failed. Pending migrations: {string.Join(", ", pendingAfter)}");
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    try
                    {
                        await _db.Database.ExecuteSqlRawAsync(
                            $"SELECT pg_advisory_unlock({AdvisoryLockKey});",
                            CancellationToken.None);
                    }
                    catch (Exception unlockException)
                    {
                        _logger.LogWarning(unlockException,
                            "Unable to explicitly release media schema advisory lock. Reference={Reference}",
                            reference);
                    }
                }

                await _db.Database.CloseConnectionAsync();
            }

            var status = await GetStatusAsync(cancellationToken);
            if (!status.IsOperational)
            {
                _logger.LogWarning(
                    "Media catalogue migration completed but operational verification failed. Reference={Reference}; StatusReference={StatusReference}; Error={Error}",
                    reference,
                    status.DiagnosticReference,
                    status.Error);
            }
            else if (!status.IsCurrent)
            {
                _logger.LogWarning(
                    "Media catalogue is operational after migration but metadata remains inconsistent. Reference={Reference}; StatusReference={StatusReference}; Error={Error}",
                    reference,
                    status.DiagnosticReference,
                    status.Error);
            }
            else
            {
                _logger.LogInformation(
                    "Media-library migrations and schema validation completed successfully. Reference={Reference}",
                    reference);
            }

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unable to migrate the media catalogue schema. Reference={Reference}",
                reference);
            return MediaLibrarySchemaStatus.Unavailable(
                $"The media catalogue migration could not be completed. Reference {reference}.", reference);
        }
        finally
        {
            _db.Database.SetCommandTimeout(originalCommandTimeout);
        }
    }

    private async Task<bool> AcquireMigrationLockAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var deadline = DateTimeOffset.UtcNow.Add(AdvisoryLockTimeout);
        var waitLogged = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@lockKey);";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "lockKey";
            parameter.Value = AdvisoryLockKey;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is bool acquired && acquired)
            {
                _logger.LogInformation(
                    "Acquired media-library migration advisory lock. Reference={Reference}",
                    reference);
                return true;
            }

            if (!waitLogged)
            {
                _logger.LogInformation(
                    "Waiting for another application instance to finish media-library migrations. Reference={Reference}",
                    reference);
                waitLogged = true;
            }

            await Task.Delay(AdvisoryLockPollInterval, cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out after {AdvisoryLockTimeout.TotalMinutes:0} minutes waiting for the media-library migration lock.");
    }

    private async Task<PhysicalSchemaState> VerifyPhysicalSchemaAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var existingColumns = new HashSet<string>(StringComparer.Ordinal);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT column_name
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'MediaAssets'
                      AND column_name = ANY (@columns);
                    """;

                var parameter = command.CreateParameter();
                parameter.ParameterName = "columns";
                parameter.Value = RequiredAvailabilityColumns;
                command.Parameters.Add(parameter);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingColumns.Add(reader.GetString(0));
                }
            }

            var historyTablePresent = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT to_regclass(format('%I.%I', current_schema(), @historyTable)) IS NOT NULL;
                    """;
                var parameter = command.CreateParameter();
                parameter.ParameterName = "historyTable";
                parameter.Value = MediaLibraryDbContext.MigrationsHistoryTable;
                command.Parameters.Add(parameter);
                historyTablePresent = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
            }

            var migrationRecorded = false;
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
                var parameter = command.CreateParameter();
                parameter.ParameterName = "migrationId";
                parameter.Value = AvailabilityMigrationId;
                command.Parameters.Add(parameter);
                migrationRecorded = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
            }

            var missing = RequiredAvailabilityColumns
                .Where(column => !existingColumns.Contains(column))
                .ToArray();

            return new PhysicalSchemaState(
                missing.Length == 0,
                historyTablePresent,
                migrationRecorded,
                missing);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string CreateReference(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    private sealed record PhysicalSchemaState(
        bool RequiredColumnsPresent,
        bool HistoryTablePresent,
        bool AvailabilityMigrationRecorded,
        IReadOnlyList<string> MissingColumns);
}
