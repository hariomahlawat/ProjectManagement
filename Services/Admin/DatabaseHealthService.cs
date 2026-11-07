using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Admin;

public enum AdminHealthStatus
{
    Healthy = 0,
    Warning = 1,
    Critical = 2,
    Unavailable = 3
}

public sealed record DatabaseHealthCheck(
    string Key,
    string Label,
    AdminHealthStatus Status,
    string Summary,
    string? Detail = null);

public sealed record DatabaseHealthSnapshot(
    bool IsRelational,
    string? Provider,
    string? Host,
    string? DatabaseName,
    string? ServerVersion,
    long? DatabaseSizeBytes,
    long? QueryLatencyMilliseconds,
    string LatestMigration,
    IReadOnlyList<string> KnownMigrations,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<string> AppliedButMissingMigrations,
    IReadOnlyList<DatabaseHealthCheck> Checks)
{
    public bool HasCriticalIssues => Checks.Any(check => check.Status == AdminHealthStatus.Critical);
    public bool HasWarnings => Checks.Any(check => check.Status == AdminHealthStatus.Warning);
}

public interface IDatabaseHealthService
{
    Task<DatabaseHealthSnapshot> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class DatabaseHealthService : IDatabaseHealthService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<DatabaseHealthService> _logger;

    public DatabaseHealthService(
        ApplicationDbContext db,
        ILogger<DatabaseHealthService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DatabaseHealthSnapshot> CheckAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<DatabaseHealthCheck>();
        var isRelational = _db.Database.IsRelational();
        if (!isRelational)
        {
            checks.Add(new(
                "provider",
                "Database type",
                AdminHealthStatus.Unavailable,
                "PRISM is not connected to the expected kind of database."));
            return new(false, _db.Database.ProviderName, null, null, null, null, null,
                "(not available)", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), checks);
        }

        var provider = _db.Database.ProviderName;
        string? host = null;
        string? databaseName = null;
        ResolveConnectionMetadata(ref host, ref databaseName);

        long? latencyMs = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            stopwatch.Stop();
            latencyMs = stopwatch.ElapsedMilliseconds;
            checks.Add(new(
                "connectivity",
                "Connection",
                canConnect ? AdminHealthStatus.Healthy : AdminHealthStatus.Critical,
                canConnect ? $"Connected in {latencyMs} ms." : "PRISM could not connect to the database."));
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(exception, "Database health connectivity check failed.");
            checks.Add(new(
                "connectivity",
                "Connection",
                AdminHealthStatus.Critical,
                "The connection check failed.",
                "Look at the application log for the reference."));
        }

        IReadOnlyList<string> known = Array.Empty<string>();
        IReadOnlyList<string> applied = Array.Empty<string>();
        IReadOnlyList<string> pending = Array.Empty<string>();
        IReadOnlyList<string> unknownApplied = Array.Empty<string>();

        try
        {
            known = _db.Database.GetMigrations().ToArray();
            applied = (await _db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            var knownSet = known.ToHashSet(StringComparer.Ordinal);
            unknownApplied = applied.Where(migration => !knownSet.Contains(migration)).OrderBy(migration => migration).ToArray();
            pending = known.Where(migration => !applied.Contains(migration, StringComparer.Ordinal)).ToArray();

            checks.Add(new(
                "migration-lineage",
                "Database updates on record",
                unknownApplied.Count == 0 ? AdminHealthStatus.Healthy : AdminHealthStatus.Critical,
                unknownApplied.Count == 0
                    ? $"All {applied.Count} updates the database has had are part of this version of PRISM."
                    : $"The database has {unknownApplied.Count} update{(unknownApplied.Count == 1 ? "" : "s")} this version of PRISM does not know, so an older PRISM may be running against a newer database.",
                unknownApplied.Count == 0 ? null : string.Join(", ", unknownApplied)));

            checks.Add(new(
                "pending-migrations",
                "Database updates to run",
                pending.Count == 0 ? AdminHealthStatus.Healthy : AdminHealthStatus.Warning,
                pending.Count == 0
                    ? "None: the database is up to date."
                    : $"{pending.Count} database update{(pending.Count == 1 ? " has" : "s have")} not run yet.",
                pending.Count == 0 ? null : string.Join(", ", pending)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Database health migration inspection failed.");
            checks.Add(new(
                "migrations",
                "Database updates",
                AdminHealthStatus.Critical,
                "PRISM could not read which database updates have run.",
                "Look at the application log for the reference."));
        }

        string? serverVersion = null;
        long? databaseSize = null;
        try
        {
            (serverVersion, databaseSize) = await ReadServerMetadataAsync(cancellationToken);
            checks.Add(new(
                "server-metadata",
                "Database server",
                AdminHealthStatus.Healthy,
                string.IsNullOrWhiteSpace(serverVersion)
                    ? "The database server answered."
                    : $"PostgreSQL {serverVersion}."));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Database health server metadata check failed.");
            checks.Add(new(
                "server-metadata",
                "Database server",
                AdminHealthStatus.Unavailable,
                "The database server's version or size could not be read."));
        }

        return new DatabaseHealthSnapshot(
            true,
            provider,
            host,
            databaseName,
            serverVersion,
            databaseSize,
            latencyMs,
            applied.Count > 0 ? applied[^1] : "(none)",
            known,
            applied,
            pending,
            unknownApplied,
            checks);
    }

    private void ResolveConnectionMetadata(ref string? host, ref string? databaseName)
    {
        var connectionString = _db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            host = builder.Host;
            databaseName = builder.Database;
        }
        catch (ArgumentException)
        {
            var connection = _db.Database.GetDbConnection();
            host = connection.DataSource;
            databaseName = connection.Database;
        }
    }

    private async Task<(string? ServerVersion, long? DatabaseSize)> ReadServerMetadataAsync(
        CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            string? serverVersion;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SHOW server_version;";
                serverVersion = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            }

            long? databaseSize;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT pg_database_size(current_database());";
                var raw = await command.ExecuteScalarAsync(cancellationToken);
                databaseSize = raw is null or DBNull ? null : Convert.ToInt64(raw);
            }

            return (serverVersion, databaseSize);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }
}
