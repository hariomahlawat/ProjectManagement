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
                "Database provider",
                AdminHealthStatus.Unavailable,
                "The configured provider is not relational."));
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
                "Connectivity",
                canConnect ? AdminHealthStatus.Healthy : AdminHealthStatus.Critical,
                canConnect ? $"Connected in {latencyMs} ms." : "The application could not connect to the database."));
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(exception, "Database health connectivity check failed.");
            checks.Add(new(
                "connectivity",
                "Connectivity",
                AdminHealthStatus.Critical,
                "The database connectivity check failed.",
                "Review application logs for the diagnostic reference."));
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
                "Migration lineage",
                unknownApplied.Count == 0 ? AdminHealthStatus.Healthy : AdminHealthStatus.Critical,
                unknownApplied.Count == 0
                    ? $"All {applied.Count} applied migration(s) are present in the deployed assembly."
                    : $"{unknownApplied.Count} applied migration(s) are missing from the deployed assembly.",
                unknownApplied.Count == 0 ? null : string.Join(", ", unknownApplied)));

            checks.Add(new(
                "pending-migrations",
                "Pending migrations",
                pending.Count == 0 ? AdminHealthStatus.Healthy : AdminHealthStatus.Warning,
                pending.Count == 0
                    ? "No pending migrations."
                    : $"{pending.Count} migration(s) are pending.",
                pending.Count == 0 ? null : string.Join(", ", pending)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Database health migration inspection failed.");
            checks.Add(new(
                "migrations",
                "Migrations",
                AdminHealthStatus.Critical,
                "Migration state could not be inspected.",
                "Review application logs for the diagnostic reference."));
        }

        string? serverVersion = null;
        long? databaseSize = null;
        try
        {
            (serverVersion, databaseSize) = await ReadServerMetadataAsync(cancellationToken);
            checks.Add(new(
                "server-metadata",
                "Server metadata",
                AdminHealthStatus.Healthy,
                string.IsNullOrWhiteSpace(serverVersion)
                    ? "Server metadata is available."
                    : $"PostgreSQL {serverVersion}."));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Database health server metadata check failed.");
            checks.Add(new(
                "server-metadata",
                "Server metadata",
                AdminHealthStatus.Unavailable,
                "Server version or database size could not be read."));
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
