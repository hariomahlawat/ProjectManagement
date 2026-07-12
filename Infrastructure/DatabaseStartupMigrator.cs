using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ProjectManagement.Infrastructure;

/// <summary>
/// Applies and verifies EF Core migration sets before the web host starts accepting
/// requests. PostgreSQL deployments are serialized with one session-level advisory lock
/// held across the complete application deployment boundary, so overlapping IIS workers
/// cannot interleave ApplicationDbContext and MediaLibraryDbContext upgrades.
/// </summary>
public static class DatabaseStartupMigrator
{
    private const string AdvisoryLockName = "PRISM_ERP_EF_MIGRATIONS";
    private static readonly TimeSpan AdvisoryLockTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AdvisoryLockPollInterval = TimeSpan.FromSeconds(1);
    private const int MigrationCommandTimeoutSeconds = 600;

    /// <summary>
    /// Applies one migration set. This overload is retained for focused tests and tools;
    /// production startup should prefer <see cref="ApplyDeploymentBoundaryAsync"/> so the
    /// advisory lock remains held across every context participating in the deployment.
    /// </summary>
    public static async Task<DatabaseStartupMigrationResult> ApplyAndValidateAsync(
        DbContext db,
        ILogger logger,
        string migrationSetName,
        bool applyMigrations = true,
        Func<CancellationToken, Task>? validateSchemaAsync = null,
        CancellationToken cancellationToken = default)
    {
        var results = await ApplyDeploymentBoundaryAsync(
            db,
            logger,
            new[]
            {
                new DatabaseStartupMigrationPlan(
                    db,
                    migrationSetName,
                    applyMigrations,
                    validateSchemaAsync)
            },
            cancellationToken);

        return results[0];
    }

    /// <summary>
    /// Applies all supplied migration sets as one deployment boundary. For PostgreSQL the
    /// first context supplies a database-scoped advisory lock, and every PostgreSQL context
    /// is required to target the same server/database before any migration is attempted.
    /// </summary>
    public static async Task<IReadOnlyList<DatabaseStartupMigrationResult>> ApplyDeploymentBoundaryAsync(
        DbContext lockDb,
        ILogger logger,
        IReadOnlyList<DatabaseStartupMigrationPlan> plans,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockDb);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(plans);

        if (plans.Count == 0)
        {
            throw new ArgumentException("At least one migration plan is required.", nameof(plans));
        }

