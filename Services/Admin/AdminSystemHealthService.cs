using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Admin;

public sealed record AdminSystemHealthCheck(
    string Key,
    string Group,
    string Label,
    AdminHealthStatus Status,
    string Summary,
    string? Detail,
    string? RecommendedAction,
    long DurationMilliseconds,
    DateTimeOffset CheckedUtc);

public sealed record AdminSystemHealthSnapshot(
    AdminHealthStatus OverallStatus,
    DateTimeOffset CheckedUtc,
    string EnvironmentName,
    string ApplicationVersion,
    IReadOnlyList<AdminSystemHealthCheck> Checks,
    DatabaseHealthSnapshot Database)
{
    public int HealthyCount => Checks.Count(check => check.Status == AdminHealthStatus.Healthy);
    public int WarningCount => Checks.Count(check => check.Status == AdminHealthStatus.Warning);
    public int CriticalCount => Checks.Count(check => check.Status == AdminHealthStatus.Critical);
    public int UnavailableCount => Checks.Count(check => check.Status == AdminHealthStatus.Unavailable);
}

public interface IAdminSystemHealthService
{
    Task<AdminSystemHealthSnapshot> CheckAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregates independent, timeout-bound checks across the database, storage,
/// data-protection and background-processing domains.
/// </summary>
public sealed class AdminSystemHealthService : IAdminSystemHealthService
{
    private const string CacheKey = "admin-system-health-v1";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private readonly IDatabaseHealthService _database;
    private readonly IUploadRootProvider _uploads;
    private readonly DocRepoOptions _docRepo;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IAdminWorkerStatusRegistry _workers;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AdminSystemHealthService> _logger;

    public AdminSystemHealthService(
        IDatabaseHealthService database,
        IUploadRootProvider uploads,
        IOptions<DocRepoOptions> docRepo,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        IAdminWorkerStatusRegistry workers,
        IMemoryCache cache,
        ILogger<AdminSystemHealthService> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
        _docRepo = docRepo?.Value ?? throw new ArgumentNullException(nameof(docRepo));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _workers = workers ?? throw new ArgumentNullException(nameof(workers));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AdminSystemHealthSnapshot> CheckAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (forceRefresh) _cache.Remove(CacheKey);
        if (_cache.TryGetValue(CacheKey, out AdminSystemHealthSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        var checks = new List<AdminSystemHealthCheck>();
        var databaseSnapshot = await RunDatabaseChecksAsync(checks, cancellationToken);

        checks.Add(await RunAsync(
            "upload-root",
            "Storage",
            "Primary upload storage",
            token => CheckWritableDirectoryAsync(_uploads.RootPath, token),
            "Configure PM_UPLOAD_ROOT or ProjectPhotos:StorageRoot to a writable persistent location.",
            cancellationToken));

        var docRepoRoot = ResolvePath(_docRepo.RootPath);
        checks.Add(await RunAsync(
            "document-repository",
            "Storage",
            "Document repository storage",
            token => CheckWritableDirectoryAsync(docRepoRoot, token),
            "Configure DocRepo:RootPath to a writable persistent location.",
            cancellationToken));

        var dataProtectionRoot = ResolveDataProtectionPath();
        checks.Add(await RunAsync(
            "data-protection-keys",
            "Application security",
            "Data Protection key storage",
            token => CheckDataProtectionDirectoryAsync(dataProtectionRoot, token),
            "Configure DP_KEYS_DIR to a persistent writable directory shared by the deployed application instance.",
            cancellationToken));

        checks.Add(BuildDiskSpaceCheck(_uploads.RootPath));
        checks.Add(BuildApplicationCheck());
        checks.AddRange(BuildWorkerChecks());

        var overall = ResolveOverallStatus(checks);
        var snapshot = new AdminSystemHealthSnapshot(
            overall,
            DateTimeOffset.UtcNow,
            _environment.EnvironmentName,
            ResolveApplicationVersion(),
            checks,
            databaseSnapshot);

        _cache.Set(CacheKey, snapshot, CacheDuration);
        return snapshot;
    }

    private async Task<DatabaseHealthSnapshot> RunDatabaseChecksAsync(
        ICollection<AdminSystemHealthCheck> target,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);
            var snapshot = await _database.CheckAsync(timeout.Token);
            stopwatch.Stop();

            foreach (var check in snapshot.Checks)
            {
                target.Add(new AdminSystemHealthCheck(
                    $"database-{check.Key}",
                    "Database",
                    check.Label,
                    check.Status,
                    check.Summary,
                    check.Detail,
                    check.Status is AdminHealthStatus.Critical or AdminHealthStatus.Warning
                        ? "Review the migration and database deployment guidance before the next release."
                        : null,
                    -1,
                    DateTimeOffset.UtcNow));
            }

            return snapshot;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            target.Add(new AdminSystemHealthCheck(
                "database-timeout",
                "Database",
                "Database diagnostics",
                AdminHealthStatus.Critical,
                "Database diagnostics exceeded the permitted execution time.",
                null,
                "Review database availability and application logs.",
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow));
            return EmptyDatabaseSnapshot();
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(exception, "System health database diagnostics failed.");
            target.Add(new AdminSystemHealthCheck(
                "database-failure",
                "Database",
                "Database diagnostics",
                AdminHealthStatus.Critical,
                "Database diagnostics could not be completed.",
                null,
                "Review application logs for the diagnostic reference.",
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow));
            return EmptyDatabaseSnapshot();
        }
    }

