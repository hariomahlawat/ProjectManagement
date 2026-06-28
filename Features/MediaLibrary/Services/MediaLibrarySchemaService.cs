using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaLibrarySchemaStatus(
    bool IsAvailable,
    bool IsCurrent,
    IReadOnlyList<string> PendingMigrations,
    string? Error)
{
    public static MediaLibrarySchemaStatus Unavailable(string error)
        => new(false, false, Array.Empty<string>(), error);
}

public interface IMediaLibrarySchemaService
{
    Task<MediaLibrarySchemaStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<MediaLibrarySchemaStatus> MigrateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns optional media-catalogue schema readiness. Readiness requires more than an
/// empty pending-migration list: the required physical columns and a representative
/// EF query must also succeed.
/// </summary>
public sealed class MediaLibrarySchemaService : IMediaLibrarySchemaService
{
    private const long AdvisoryLockKey = 0x505249534D4D4544; // "PRISMMED"
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
        var reference = $"MLS-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        try
        {
            if (!await _db.Database.CanConnectAsync(cancellationToken))
            {
                return MediaLibrarySchemaStatus.Unavailable(
                    $"The PostgreSQL database is not reachable. Reference {reference}.");
            }

            var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            if (pending.Length > 0)
            {
                return new MediaLibrarySchemaStatus(
                    true,
                    false,
                    pending,
                    "The media catalogue has pending database migrations.");
            }

            var physicalState = await VerifyPhysicalSchemaAsync(cancellationToken);
            if (!physicalState.IsValid)
            {
                _logger.LogWarning(
                    "Media catalogue schema verification failed. Reference={Reference}; MissingColumns={MissingColumns}; MigrationRecorded={MigrationRecorded}",
                    reference,
                    string.Join(",", physicalState.MissingColumns),
                    physicalState.MigrationRecorded);

                return new MediaLibrarySchemaStatus(
                    true,
                    false,
                    Array.Empty<string>(),
                    $"The media catalogue schema is incomplete. Initialize the catalogue or apply the latest migration. Reference {reference}.");
            }

            // Final model-to-database smoke test. This catches snapshot/model drift even
            // when information_schema looks correct.
            await _db.Assets
                .AsNoTracking()
                .Select(asset => new { asset.Id, asset.AvailabilityStatus })
                .Take(1)
                .ToListAsync(cancellationToken);

            return new MediaLibrarySchemaStatus(true, true, Array.Empty<string>(), null);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException or DataException)
        {
            _logger.LogWarning(ex,
                "Unable to inspect the optional media catalogue schema. Reference={Reference}",
                reference);
            return MediaLibrarySchemaStatus.Unavailable(
                $"The media catalogue schema could not be verified. Reference {reference}.");
        }
    }

    public async Task<MediaLibrarySchemaStatus> MigrateAsync(CancellationToken cancellationToken)
    {
        var reference = $"MLM-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await _db.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_lock({AdvisoryLockKey});",
                    cancellationToken);

                await _db.Database.MigrateAsync(cancellationToken);
            }
            finally
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
                        "Unable to release media schema advisory lock. Reference={Reference}",
                        reference);
                }

                await _db.Database.CloseConnectionAsync();
            }

            var status = await GetStatusAsync(cancellationToken);
            if (!status.IsCurrent)
            {
                _logger.LogWarning(
                    "Media catalogue migration completed but post-migration verification failed. Reference={Reference}; Error={Error}",
                    reference,
                    status.Error);
            }

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unable to migrate the optional media catalogue schema. Reference={Reference}",
                reference);
            return MediaLibrarySchemaStatus.Unavailable(
                $"The media catalogue migration could not be completed. Reference {reference}.");
        }
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

            var migrationRecorded = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT EXISTS (
                        SELECT 1
                        FROM "__EFMigrationsHistory"
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

            return new PhysicalSchemaState(missing.Length == 0 && migrationRecorded, migrationRecorded, missing);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed record PhysicalSchemaState(
        bool IsValid,
        bool MigrationRecorded,
        IReadOnlyList<string> MissingColumns);
}
