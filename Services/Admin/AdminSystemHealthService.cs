using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Indexing;
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

    /// <summary>
    /// The result of the most recent run, without running the checks again (null when they have
    /// not run since the application started). Administration Home uses it so opening Home never
    /// waits on storage or database checks.
    /// </summary>
    AdminSystemHealthSnapshot? GetLatest() => null;
}

/// <summary>
/// Aggregates independent, timeout-bound checks across the database, storage,
/// data-protection and background-processing domains.
/// </summary>
public sealed class AdminSystemHealthService : IAdminSystemHealthService
{
    private const string CacheKey = "admin-system-health-v1";
    private const string LatestKey = "admin-system-health-latest-v1";
    private static readonly TimeSpan LatestDuration = TimeSpan.FromDays(1);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private readonly IDatabaseHealthService _database;
    private readonly IUploadRootProvider _uploads;
    private readonly DocRepoOptions _docRepo;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IAdminWorkerStatusRegistry _workers;
    private readonly ISearchIndexStore _searchIndex;
    private readonly ApplicationDbContext _db;
    private readonly IDocStorage _docStorage;
    private readonly IOptionsMonitor<ChatOptions> _chat;
    private readonly IServiceProvider _services;
    private readonly IOptions<SearchV2Options> _searchOptions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AdminSystemHealthService> _logger;

    public AdminSystemHealthService(
        IDatabaseHealthService database,
        IUploadRootProvider uploads,
        IOptions<DocRepoOptions> docRepo,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        IAdminWorkerStatusRegistry workers,
        ISearchIndexStore searchIndex,
        ApplicationDbContext db,
        IDocStorage docStorage,
        IOptionsMonitor<ChatOptions> chat,
        IServiceProvider services,
        IOptions<SearchV2Options> searchOptions,
        IMemoryCache cache,
        ILogger<AdminSystemHealthService> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
        _docRepo = docRepo?.Value ?? throw new ArgumentNullException(nameof(docRepo));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _workers = workers ?? throw new ArgumentNullException(nameof(workers));
        _searchIndex = searchIndex ?? throw new ArgumentNullException(nameof(searchIndex));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _docStorage = docStorage ?? throw new ArgumentNullException(nameof(docStorage));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _searchOptions = searchOptions ?? throw new ArgumentNullException(nameof(searchOptions));
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

        // Search is part of PRISM's health: items that could not be indexed are missing from
        // results until someone retries them (docs/ux/admin-module.md, PR 7).
        checks.Add(await RunAsync(
            "search-index",
            "Search",
            "Search index",
            CheckSearchIndexAsync,
            "Open Search index to retry the items that failed, or rebuild the index.",
            cancellationToken));

        // Problems that used to show only on their own pages (docs/ux/admin-review-fixes.md, PR 3):
        // files the Document Repository cannot open, media PRISM cannot find, face recognition
        // without its detector, and chat uploads about to stop for lack of disk space.
        checks.Add(await RunAsync(
            "document-repository-files",
            "Storage",
            "Document Repository files",
            CheckRepositoryFilesAsync,
            "Open Missing files (Document Repository) to see which documents have lost their file, then restore the files from backup.",
            cancellationToken));

        if (_configuration.GetValue<bool?>("MediaLibrary:Enabled") ?? true)
        {
            checks.Add(await RunAsync(
                "media-availability",
                "Media library",
                "Photos and videos",
                CheckMediaAvailabilityAsync,
                "Open Media sources: check that the media folders are reachable, then use Recheck under Unavailable media.",
                cancellationToken));
            checks.Add(await RunAsync(
                "media-face-detector",
                "Media library",
                "Face recognition",
                CheckFaceDetectorAsync,
                "Open Media intelligence and install the approved detector file it names.",
                cancellationToken));
        }

        var chatUploads = BuildChatUploadSpaceCheck(_uploads.RootPath);
        if (chatUploads is not null)
        {
            checks.Add(chatUploads);
        }

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
        _cache.Set(LatestKey, snapshot, LatestDuration);
        return snapshot;
    }

    public AdminSystemHealthSnapshot? GetLatest() =>
        _cache.TryGetValue(LatestKey, out AdminSystemHealthSnapshot? latest) ? latest : null;

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