    private async Task<AdminSystemHealthCheck> RunAsync(
        string key,
        string group,
        string label,
        Func<CancellationToken, Task<(AdminHealthStatus Status, string Summary, string? Detail)>> check,
        string recommendedAction,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);
            var result = await check(timeout.Token);
            stopwatch.Stop();
            return new(
                key,
                group,
                label,
                result.Status,
                result.Summary,
                result.Detail,
                result.Status == AdminHealthStatus.Healthy ? null : recommendedAction,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new(
                key,
                group,
                label,
                AdminHealthStatus.Warning,
                "The check exceeded the permitted execution time.",
                null,
                recommendedAction,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogWarning(exception, "System health check {HealthCheckKey} failed.", key);
            return new(
                key,
                group,
                label,
                AdminHealthStatus.Warning,
                "The check could not be completed.",
                null,
                recommendedAction,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
    }

    private static async Task<(AdminHealthStatus Status, string Summary, string? Detail)> CheckWritableDirectoryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return (AdminHealthStatus.Warning, "The configured directory does not exist.", null);
        }

        var probe = Path.Combine(path, $".prism-health-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probe, "ok", cancellationToken);
            return (AdminHealthStatus.Healthy, "The directory is available and writable.", null);
        }
        finally
        {
            try
            {
                if (File.Exists(probe)) File.Delete(probe);
            }
            catch
            {
                // Probe cleanup failure must not mask the primary result.
            }
        }
    }

    private async Task<(AdminHealthStatus Status, string Summary, string? Detail)> CheckDataProtectionDirectoryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var writable = await CheckWritableDirectoryAsync(path, cancellationToken);
        if (writable.Status != AdminHealthStatus.Healthy)
        {
            return writable;
        }

        var explicitlyConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DP_KEYS_DIR"));
        if (_environment.IsProduction() && !explicitlyConfigured)
        {
            return (
                AdminHealthStatus.Warning,
                "Key storage is writable but is using the production fallback location.",
                "Set DP_KEYS_DIR to an explicitly managed persistent location before deployment.");
        }

        return (
            AdminHealthStatus.Healthy,
            "The key directory is available and writable.",
            explicitlyConfigured ? "Persistent key storage is explicitly configured." : "Development key storage is active.");
    }