        foreach (var plan in plans)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(plan.DbContext);
            ArgumentException.ThrowIfNullOrWhiteSpace(plan.MigrationSetName);
        }

        ValidatePostgresDeploymentBoundary(lockDb, plans);

        NpgsqlConnection? migrationLockConnection = null;
        var advisoryLockAcquired = false;

        try
        {
            if (lockDb.Database.IsRelational() && lockDb.Database.IsNpgsql())
            {
                migrationLockConnection = await OpenMigrationLockConnectionAsync(lockDb, cancellationToken);
                advisoryLockAcquired = await AcquireAdvisoryLockAsync(
                    migrationLockConnection,
                    logger,
                    migrationSetName: "complete PRISM deployment",
                    cancellationToken);
            }

            // Validate every migration lineage before changing either context. This prevents
            // a compatible ApplicationDbContext from being upgraded when the Media Library
            // history belongs to a different or incomplete build (and vice versa).
            var preflightStates = new List<MigrationPreflightState>(plans.Count);
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                preflightStates.Add(await PreflightAsync(plan, logger, cancellationToken));
            }

            var results = new List<DatabaseStartupMigrationResult>(plans.Count);
            for (var index = 0; index < plans.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await ApplyAndValidateCoreAsync(
                    plans[index],
                    preflightStates[index],
                    logger,
                    cancellationToken));
            }

            logger.LogInformation(
                "Database deployment boundary completed successfully for {MigrationSets}.",
                string.Join(", ", plans.Select(plan => plan.MigrationSetName)));

            return results;
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
                        logger.LogInformation(
                            "Released the PostgreSQL migration advisory lock after the complete PRISM deployment boundary.");
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            exception,
                            "Unable to explicitly release the PostgreSQL deployment migration lock. Disposing the session will release it.");
                    }
                }

                await migrationLockConnection.DisposeAsync();
            }
        }
    }

    private static async Task<MigrationPreflightState> PreflightAsync(
        DatabaseStartupMigrationPlan plan,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var db = plan.DbContext;
        var migrationSetName = plan.MigrationSetName;

        if (!db.Database.IsRelational())
        {
            logger.LogInformation(
                "Database migration set {MigrationSet} uses non-relational provider {Provider}; startup migration was skipped.",
                migrationSetName,
                db.Database.ProviderName);

            return MigrationPreflightState.NotApplicable;
        }

        var originalCommandTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(MigrationCommandTimeoutSeconds);

        try
        {
            var knownMigrations = db.Database.GetMigrations().ToList();
            if (knownMigrations.Count == 0)
            {
                var message =
                    $"No EF Core migrations were discovered for relational migration set {migrationSetName}. " +
                    "The published application is incomplete or the migrations assembly is misconfigured.";
                logger.LogCritical(message);
                throw new InvalidOperationException(message);
            }

            var duplicateKnownMigrations = knownMigrations
                .GroupBy(migration => migration, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateKnownMigrations.Length > 0)
            {
                var message =
                    $"Migration set {migrationSetName} contains duplicate migration identifiers: " +
                    string.Join(", ", duplicateKnownMigrations);
                logger.LogCritical(message);
                throw new InvalidOperationException(message);
            }

            if (plan.ExpectedMigrationIds is not null
                && !knownMigrations.SequenceEqual(plan.ExpectedMigrationIds, StringComparer.Ordinal))
            {
                var knownSet = knownMigrations.ToHashSet(StringComparer.Ordinal);
                var expectedSet = plan.ExpectedMigrationIds.ToHashSet(StringComparer.Ordinal);
                var missingFromAssembly = plan.ExpectedMigrationIds
                    .Where(id => !knownSet.Contains(id))
                    .ToArray();
                var missingFromManifest = knownMigrations
                    .Where(id => !expectedSet.Contains(id))
                    .ToArray();

                var message =
                    $"Migration assembly and immutable manifest disagree for {migrationSetName}. " +
                    $"Missing from assembly: {FormatList(missingFromAssembly)}. " +
                    $"Missing from manifest: {FormatList(missingFromManifest)}. " +
                    "Publish a complete build generated from one committed source revision.";
                logger.LogCritical(message);
                throw new InvalidOperationException(message);
            }

            var appliedBefore = (await db.Database.GetAppliedMigrationsAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            EnsureNoUnknownAppliedMigrations(
                knownMigrations,
                appliedBefore,
                migrationSetName,
                logger);

            var pendingBefore = knownMigrations
                .Where(migration => !appliedBefore.Contains(migration))
                .ToList();

            logger.LogInformation(
                "Migration preflight passed for {MigrationSet}. Known={KnownCount}; Applied={AppliedCount}; Pending={PendingCount}.",
                migrationSetName,
                knownMigrations.Count,
                appliedBefore.Count,
                pendingBefore.Count);

            return new MigrationPreflightState(
                IsRelational: true,
                KnownMigrations: knownMigrations,
                PendingBefore: pendingBefore);
        }
        finally
        {
            db.Database.SetCommandTimeout(originalCommandTimeout);
        }
    }

    private static async Task<DatabaseStartupMigrationResult> ApplyAndValidateCoreAsync(
        DatabaseStartupMigrationPlan plan,
        MigrationPreflightState preflight,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var db = plan.DbContext;
        var migrationSetName = plan.MigrationSetName;

        if (!preflight.IsRelational)
        {
            return DatabaseStartupMigrationResult.NotApplicable(migrationSetName);
        }

        var knownMigrations = preflight.KnownMigrations;
        var pendingBefore = preflight.PendingBefore;
        var originalCommandTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(MigrationCommandTimeoutSeconds);

        try
        {
            if (pendingBefore.Count == 0)
            {
                logger.LogInformation(
                    "Migration set {MigrationSet} is current; no pending EF Core migrations were found.",
                    migrationSetName);
            }
            else
            {
                if (!plan.ApplyMigrations)
                {
                    var disabledMessage =
                        $"Migration set {migrationSetName} has {pendingBefore.Count} pending migration(s), " +
                        "but automatic startup migration is disabled: " + string.Join(", ", pendingBefore);
                    logger.LogCritical(disabledMessage);
                    throw new InvalidOperationException(disabledMessage);
                }

                logger.LogWarning(
                    "Applying {Count} pending EF Core migration(s) for {MigrationSet} before application startup: {Migrations}",
                    pendingBefore.Count,
                    migrationSetName,
                    string.Join(", ", pendingBefore));

                try
                {
                    await db.Database.MigrateAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogCritical(
                        exception,
                        "Automatic migration failed for {MigrationSet}. Application startup is being aborted before requests are accepted.",
                        migrationSetName);
                    throw;
                }

                logger.LogInformation(
                    "Automatic migration completed successfully for {MigrationSet}.",
                    migrationSetName);
            }

            var appliedAfterMigration = await VerifyMigrationClosureAsync(
                db,
                knownMigrations,
                logger,
                migrationSetName,
                cancellationToken);

            if (plan.ValidateSchemaAsync is not null)
            {
                try
                {
                    await plan.ValidateSchemaAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogCritical(
                        exception,
                        "Physical schema validation failed for {MigrationSet}. Application startup is being aborted before requests are accepted.",
                        migrationSetName);
                    throw;
                }
            }

            // Re-read state after structural validation. This catches accidental history
            // mutation or a migration assembly mismatch before workers and requests start.
            var appliedAfterValidation = (await db.Database.GetAppliedMigrationsAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            EnsureNoUnknownAppliedMigrations(
                knownMigrations,
                appliedAfterValidation,
                migrationSetName,
                logger);

            var remaining = knownMigrations
                .Where(migration => !appliedAfterValidation.Contains(migration))
                .ToList();
            if (remaining.Count > 0)
            {
                var message =
                    $"Migration set {migrationSetName} still has {remaining.Count} unapplied migration(s): " +
                    string.Join(", ", remaining);
                logger.LogCritical(message);
                throw new InvalidOperationException(message);
            }

            var latestApplied = knownMigrations.LastOrDefault(appliedAfterValidation.Contains);
            logger.LogInformation(
                "Database startup gate completed for {MigrationSet}. Latest applied migration: {LatestMigration}.",
                migrationSetName,
                latestApplied ?? "(none)");

            return new DatabaseStartupMigrationResult(
                MigrationSetName: migrationSetName,
                IsRelational: true,
                PendingBefore: pendingBefore,
                AppliedAfter: appliedAfterMigration,
                LatestAppliedMigration: latestApplied);
        }
        finally
        {
            db.Database.SetCommandTimeout(originalCommandTimeout);
        }
    }

    private static void ValidatePostgresDeploymentBoundary(
        DbContext lockDb,
        IReadOnlyList<DatabaseStartupMigrationPlan> plans)
    {
        if (!lockDb.Database.IsRelational())
        {
            return;
        }

        var boundaryProvider = lockDb.Database.ProviderName;
        var providerMismatches = plans
            .Where(plan => plan.DbContext.Database.IsRelational())
            .Where(plan => !string.Equals(
                plan.DbContext.Database.ProviderName,
                boundaryProvider,
                StringComparison.Ordinal))
            .Select(plan => $"{plan.MigrationSetName}={plan.DbContext.Database.ProviderName}")
            .ToArray();
        if (providerMismatches.Length > 0)
        {
            throw new InvalidOperationException(
                "All relational contexts in the PRISM startup migration boundary must use the same provider. " +
                $"Boundary provider={boundaryProvider}; mismatches={string.Join(", ", providerMismatches)}.");
        }

        if (!lockDb.Database.IsNpgsql())
        {
            return;
        }

        var boundary = ReadPostgresIdentity(lockDb);
        foreach (var plan in plans.Where(plan => plan.DbContext.Database.IsNpgsql()))
        {
            var candidate = ReadPostgresIdentity(plan.DbContext);
            if (!string.Equals(boundary.Host, candidate.Host, StringComparison.OrdinalIgnoreCase)
                || boundary.Port != candidate.Port
                || !string.Equals(boundary.Database, candidate.Database, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "All PostgreSQL contexts in the PRISM startup migration boundary must target the same server and database. " +
                    $"Lock database={boundary.Host}:{boundary.Port}/{boundary.Database}; " +
                    $"{plan.MigrationSetName}={candidate.Host}:{candidate.Port}/{candidate.Database}.");
            }
        }
    }

    private static PostgresDatabaseIdentity ReadPostgresIdentity(DbContext db)
    {
        var connectionString = db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A PostgreSQL connection string is required for the startup migration boundary.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var host = builder.Host?.Trim();
        var database = builder.Database?.Trim();

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "The PostgreSQL connection string for the startup migration boundary must specify a non-empty Host.");
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException(
                "The PostgreSQL connection string for the startup migration boundary must specify a non-empty Database.");
        }

        return new PostgresDatabaseIdentity(host, builder.Port, database);
    }

    private static void EnsureNoUnknownAppliedMigrations(
        IReadOnlyCollection<string> knownMigrations,
        IReadOnlyCollection<string> appliedMigrations,
        string migrationSetName,
        ILogger logger)
    {
        var known = knownMigrations.ToHashSet(StringComparer.Ordinal);
        var unknownApplied = appliedMigrations
            .Where(migration => !known.Contains(migration))
            .OrderBy(migration => migration, StringComparer.Ordinal)
            .ToList();
        if (unknownApplied.Count == 0)
        {
            return;
        }

        var message =
            $"Database migration history for {migrationSetName} contains {unknownApplied.Count} migration(s) " +
            "that are not present in the deployed assembly: " + string.Join(", ", unknownApplied) +
            ". Deploy the matching or newer application build; database downgrades are not supported.";
        logger.LogCritical(message);
        throw new InvalidOperationException(message);
    }

    private static string FormatList(IReadOnlyCollection<string> values)
        => values.Count == 0 ? "(none)" : string.Join(", ", values);

    private static async Task<NpgsqlConnection> OpenMigrationLockConnectionAsync(
        DbContext db,
        CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A PostgreSQL connection string is required to acquire the startup migration lock.");
        }

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            // A session-level advisory lock must never be returned to a pool if explicit
            // unlock encounters an infrastructure failure. Closing this dedicated physical
            // session is the final lock-release guarantee.
            Pooling = false,
            ApplicationName = "PRISM migration gate"
        };

        var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> AcquireAdvisoryLockAsync(
        NpgsqlConnection connection,
        ILogger logger,
        string migrationSetName,
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
                    "Acquired the PostgreSQL migration advisory lock for {MigrationSet}.",
                    migrationSetName);
                return true;
            }

            if (!waitLogged)
            {
                logger.LogInformation(
                    "Waiting for another application instance to finish database migrations before processing {MigrationSet}.",
                    migrationSetName);
                waitLogged = true;
            }

            await Task.Delay(AdvisoryLockPollInterval, cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out after {AdvisoryLockTimeout.TotalMinutes:0} minutes waiting to migrate {migrationSetName}.");
    }

    private static async Task<IReadOnlyList<string>> VerifyMigrationClosureAsync(
        DbContext db,
        IReadOnlyCollection<string> knownMigrations,
        ILogger logger,
        string migrationSetName,
        CancellationToken cancellationToken)
    {
        var appliedList = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var applied = appliedList.ToHashSet(StringComparer.Ordinal);
        EnsureNoUnknownAppliedMigrations(knownMigrations, applied, migrationSetName, logger);

        var missing = knownMigrations
            .Where(migration => !applied.Contains(migration))
            .ToList();

        if (missing.Count == 0)
        {
            logger.LogInformation(
                "All {Count} migrations in {MigrationSet} are recorded as applied.",
                knownMigrations.Count,
                migrationSetName);
            return appliedList;
        }

        var message =
            $"Migration closure verification failed for {migrationSetName}. " +
            $"{missing.Count} migration(s) from the deployed assembly are not applied: " +
            string.Join(", ", missing);
        logger.LogCritical(message);
        throw new InvalidOperationException(message);
    }

    private sealed record MigrationPreflightState(
        bool IsRelational,
        IReadOnlyList<string> KnownMigrations,
        IReadOnlyList<string> PendingBefore)
    {
        public static MigrationPreflightState NotApplicable { get; } =
            new(false, Array.Empty<string>(), Array.Empty<string>());
    }

    private sealed record PostgresDatabaseIdentity(string Host, int Port, string Database);
}

public sealed record DatabaseStartupMigrationPlan(
    DbContext DbContext,
    string MigrationSetName,
    bool ApplyMigrations = true,
    Func<CancellationToken, Task>? ValidateSchemaAsync = null,
    IReadOnlyList<string>? ExpectedMigrationIds = null);

public sealed record DatabaseStartupMigrationResult(
    string MigrationSetName,
    bool IsRelational,
    IReadOnlyList<string> PendingBefore,
    IReadOnlyList<string> AppliedAfter,
    string? LatestAppliedMigration)
{
    public static DatabaseStartupMigrationResult NotApplicable(string migrationSetName) =>
        new(migrationSetName, false, Array.Empty<string>(), Array.Empty<string>(), null);
}
