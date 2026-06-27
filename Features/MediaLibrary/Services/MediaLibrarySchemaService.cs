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
/// Owns media-catalogue schema readiness. The catalogue is optional: failures are
/// reported to administration but never prevent core PRISM Photos from operating.
/// </summary>
public sealed class MediaLibrarySchemaService : IMediaLibrarySchemaService
{
    private const long AdvisoryLockKey = 0x505249534D4D4544; // "PRISMMED"
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
        try
        {
            if (!await _db.Database.CanConnectAsync(cancellationToken))
            {
                return MediaLibrarySchemaStatus.Unavailable("The PostgreSQL database is not reachable.");
            }

            var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            return new MediaLibrarySchemaStatus(true, pending.Length == 0, pending, null);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Unable to inspect the optional media catalogue schema");
            return MediaLibrarySchemaStatus.Unavailable(ex.GetBaseException().Message);
        }
    }

    public async Task<MediaLibrarySchemaStatus> MigrateAsync(CancellationToken cancellationToken)
    {
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
                    _logger.LogWarning(unlockException, "Unable to release media schema advisory lock");
                }

                await _db.Database.CloseConnectionAsync();
            }

            return await GetStatusAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to migrate the optional media catalogue schema");
            return MediaLibrarySchemaStatus.Unavailable(ex.GetBaseException().Message);
        }
    }
}
