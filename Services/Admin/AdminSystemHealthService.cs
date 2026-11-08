using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Chat;
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

    // One run of the checks at a time in this process (docs/ux/admin-review-2-fixes.md, PR 11):
    // when several administrators press "Run checks again", or the 45-second cache expires
    // under them, the others wait for that run and reuse its result instead of starting
    // their own scans of the database, the files and the media library.
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    private readonly IDatabaseHealthService _database;
    private readonly IUploadRootProvider _uploads;
    private readonly DocRepoOptions _docRepo;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IAdminWorkerStatusRegistry _workers;
    private readonly ISearchIndexStore _searchIndex;
    private readonly ApplicationDbContext _db;
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

        await RunGate.WaitAsync(cancellationToken);
        try
        {
            // A run that finished while this request waited is fresh enough, even for "Run
            // checks again": its result was produced after this request removed the old one.
            if (_cache.TryGetValue(CacheKey, out AdminSystemHealthSnapshot? justRun) && justRun is not null)
            {
                return justRun;
            }

            return await RunChecksAsync(forceRefresh, cancellationToken);
        }
        finally
        {
            RunGate.Release();
        }
    }

    private async Task<AdminSystemHealthSnapshot> RunChecksAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var checks = new List<AdminSystemHealthCheck>();
        var databaseSnapshot = await RunDatabaseChecksAsync(checks, cancellationToken);

        checks.Add(await RunAsync(
            "upload-root",
            "Storage",
            "Upload folder",
            token => CheckWritableDirectoryAsync(_uploads.RootPath, token),
            "Ask whoever runs the PRISM server to point uploads at a kept folder PRISM can write to (setting PM_UPLOAD_ROOT or ProjectPhotos:StorageRoot).",
            cancellationToken,
            touchesFileSystem: true));

        var docRepoRoot = ResolvePath(_docRepo.RootPath);
        checks.Add(await RunAsync(
            "document-repository",
            "Storage",
            "Document Repository folder",
            token => CheckWritableDirectoryAsync(docRepoRoot, token),
            "Ask whoever runs the PRISM server to point the Document Repository at a kept folder PRISM can write to (setting DocRepo:RootPath).",
            cancellationToken,
            touchesFileSystem: true));

        var dataProtectionRoot = ResolveDataProtectionPath();
        checks.Add(await RunAsync(
            "data-protection-keys",
            "Security",
            "Sign-in key folder",
            token => CheckDataProtectionDirectoryAsync(dataProtectionRoot, token),
            "Ask whoever runs the PRISM server to keep sign-in keys in a folder that survives restarts (setting DP_KEYS_DIR); without it everyone is signed out when PRISM restarts.",
            cancellationToken,
            touchesFileSystem: true));

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
            token => CheckRepositoryFilesAsync(forceRefresh, token),
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
                "Open Media intelligence: it lists each model file that is missing or not approved.",
                cancellationToken));
        }

        // Free-space reads can block on an unreachable network share, so they run inside the
        // same time limit as every other check.
        var chatUploads = await RunWithinTimeLimitAsync(
            "chat-upload-space", "Storage", "Space for chat files",
            () => BuildChatUploadSpaceCheck(_uploads.RootPath), cancellationToken);
        if (chatUploads is not null)
        {
            checks.Add(chatUploads);
        }

        checks.Add((await RunWithinTimeLimitAsync(
            "storage-capacity", "Storage", "Disk space",
            () => BuildDiskSpaceCheck(_uploads.RootPath), cancellationToken))!);
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
                        ? "Ask whoever runs the PRISM server to finish the database update before anyone relies on this version."
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
                "Database checks",
                AdminHealthStatus.Critical,
                "The database checks took too long and were stopped.",
                null,
                "Check that the database server is running, then look at the application log.",
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
                "Database checks",
                AdminHealthStatus.Critical,
                "The database checks could not finish.",
                null,
                "Look at the application log for the reference.",
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
        CancellationToken cancellationToken,
        bool touchesFileSystem = false)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);
            // Folder checks start on the thread pool and are awaited with the time limit, so one
            // that blocks before its first await (Directory.Exists on an unreachable share) cannot
            // hold the page past the limit; it finishes in the background. Database checks stay
            // inline so an abandoned one never shares the request's DbContext with the next.
            var result = touchesFileSystem
                ? await Task.Run(() => check(timeout.Token), timeout.Token).WaitAsync(timeout.Token)
                : await check(timeout.Token);
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
                "The check took too long and was stopped.",
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
                "The check could not finish.",
                null,
                recommendedAction,
                stopwatch.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);
        }
    }

    /// <summary>Runs a synchronous check on the thread pool and gives up after the time limit.</summary>
    internal static async Task<AdminSystemHealthCheck?> RunWithinTimeLimitAsync(
        string key,
        string group,
        string label,
        Func<AdminSystemHealthCheck?> build,
        CancellationToken cancellationToken,
        TimeSpan? limit = null)
    {
        try
        {
            return await Task.Run(build, cancellationToken).WaitAsync(limit ?? CheckTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new AdminSystemHealthCheck(
                key,
                group,
                label,
                AdminHealthStatus.Warning,
                "The check took too long and was stopped.",
                null,
                "Check that the upload folder's disk or network share can be reached.",
                (long)(limit ?? CheckTimeout).TotalMilliseconds,
                DateTimeOffset.UtcNow);
        }
    }

    private static async Task<(AdminHealthStatus Status, string Summary, string? Detail)> CheckWritableDirectoryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return (AdminHealthStatus.Warning, "The folder does not exist.", null);
        }

        var probe = Path.Combine(path, $".prism-health-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probe, "ok", cancellationToken);
            return (AdminHealthStatus.Healthy, "The folder is there and PRISM can write to it.", null);
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
                "PRISM can save sign-in keys, but only in its fallback folder.",
                "Set DP_KEYS_DIR to a folder that is kept and backed up.");
        }

        return (
            AdminHealthStatus.Healthy,
            "The sign-in key folder is there and PRISM can write to it.",
            explicitlyConfigured ? "The folder is set in the server settings." : "Using the development folder.");
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
                "Disk space",
                status,
                $"{freePercent:0.#}% of the upload disk is free.",
                $"{FormatBytes(drive.AvailableFreeSpace)} free of {FormatBytes(drive.TotalSize)}.",
                status == AdminHealthStatus.Healthy ? null : "Free some space, or move uploads to a larger disk.",
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
                "Disk space",
                AdminHealthStatus.Unavailable,
                "Free space on the upload disk could not be read.",
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
            ? "PRISM is using its live (Production) settings."
            : $"PRISM is using its {_environment.EnvironmentName} settings, so this page does not show whether the live server is ready.";
        var detail = $"Version {ResolveApplicationVersion()} · database updates at start-up {(migrationsOnStartup ? "on" : "off")}.";
        return new(
            "application-runtime",
            "Security",
            "PRISM version and settings",
            status,
            summary,
            detail,
            production ? null : "To check the live server, open this page on it; it uses the Production settings.",
            -1,
            DateTimeOffset.UtcNow);
    }

    private IEnumerable<AdminSystemHealthCheck> BuildWorkerChecks()
    {
        var now = DateTimeOffset.UtcNow;
        return _workers.GetSnapshot().Select(worker => WorkerCheck(worker, now)).ToArray();
    }

    internal static AdminSystemHealthCheck WorkerCheck(AdminWorkerStatus worker, DateTimeOffset now)
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
            AdminWorkerState.Registered => "Started with PRISM; it has not finished its first run yet.",
            AdminWorkerState.Running when runningTooLong => "Its current run is taking longer than expected.",
            AdminWorkerState.Running => "Running now.",
            AdminWorkerState.Healthy when stale => "Its last good run is older than expected.",
            AdminWorkerState.Healthy => "Its last run finished without problems.",
            AdminWorkerState.Failed => "Its last run failed.",
            AdminWorkerState.SwitchedOff => "Switched off.",
            _ => "Its state is not known."
        };

        var details = WorkerDetails(worker);
        return new(
            $"worker-{worker.Key}",
            "Background jobs",
            worker.Label,
            status,
            summary,
            details.Count == 0 ? null : EndSentence(string.Join(" · ", details)),
            status is AdminHealthStatus.Warning or AdminHealthStatus.Critical
                ? "Look at the application log for the error, then check the job's schedule and settings."
                : null,
            -1,
            now);
    }

    private static List<string> WorkerDetails(AdminWorkerStatus worker)
    {
        var details = new List<string>();
        if (worker.State == AdminWorkerState.SwitchedOff)
        {
            // Turned off in the settings: no schedule and no last run, only why it is off.
            if (!string.IsNullOrWhiteSpace(worker.Detail)) details.Add(worker.Detail);
            return details;
        }

        if (worker.ExpectedInterval is { } expected)
        {
            details.Add($"Runs every {FormatDuration(expected)}");
        }
        if (worker.LastSucceededUtc.HasValue)
        {
            details.Add($"Last good run: {FormatIst(worker.LastSucceededUtc.Value)}");
        }
        else if (worker.LastStartedUtc.HasValue)
        {
            details.Add($"Last started: {FormatIst(worker.LastStartedUtc.Value)}");
        }
        else
        {
            details.Add($"Started with PRISM: {FormatIst(worker.RegisteredUtc)}");
        }
        if (!string.IsNullOrWhiteSpace(worker.Detail))
        {
            details.Add(worker.State == AdminWorkerState.Failed
                ? $"What failed: {worker.Detail}"
                : worker.Detail);
        }
        return details;
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
        var detail = $"{index.EntryCount:N0} items searchable · {index.PendingItems:N0} waiting to be added.";
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
    internal const int RepositoryFileCheckLimit = DocRepoMissingFilesService.CheckLimit;

    private async Task<(AdminHealthStatus Status, string Summary, string? Detail)> CheckRepositoryFilesAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        // The same check (and cached result) as the Missing files page, so both show the same
        // number. Resolved here, inside the guarded check: the storage service creates its folder
        // when it starts, and a failure there must become this check's warning, not stop System
        // health and Home from loading. A forced refresh of System health checks the files again too.
        return await RepositoryFilesCheckAsync(_services.GetRequiredService<IDocRepoMissingFilesService>(), forceRefresh, cancellationToken);
    }

    internal static async Task<(AdminHealthStatus Status, string Summary, string? Detail)> RepositoryFilesCheckAsync(
        IDocRepoMissingFilesService missingFiles,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        // Not counting the trash, like the Missing files page's default view.
        var scan = await missingFiles.ScanAsync(includeDeleted: false, forceRefresh, cancellationToken);
        return RepositoryFilesResult(scan.CheckedCount, scan.MissingCount, scan.Capped);
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

        // The full readiness covers face recognition in Photos (detector, embedder and the rest);
        // CheckDetectorAsync only covers the classification assistant's detector.
        var recognition = await readiness.CheckAsync(cancellationToken);
        return FaceDetectorResult(recognition.IsEnabled, recognition.IsReady, recognition.Message);
    }

    internal static (AdminHealthStatus Status, string Summary, string? Detail) FaceDetectorResult(bool enabled, bool ready, string message)
    {
        if (!enabled)
        {
            return (AdminHealthStatus.Healthy, "Face recognition is switched off.", null);
        }

        return ready
            ? (AdminHealthStatus.Healthy, "Face recognition is ready.", null)
            : (AdminHealthStatus.Warning, "Face recognition in Photos is not working: its model is not ready.", message);
    }

    // Chat stops accepting files below a free-space floor; warn while there is still room to act.
    private AdminSystemHealthCheck? BuildChatUploadSpaceCheck(string path)
    {
        var attachments = _chat.CurrentValue.Attachments;
        if (!_chat.CurrentValue.Enabled || !attachments.Enabled) return null;

        try
        {
            // The same measurement chat uploads are refused by (it picks the mount that holds the
            // upload folder, not the system root).
            var diskSpace = _services.GetService<IChatDiskSpace>();
            if (diskSpace?.AvailableBytes(path) is not { } free) return null;
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

    // Worker times were shown in UTC while the rest of Administration uses IST; same format as IAdminTimeService.FormatIst.
    private static string FormatIst(DateTimeOffset utc) =>
        ProjectManagement.Infrastructure.IstClock.ToIst(utc).ToString(AdminTimeService.IstFormat, System.Globalization.CultureInfo.InvariantCulture) + " IST";

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
        if (duration.TotalDays >= 1) return Plural(duration.TotalDays, "day");
        if (duration.TotalHours >= 1) return Plural(duration.TotalHours, "hour");
        return Plural(Math.Max(1, Math.Round(duration.TotalMinutes)), "minute");
    }

    private static string Plural(double value, string unit)
    {
        var text = value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return text == "1" ? $"1 {unit}" : $"{text} {unit}s";
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
