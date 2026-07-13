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
            token => CheckWritableDirectoryAsync(dataProtectionRoot, token),
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
                    stopwatch.ElapsedMilliseconds,
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
            return (AdminHealthStatus.Warning, "The configured directory does not exist.", path);
        }

        var probe = Path.Combine(path, $".prism-health-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probe, "ok", cancellationToken);
            return (AdminHealthStatus.Healthy, "The directory is available and writable.", path);
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
        var summary = _environment.IsProduction()
            ? "Production environment configuration is active."
            : $"{_environment.EnvironmentName} environment configuration is active.";
        var detail = $"Version {ResolveApplicationVersion()} · Startup migrations {(migrationsOnStartup ? "enabled" : "disabled")}.";
        return new(
            "application-runtime",
            "Application security",
            "Application runtime",
            AdminHealthStatus.Healthy,
            summary,
            detail,
            null,
            0,
            DateTimeOffset.UtcNow);
    }

    private IEnumerable<AdminSystemHealthCheck> BuildWorkerChecks()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var worker in _workers.GetSnapshot())
        {
            var status = worker.State switch
            {
                AdminWorkerState.Failed => AdminHealthStatus.Critical,
                AdminWorkerState.Registered when now - worker.RegisteredUtc > TimeSpan.FromMinutes(5) => AdminHealthStatus.Warning,
                _ => AdminHealthStatus.Healthy
            };
            var summary = worker.State switch
            {
                AdminWorkerState.Registered => "Registered; no processing cycle has completed since application start.",
                AdminWorkerState.Running => "A processing cycle is currently running.",
                AdminWorkerState.Healthy => "The latest processing cycle completed successfully.",
                AdminWorkerState.Failed => "The latest processing cycle failed.",
                _ => "Worker state is unavailable."
            };
            var detail = worker.State == AdminWorkerState.Failed
                ? $"Last failure: {worker.LastFailedUtc:dd MMM yyyy, HH:mm} UTC{(string.IsNullOrWhiteSpace(worker.Detail) ? string.Empty : $" · {worker.Detail}")}."
                : worker.LastSucceededUtc.HasValue
                    ? $"Last successful cycle: {worker.LastSucceededUtc:dd MMM yyyy, HH:mm} UTC{(string.IsNullOrWhiteSpace(worker.Detail) ? string.Empty : $" · {worker.Detail}")}."
                    : worker.Detail;

            yield return new(
                $"worker-{worker.Key}",
                "Background services",
                worker.Label,
                status,
                summary,
                detail,
                status == AdminHealthStatus.Healthy ? null : "Review application logs and the worker configuration.",
                0,
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