        var explicitlyConfigured = !string.IsNullOrWhiteSpace(_configuration["DP_KEYS_DIR"]);
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
                details.Add($"Last successful cycle: {FormatIst(worker.LastSucceededUtc.Value)}");
            }
            else if (worker.LastStartedUtc.HasValue)
            {
                details.Add($"Last started: {FormatIst(worker.LastStartedUtc.Value)}");
            }
            else
            {
                details.Add($"Registered: {FormatIst(worker.RegisteredUtc)}");
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
                EndSentence(string.Join(" · ", details)),
                status is AdminHealthStatus.Warning or AdminHealthStatus.Critical
                    ? "Review application logs and the worker schedule or configuration."
                    : null,
                -1,
                now);
        }
    }

    private async Task<(AdminHealthStatus Status, string Summary, string? Detail)> CheckSearchIndexAsync(CancellationToken cancellationToken)
    {
        var index = await _searchIndex.GetHealthAsync(cancellationToken);
        return SearchIndexResult(index, _searchOptions.Value.ProjectionVersion);
    }

    // Search answers only from an index built for this version of PRISM (SearchEngine rejects any
    // other version), so a version mismatch is "not ready" even when nothing has failed.
    internal static (AdminHealthStatus Status, string Summary, string? Detail) SearchIndexResult(
        ProjectManagement.Services.SearchV2.Models.SearchIndexHealth index,
        int requiredProjectionVersion)
    {
        var detail = $"{index.EntryCount:N0} items searchable · {index.PendingItems:N0} waiting to be indexed.";
        if (index.IsReady && index.IndexVersion != requiredProjectionVersion)
        {
            return (
                AdminHealthStatus.Warning,
                "The search index was built for a different version of PRISM, so search shows no results until it is rebuilt.",
                $"Index version {index.IndexVersion}; this version of PRISM needs {requiredProjectionVersion}. " + detail);
        }

        if (!index.IsReady)
        {
            return (AdminHealthStatus.Warning, "The search index is not ready yet, so search shows few or no results.", detail);
        }

        if (index.FailedItems > 0)
        {
            return (
                AdminHealthStatus.Warning,
                index.FailedItems == 1
                    ? "1 item could not be added to search, so it is missing from results."
                    : $"{index.FailedItems:N0} items could not be added to search, so they are missing from results.",
                detail);
        }

        return (AdminHealthStatus.Healthy, "Search is up to date.", detail);
    }

    // Each file check is a cheap existence test; the newest documents are checked first and the
    // total is capped so a very large repository cannot hold the check past its time limit.
    internal const int RepositoryFileCheckLimit = 5000;

    private async Task<(AdminHealthStatus Status, string Summary, string? Detail)> CheckRepositoryFilesAsync(CancellationToken cancellationToken)
    {
        var paths = await _db.Documents
            .AsNoTracking()
            .Where(document => !document.IsDeleted)
            .OrderByDescending(document => document.CreatedAtUtc)
            .Select(document => document.StoragePath)
            .Take(RepositoryFileCheckLimit + 1)
            .ToListAsync(cancellationToken);

        var capped = paths.Count > RepositoryFileCheckLimit;
        if (capped) paths.RemoveAt(paths.Count - 1);

        var missing = 0;
        foreach (var path in paths)
        {
            if (!await _docStorage.ExistsAsync(path ?? string.Empty, cancellationToken)) missing++;
        }

        return RepositoryFilesResult(paths.Count, missing, capped);
    }

    internal static (AdminHealthStatus Status, string Summary, string? Detail) RepositoryFilesResult(int checkedCount, int missing, bool capped)
    {
        var scope = capped ? $"the newest {checkedCount:N0} documents" : $"{checkedCount:N0} document{(checkedCount == 1 ? "" : "s")}";
        if (missing == 0)
        {
            return (AdminHealthStatus.Healthy, "Every document's file is on disk.", $"Checked {scope}.");
        }

        return (
            AdminHealthStatus.Warning,
            missing == 1
                ? "1 document has lost its file, so it cannot be opened or downloaded."
                : $"{missing:N0} documents have lost their file, so they cannot be opened or downloaded.",
            $"Checked {scope}.");
    }

    private async Task<(AdminHealthStatus Status, string Summary, string? Detail)> CheckMediaAvailabilityAsync(CancellationToken cancellationToken)
    {
        var service = _services.GetService<IMediaAvailabilityRecoveryService>();
        if (service is null)
        {
            return (AdminHealthStatus.Healthy, "The media library is not in use.", null);
        }

        var status = await service.GetStatusAsync(cancellationToken);
        return MediaAvailabilityResult(status.AvailableAssets, status.UnavailableAssets);
    }

    internal static (AdminHealthStatus Status, string Summary, string? Detail) MediaAvailabilityResult(int available, int unavailable)
    {
        var total = available + unavailable;
        if (unavailable == 0)
        {
            return (AdminHealthStatus.Healthy, "Every photo and video can be found.", $"{total:N0} items in the media library.");
        }

        return (
            AdminHealthStatus.Warning,
            unavailable == 1
                ? "1 photo or video cannot be found where PRISM reads it, so it is hidden from Photos."
                : $"{unavailable:N0} photos and videos cannot be found where PRISM reads them, so they are hidden from Photos.",
            $"{unavailable:N0} of {total:N0} items unavailable.");
    }

    private async Task<(AdminHealthStatus Status, string Summary, string? Detail)> CheckFaceDetectorAsync(CancellationToken cancellationToken)
    {
        var readiness = _services.GetService<IFaceModelReadinessService>();
        if (readiness is null)
        {
            return (AdminHealthStatus.Healthy, "Face recognition is not in use.", null);
        }

        var detector = await readiness.CheckDetectorAsync(cancellationToken);
        return FaceDetectorResult(detector.IsEnabled, detector.IsReady, detector.Message);
    }

    internal static (AdminHealthStatus Status, string Summary, string? Detail) FaceDetectorResult(bool enabled, bool ready, string message)
    {
        if (!enabled)
        {
            return (AdminHealthStatus.Healthy, "Face recognition is switched off.", null);
        }

        return ready
            ? (AdminHealthStatus.Healthy, "Face recognition is ready.", null)
            : (AdminHealthStatus.Warning, "Face recognition in Photos is not working: its detector is not ready.", message);
    }

    // Chat stops accepting files below a free-space floor; warn while there is still room to act.
    private AdminSystemHealthCheck? BuildChatUploadSpaceCheck(string path)
    {
        var attachments = _chat.CurrentValue.Attachments;
        if (!_chat.CurrentValue.Enabled || !attachments.Enabled) return null;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root)) return null;
            var free = new DriveInfo(root).AvailableFreeSpace;
            var result = ChatUploadSpaceResult(free, attachments.MinimumFreeDiskBytes);
            return new(
                "chat-upload-space",
                "Storage",
                "Space for chat files",
                result.Status,
                result.Summary,
                result.Detail,
                result.Status == AdminHealthStatus.Healthy ? null : "Free space on the upload disk, or remove old chat files in Chat file storage.",
                -1,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Free space for chat uploads could not be determined for {StoragePath}.", path);
            return null;
        }
    }

    internal static (AdminHealthStatus Status, string Summary, string? Detail) ChatUploadSpaceResult(long freeBytes, long minimumBytes)
    {
        var detail = $"{FormatBytes(freeBytes)} free; chat uploads stop below {FormatBytes(minimumBytes)}.";
        if (freeBytes < minimumBytes)
        {
            return (AdminHealthStatus.Critical, "Chat file uploads have stopped: the upload disk is below its free-space limit.", detail);
        }

        // Warn within half the limit again (for a 20 GB limit, below 30 GB free).
        return freeBytes < minimumBytes + minimumBytes / 2
            ? (AdminHealthStatus.Warning, "The upload disk is close to the point where chat file uploads stop.", detail)
            : (AdminHealthStatus.Healthy, "There is room for chat files.", detail);
    }

    internal static string EndSentence(string text) =>
        text.EndsWith('.') ? text : text + ".";

    private string ResolvePath(string configuredPath)
    {
        var path = Environment.ExpandEnvironmentVariables(configuredPath);
        if (!Path.IsPathRooted(path)) path = Path.Combine(_environment.ContentRootPath, path);
        return Path.GetFullPath(path);
    }

    // Worker times were shown in UTC while the rest of Administration uses IST.
    private static string FormatIst(DateTimeOffset utc) =>
        ProjectManagement.Infrastructure.IstClock.ToIst(utc).ToString("dd MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " IST";

    private string ResolveDataProtectionPath()
    {
        // Same source as Program.cs: configuration (environment variable, appsettings or
        // command line), not the environment variable alone, so the check probes the folder
        // the application actually uses.
        var configured = _configuration["DP_KEYS_DIR"];
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

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
