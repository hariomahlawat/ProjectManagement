using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectManagement.Data;

namespace ProjectManagement.Infrastructure;

/// <summary>
/// Applies every pending EF Core migration before the web application starts serving requests.
/// PostgreSQL deployments are serialised with a session-level advisory lock so overlapping IIS
/// worker starts cannot migrate or validate the same database concurrently.
/// </summary>
public static class DatabaseStartupMigrator
{
    private const string AdvisoryLockName = "PRISM_ERP_EF_MIGRATIONS";
    private static readonly TimeSpan AdvisoryLockTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AdvisoryLockPollInterval = TimeSpan.FromSeconds(1);
    private const int MigrationCommandTimeoutSeconds = 600;

    public static async Task<DatabaseStartupMigrationResult> ApplyAndValidateAsync(
        ApplicationDbContext db,
        ILogger logger,
        Func<CancellationToken, Task>? validateSchemaAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        if (!db.Database.IsRelational())
        {
            logger.LogInformation(
                "Database provider {Provider} is non-relational; EF Core migration startup gate was skipped.",
                db.Database.ProviderName);

            return DatabaseStartupMigrationResult.NotApplicable;
        }

        NpgsqlConnection? migrationLockConnection = null;
        var advisoryLockAcquired = false;
        var originalCommandTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(MigrationCommandTimeoutSeconds);

        try
        {
            if (db.Database.IsNpgsql())
            {
                migrationLockConnection = await OpenMigrationLockConnectionAsync(db, cancellationToken);
                advisoryLockAcquired = await AcquireAdvisoryLockAsync(
                    migrationLockConnection,
                    logger,
                    cancellationToken);
            }

            var knownMigrations = db.Database.GetMigrations().ToList();
            var appliedBefore = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var pendingBefore = knownMigrations
                .Where(migration => !appliedBefore.Contains(migration))
                .ToList();

            if (pendingBefore.Count == 0)
            {
                logger.LogInformation(
                    "Database schema is current; no pending EF Core migrations were found.");
            }
            else
            {
                logger.LogWarning(
                    "Applying {Count} pending EF Core migration(s) before application startup: {Migrations}",
                    pendingBefore.Count,
                    string.Join(", ", pendingBefore));

                try
                {
                    await db.Database.MigrateAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogCritical(
                        exception,
                        "Automatic database migration failed. Application startup is being aborted before requests are accepted.");
                    throw;
                }

                logger.LogInformation(
                    "Automatic database migration completed successfully.");
            }

            await VerifyMigrationClosureAsync(db, knownMigrations, logger, cancellationToken);

            if (validateSchemaAsync is not null)
            {
                await validateSchemaAsync(cancellationToken);
            }

            // Re-read migration state after structural validation. This detects an accidental
            // mismatch between the migration assembly and __EFMigrationsHistory before startup.
            var appliedAfter = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            var remaining = knownMigrations
                .Except(appliedAfter, StringComparer.Ordinal)
                .ToList();

            if (remaining.Count > 0)
            {
                var message =
                    $"Database still has {remaining.Count} unapplied EF Core migration(s) after startup migration: " +
                    string.Join(", ", remaining);
                logger.LogCritical(message);
                throw new InvalidOperationException(message);
            }

            var latestApplied = appliedAfter.Count == 0 ? null : appliedAfter[^1];
            logger.LogInformation(
                "Database startup gate completed. Latest applied migration: {LatestMigration}.",
                latestApplied ?? "(none)");

            return new DatabaseStartupMigrationResult(
                IsRelational: true,
                PendingBefore: pendingBefore,
                AppliedAfter: appliedAfter,
                LatestAppliedMigration: latestApplied);
        }
        finally
        {
            if (migrationLockConnection is not null)
            {
                if (advisoryLockAcquired && migrationLockConnection.State == ConnectionState.Open)
                {
                    try
                    {
                        await using var unlockCommand = migrationLockConnection.CreateCommand();
                        unlockCommand.CommandText =
                            "SELECT pg_advisory_unlock(hashtextextended(@lock_name, 0));";
                        unlockCommand.Parameters.AddWithValue("lock_name", AdvisoryLockName);
                        await unlockCommand.ExecuteScalarAsync(CancellationToken.None);
                        logger.LogInformation("Released the PostgreSQL migration advisory lock.");
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            exception,
                            "Unable to explicitly release the PostgreSQL migration advisory lock. Disposing the lock session will release or reset it.");
                    }
                }

                await migrationLockConnection.DisposeAsync();
            }

            db.Database.SetCommandTimeout(originalCommandTimeout);
        }
    }

    private static async Task<NpgsqlConnection> OpenMigrationLockConnectionAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A PostgreSQL connection string is required to acquire the startup migration lock.");
        }

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> AcquireAdvisoryLockAsync(
        NpgsqlConnection connection,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(AdvisoryLockTimeout);
        var waitLogged = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT pg_try_advisory_lock(hashtextextended(@lock_name, 0));";
            command.Parameters.AddWithValue("lock_name", AdvisoryLockName);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is bool lockAcquired && lockAcquired)
            {
                logger.LogInformation(
                    "Acquired the PostgreSQL migration advisory lock.");
                return true;
            }

            if (!waitLogged)
            {
                logger.LogInformation(
                    "Waiting for another application instance to finish database migrations.");
                waitLogged = true;
            }

            await Task.Delay(AdvisoryLockPollInterval, cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out after {AdvisoryLockTimeout.TotalMinutes:0} minutes waiting for another application instance to finish database migration.");
    }

    private static async Task VerifyMigrationClosureAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<string> knownMigrations,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var missing = knownMigrations
            .Where(migration => !applied.Contains(migration))
            .ToList();

        if (missing.Count == 0)
        {
            logger.LogInformation(
                "All {Count} migrations in the deployed assembly are recorded as applied.",
                knownMigrations.Count);
            return;
        }

        var message =
            $"Migration closure verification failed. {missing.Count} migration(s) from the deployed assembly are not applied: " +
            string.Join(", ", missing);
        logger.LogCritical(message);
        throw new InvalidOperationException(message);
    }
}

public sealed record DatabaseStartupMigrationResult(
    bool IsRelational,
    IReadOnlyList<string> PendingBefore,
    IReadOnlyList<string> AppliedAfter,
    string? LatestAppliedMigration)
{
    public static DatabaseStartupMigrationResult NotApplicable { get; } =
        new(false, Array.Empty<string>(), Array.Empty<string>(), null);
}