    private AdminSystemHealthCheck BuildDiskSpaceCheck(string path)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root)) throw new IOException("Storage root could not be determined.");

            var drive = new DriveInfo(root);
            var freePercent = drive.TotalSize <= 0 ? 0 : drive.AvailableFreeSpace * 100d / drive.TotalSize;
            var status = freePercent switch
            {
                < 5 => AdminHealthStatus.Critical,
                < 15 => AdminHealthStatus.Warning,
                _ => AdminHealthStatus.Healthy
            };
            stopwatch.Stop();
            return new(
                "storage-capacity",
                "Storage",
                "Storage capacity",
                status,
                $"{freePercent:0.#}% free on the volume containing the upload root.",
                $"{FormatBytes(drive.AvailableFreeSpace)} available of {FormatBytes(drive.TotalSize)}.",
                status == AdminHealthStatus.Healthy ? null : "Free storage capacity or move uploads to a larger persistent volume.",
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogDebug(exception, "Storage capacity could not be determined for {StoragePath}.", path);
            return new(
                "storage-capacity",
                "Storage",
                "Storage capacity",
                AdminHealthStatus.Unavailable,
                "Available storage capacity could not be determined.",
                null,
                null,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
    }

    private AdminSystemHealthCheck BuildApplicationCheck()
    {
        var migrationsOnStartup = _configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");
        var production = _environment.IsProduction();
        var status = production ? AdminHealthStatus.Healthy : AdminHealthStatus.Warning;
        var summary = production
            ? "Production environment configuration is active."
            : $"{_environment.EnvironmentName} environment configuration is active; this is not a production-readiness result.";
        var detail = $"Version {ResolveApplicationVersion()} · Startup migrations {(migrationsOnStartup ? "enabled" : "disabled")}.";
        return new(
            "application-runtime",
            "Application security",
            "Application runtime",
            status,
            summary,
            detail,
            production ? null : "Use the Production environment and production configuration when validating deployment readiness.",
            -1,
            DateTimeOffset.UtcNow);
    }

    private IEnumerable<AdminSystemHealthCheck> BuildWorkerChecks()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var worker in _workers.GetSnapshot())
        {
            var expected = worker.ExpectedInterval;
            var staleAfter = expected.HasValue
                ? expected.Value + TimeSpan.FromTicks(Math.Max(expected.Value.Ticks, TimeSpan.FromMinutes(15).Ticks))
                : TimeSpan.FromHours(48);
            var runningTooLong = worker.State == AdminWorkerState.Running
                && worker.LastStartedUtc.HasValue
                && now - worker.LastStartedUtc.Value > staleAfter;
            var stale = worker.State == AdminWorkerState.Healthy
                && worker.LastSucceededUtc.HasValue
                && now - worker.LastSucceededUtc.Value > staleAfter;

            var status = worker.State switch
            {
                AdminWorkerState.Failed => AdminHealthStatus.Critical,
                AdminWorkerState.Registered => AdminHealthStatus.Unavailable,
                AdminWorkerState.Running when runningTooLong => AdminHealthStatus.Warning,
                AdminWorkerState.Healthy when stale => AdminHealthStatus.Warning,
                _ => AdminHealthStatus.Healthy
            };

            var summary = worker.State switch
            {
                AdminWorkerState.Registered => "Starting; no processing cycle has completed since application start.",
                AdminWorkerState.Running when runningTooLong => "The current processing cycle has exceeded its expected completion window.",
                AdminWorkerState.Running => "A processing cycle is currently running.",
                AdminWorkerState.Healthy when stale => "The latest successful cycle is older than the expected schedule.",
                AdminWorkerState.Healthy => "The latest processing cycle completed successfully.",
                AdminWorkerState.Failed => "The latest processing cycle failed.",
                _ => "Worker state is unavailable."
            };

            var details = new List<string>();
            if (expected.HasValue)
            {
                details.Add($"Expected cadence: {FormatDuration(expected.Value)}");
            }
            if (worker.LastSucceededUtc.HasValue)
            {
                details.Add($"Last successful cycle: {worker.LastSucceededUtc:dd MMM yyyy, HH:mm} UTC");
            }
            else if (worker.LastStartedUtc.HasValue)
            {
                details.Add($"Last started: {worker.LastStartedUtc:dd MMM yyyy, HH:mm} UTC");
            }
            else
            {
                details.Add($"Registered: {worker.RegisteredUtc:dd MMM yyyy, HH:mm} UTC");
            }
            if (!string.IsNullOrWhiteSpace(worker.Detail))
            {
                details.Add(worker.State == AdminWorkerState.Failed
                    ? $"Failure type: {worker.Detail}"
                    : worker.Detail);
            }

            yield return new(
                $"worker-{worker.Key}",
                "Background services",
                worker.Label,
                status,
                summary,
                string.Join(" · ", details) + ".",
                status is AdminHealthStatus.Warning or AdminHealthStatus.Critical
                    ? "Review application logs and the worker schedule or configuration."
                    : null,
                -1,
                now);
        }
    }

    private string ResolvePath(string configuredPath)
    {
        var path = Environment.ExpandEnvironmentVariables(configuredPath);
        if (!Path.IsPathRooted(path)) path = Path.Combine(_environment.ContentRootPath, path);
        return Path.GetFullPath(path);
    }

    private string ResolveDataProtectionPath()
    {
        var configured = Environment.GetEnvironmentVariable("DP_KEYS_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));

        return _environment.IsDevelopment()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PRISM-ERP", "DataProtectionKeys")
            : "/var/pm/keys";
    }

    private static AdminHealthStatus ResolveOverallStatus(IEnumerable<AdminSystemHealthCheck> checks)
    {
        var statuses = checks.Select(check => check.Status).ToArray();
        if (statuses.Length == 0) return AdminHealthStatus.Unavailable;
        if (statuses.Contains(AdminHealthStatus.Critical)) return AdminHealthStatus.Critical;
        if (statuses.Contains(AdminHealthStatus.Warning)) return AdminHealthStatus.Warning;
        if (statuses.All(status => status == AdminHealthStatus.Unavailable)) return AdminHealthStatus.Unavailable;
        if (statuses.Contains(AdminHealthStatus.Unavailable)) return AdminHealthStatus.Warning;
        return AdminHealthStatus.Healthy;
    }

    private static string ResolveApplicationVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1) return $"{duration.TotalDays:0.#} day(s)";
        if (duration.TotalHours >= 1) return $"{duration.TotalHours:0.#} hour(s)";
        return $"{Math.Max(1, duration.TotalMinutes):0} minute(s)";
    }

    private static string FormatBytes(long bytes)
    {
        var size = (double)bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var index = 0;
        while (size >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:0.##} {units[index]}";
    }

    private static DatabaseHealthSnapshot EmptyDatabaseSnapshot() => new(
        false,
        null,
        null,
        null,
        null,
        null,
        null,
        "(not available)",
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<DatabaseHealthCheck>());
}
