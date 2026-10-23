using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;

namespace ProjectManagement.Services.SearchV2.Indexing;

/// <summary>
/// Cross-process coordination for the Search V2 indexing worker. Every PRISM process
/// runs the worker, and rolling deployments overlap old and new processes, so full
/// rebuilds and incremental passes are coordinated through one PostgreSQL advisory lock:
/// a full rebuild holds it exclusively and incremental passes hold it shared. An
/// incremental pass therefore never dequeues work while a rebuild is reading its
/// snapshot (which would let the rebuild activate a generation that misses that work),
/// and only one process rebuilds at a time.
/// </summary>
public interface ISearchIndexCoordinator
{
    /// <summary>Returns a held lease, or null when another process holds the lock.</summary>
    Task<IAsyncDisposable?> TryAcquireFullRebuildAsync(CancellationToken cancellationToken);

    /// <summary>Returns a held lease, or null while a full rebuild holds the lock.</summary>
    Task<IAsyncDisposable?> TryAcquireIncrementalAsync(CancellationToken cancellationToken);
}

public sealed class SearchIndexCoordinator : ISearchIndexCoordinator
{
    /// <summary>ASCII "PRISMSV2". Distinct from the startup migration lock.</summary>
    internal const long AdvisoryLockKey = 0x5052_4953_4D53_5632;

    private readonly ApplicationDbContext _db;

    public SearchIndexCoordinator(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<IAsyncDisposable?> TryAcquireFullRebuildAsync(CancellationToken cancellationToken) =>
        TryAcquireAsync(shared: false, cancellationToken);

    public Task<IAsyncDisposable?> TryAcquireIncrementalAsync(CancellationToken cancellationToken) =>
        TryAcquireAsync(shared: true, cancellationToken);

    private async Task<IAsyncDisposable?> TryAcquireAsync(bool shared, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsNpgsql())
        {
            // Non-PostgreSQL providers are single-process test hosts.
            return NoopLease.Instance;
        }

        // The lease owns a dedicated, unpooled session. Session-level advisory locks are
        // released when the session ends, so a crashed process cannot strand the lock,
        // and a lock can never linger on a connection returned to the pool.
        var builder = new NpgsqlConnectionStringBuilder(_db.Database.GetConnectionString())
        {
            Pooling = false
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = shared
                ? "SELECT pg_try_advisory_lock_shared(@key);"
                : "SELECT pg_try_advisory_lock(@key);";
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new AdvisoryLease(connection, shared);
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
        private readonly bool _shared;

        public AdvisoryLease(NpgsqlConnection connection, bool shared)
        {
            _connection = connection;
            _shared = shared;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = _connection.CreateCommand();
                command.CommandText = _shared
                    ? "SELECT pg_advisory_unlock_shared(@key);"
                    : "SELECT pg_advisory_unlock(@key);";
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
