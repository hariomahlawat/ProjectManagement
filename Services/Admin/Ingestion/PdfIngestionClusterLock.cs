using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Admin.Ingestion;

/// <summary>
/// Makes "one PDF ingestion run at a time" hold across application processes.
///
/// <see cref="PdfIngestionRunGate"/> only guards the current process. During a rolling or
/// overlapping deployment (or with more than one instance on the LAN) two administrators could
/// start runs in different processes; each run read the "already linked" list once at the start,
/// so both ingested the same attachments and created duplicate repository links.
/// </summary>
public interface IPdfIngestionClusterLock
{
    /// <summary>Returns a lease to dispose when the run ends, or null when another process holds it.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}

public sealed class PdfIngestionClusterLock : IPdfIngestionClusterLock
{
    // "PRISMPDF". Distinct from the SearchV2 rebuild and media catalogue advisory keys.
    internal const long AdvisoryLockKey = 0x5052_4953_4D50_4446;

    private readonly ApplicationDbContext _db;

    public PdfIngestionClusterLock(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsNpgsql())
        {
            // Non-PostgreSQL providers are single-process test hosts; the in-process gate suffices.
            return NoopLease.Instance;
        }

        // A dedicated, unpooled session owns the session-level advisory lock, so a crashed
        // process cannot strand it and it never lingers on a pooled connection.
        var builder = new NpgsqlConnectionStringBuilder(_db.Database.GetConnectionString())
        {
            Pooling = false
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key);";
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new AdvisoryLease(connection);
            }

            await connection.DisposeAsync();
            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class AdvisoryLease : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;

        public AdvisoryLease(NpgsqlConnection connection) => _connection = connection;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = _connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@key);";
                command.Parameters.AddWithValue("key", AdvisoryLockKey);
                await command.ExecuteScalarAsync();
            }
            catch
            {
                // Closing the unpooled session below releases the lock regardless.
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public static readonly NoopLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
