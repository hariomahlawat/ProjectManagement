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

    /// <summary>
    /// Holds one record's lock while its index rows are read from the source tables and
    /// written. Incremental passes in different processes run side by side, and a record is
    /// also rewritten as another record's dependent (a project's ToT rows, an IPR's project)
    /// outside its own work item; the lock makes the process that reads after a change also
    /// write after it, so an older read is never written last. Waits for the holder.
    /// </summary>
    Task<IAsyncDisposable> LockRecordAsync(string entityType, string entityKey, CancellationToken cancellationToken) =>
        Task.FromResult<IAsyncDisposable>(NoRecordLock.Instance);
}

internal sealed class NoRecordLock : IAsyncDisposable
{
    public static readonly NoRecordLock Instance = new();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class SearchIndexCoordinator : ISearchIndexCoordinator
{
    /// <summary>ASCII "PRISMSV2". Distinct from the startup migration lock.</summary>
    internal const long AdvisoryLockKey = 0x5052_4953_4D53_5632;

    /// <summary>
    /// ASCII "SV2R": the first key of a record lock (two-key form, which never overlaps the
    /// one-key locks). The second key is the hash of the record's type and key.
    /// </summary>
    internal const int RecordLockClass = 0x5356_3252;

    /// <summary>How long a record lock waits for another process before the item is retried.</summary>
    internal static readonly TimeSpan RecordLockWait = TimeSpan.FromSeconds(60);

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

    public async Task<IAsyncDisposable> LockRecordAsync(string entityType, string entityKey, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsNpgsql())
        {
            return NoRecordLock.Instance;
        }

        // A lock taken while this pass already holds one (a ToT or IPR row refreshing its
        // projects) goes on the same session. Advisory locks are re-entrant within a session,
        // so a record whose hash equals the held one's cannot wait on itself; and PostgreSQL
        // sees one session, so its deadlock detection covers the pair. It is released with
        // the outer lock.
        if (_held is { } outer)
        {
            await AcquireAsync(outer.Connection, outer.Transaction, entityType, entityKey, cancellationToken);
            return NoRecordLock.Instance;
        }

        // A transaction lock on its own pooled connection: it ends with the transaction, and a
        // connection returned to the pool is reset (rolled back), so the lock cannot linger.
        var connection = new NpgsqlConnection(_db.Database.GetConnectionString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await AcquireAsync(connection, transaction, entityType, entityKey, cancellationToken);
            var held = new RecordLock(connection, transaction, () => _held = null);
            _held = held;
            return held;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    // The lock this pass holds, if any. A coordinator is scoped to one pass, which indexes
    // one record at a time.
    private RecordLock? _held;

    private static async Task AcquireAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string entityType, string entityKey, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SET LOCAL lock_timeout = '{(int)RecordLockWait.TotalMilliseconds}ms'; "
            + "SELECT pg_advisory_xact_lock(@class, hashtext(@record));";
        command.Parameters.AddWithValue("class", RecordLockClass);
        command.Parameters.AddWithValue("record", entityType + "/" + entityKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class RecordLock : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;
        private readonly Action _released;

        public RecordLock(NpgsqlConnection connection, NpgsqlTransaction transaction, Action released)
        {
            _connection = connection;
            _transaction = transaction;
            _released = released;
        }

        public NpgsqlConnection Connection => _connection;

        public NpgsqlTransaction Transaction => _transaction;

        public async ValueTask DisposeAsync()
        {
            _released();
            try
            {
                await _transaction.RollbackAsync();
            }
            catch
            {
                // Returning the connection to the pool resets it, which ends the transaction.
            }
            finally
            {
                await _transaction.DisposeAsync();
                await _connection.DisposeAsync();
            }
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
