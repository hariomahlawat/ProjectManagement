using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Features.MediaLibrary.Admin;

public sealed class MediaSourceAdminService : IMediaSourceAdminService
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly IFileSystemSourceHealthService _healthService;
    private readonly IFileSystemPathResolver _pathResolver;
    private readonly IMediaAdminAccessService _access;
    private readonly IAdminAuditService _audit;
    private readonly IAdminTimeService _time;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MediaSourceAdminService> _logger;

    public MediaSourceAdminService(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        IFileSystemSourceHealthService healthService,
        IFileSystemPathResolver pathResolver,
        IMediaAdminAccessService access,
        IAdminAuditService audit,
        IAdminTimeService time,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MediaSourceAdminService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaAdminCommandResult<MediaSourceAdminInput>> GetForEditAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaView, cancellationToken))
        {
            return Forbidden<MediaSourceAdminInput>();
        }

        var source = await _db.Sources.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsDeleted, cancellationToken);
        if (source is null || source.SourceType != MediaLibrarySourceType.FileSystem)
        {
            return MediaAdminCommandResult<MediaSourceAdminInput>.Failure(
                "The requested external source was not found.",
                MediaAdminErrorCodes.NotFound);
        }

        if (source.IsConfigurationManaged)
        {
            return MediaAdminCommandResult<MediaSourceAdminInput>.Failure(
                "This source is managed by configuration and cannot be edited here.",
                MediaAdminErrorCodes.SourceManagedByConfiguration);
        }

        return MediaAdminCommandResult<MediaSourceAdminInput>.Success(ToInput(source));
    }

    public async Task<MediaAdminCommandResult<Guid>> SaveAsync(
        MediaSourceAdminInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaConfigure, cancellationToken))
        {
            return Forbidden<Guid>();
        }

        if (!_options.IsExternalSourceFeatureEnabled)
        {
            return MediaAdminCommandResult<Guid>.Failure(
                "External folders are disabled in production configuration.",
                MediaAdminErrorCodes.SourceDisabled);
        }

        var validation = ValidateAndNormalize(input, validateName: true);
        if (validation.Count > 0)
        {
            return MediaAdminCommandResult<Guid>.Failure(
                "Correct the highlighted source details.",
                MediaAdminErrorCodes.InvalidInput,
                fieldErrors: validation);
        }

        try
        {
            var normalizedKey = MediaSourceBootstrapper.NormalizeKey(
                string.IsNullOrWhiteSpace(input.Key) ? input.Name : input.Key);
            var normalizedName = input.Name.ToUpperInvariant();

            var duplicateName = await _db.Sources.AsNoTracking().AnyAsync(source =>
                source.Name.ToUpper() == normalizedName
                && !source.IsDeleted
                && (!input.Id.HasValue || source.Id != input.Id.Value),
                cancellationToken);
            if (duplicateName)
            {
                return FieldFailure<Guid>(
                    "Input.Name",
                    "Another media source already uses this name.",
                    MediaAdminErrorCodes.DuplicateSource);
            }

            var duplicateKey = await _db.Sources.AsNoTracking().AnyAsync(source =>
                source.Key == normalizedKey
                && !source.IsDeleted
                && (!input.Id.HasValue || source.Id != input.Id.Value),
                cancellationToken);
            if (duplicateKey)
            {
                return FieldFailure<Guid>("Input.Key", "Another media source already uses this key.", MediaAdminErrorCodes.DuplicateSource);
            }

            var existingRoots = await _db.Sources.AsNoTracking()
                .Where(source => source.SourceType == MediaLibrarySourceType.FileSystem
                                 && !source.IsDeleted
                                 && source.RootPath != null
                                 && (!input.Id.HasValue || source.Id != input.Id.Value))
                .Select(source => source.RootPath!)
                .ToListAsync(cancellationToken);
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (existingRoots.Any(root => string.Equals(root, input.RootPath, pathComparison)))
            {
                return FieldFailure<Guid>("Input.RootPath", "This folder is already connected as another media source.", MediaAdminErrorCodes.DuplicateSource);
            }

            MediaLibrarySource entity;
            SourceAuditSnapshot? before = null;
            var created = !input.Id.HasValue;
            if (input.Id.HasValue)
            {
                entity = await _db.Sources.SingleOrDefaultAsync(
                             source => source.Id == input.Id.Value && !source.IsDeleted,
                             cancellationToken)
                         ?? throw new MediaSourceNotFoundException();

                if (entity.SourceType != MediaLibrarySourceType.FileSystem || entity.IsConfigurationManaged)
                {
                    return MediaAdminCommandResult<Guid>.Failure(
                        "This source cannot be edited from Media administration.",
                        MediaAdminErrorCodes.SourceManagedByConfiguration);
                }

                if (!MatchesConcurrency(entity, input.ConcurrencyToken))
                {
                    return ConcurrencyFailure<Guid>();
                }

                before = Snapshot(entity);
            }
            else
            {
                entity = new MediaLibrarySource
                {
                    Id = Guid.NewGuid(),
                    SourceType = MediaLibrarySourceType.FileSystem,
                    CreatedAtUtc = _time.UtcNow,
                    ScanStatus = "Never",
                    HealthStatus = "Unknown"
                };
                _db.Sources.Add(entity);
            }

            var extensions = ParseExtensions(input.AllowedExtensions);
            entity.Key = normalizedKey;
            entity.Name = input.Name;
            entity.RootPath = input.RootPath;
            entity.IsEnabled = input.IsEnabled;
            entity.IsVisibleInLibrary = input.IsVisibleInLibrary;
            entity.IsReadOnly = true;
            entity.IncludeSubfolders = input.IncludeSubfolders;
            entity.ScanIntervalMinutes = input.ScanIntervalMinutes;
            entity.AllowedExtensionsJson = JsonSerializer.Serialize(extensions);
            entity.IsConfigurationManaged = false;
            entity.IsDeleted = false;
            entity.DisconnectedAtUtc = null;
            entity.UpdatedAtUtc = _time.UtcNow;

            var health = await _healthService.TestAsync(
                entity.RootPath,
                entity.IncludeSubfolders,
                extensions,
                cancellationToken);
            ApplyHealth(entity, health);

            if (entity.IsEnabled)
            {
                entity.ScanRequestedAtUtc = _time.UtcNow;
                entity.ScanStatus = health.IsReachable ? "Queued" : "Waiting for source";
            }
            else
            {
                entity.ScanRequestedAtUtc = null;
                entity.ScanStatus = "Disabled";
            }

            await _db.SaveChangesAsync(cancellationToken);
            await AuditBestEffortAsync(new AdminAuditEntry(
                created ? "MediaSourceCreated" : "MediaSourceUpdated",
                "MediaLibrarySource",
                entity.Id.ToString(),
                Before: before,
                After: Snapshot(entity),
                Message: created ? "External media source created." : "External media source updated.",
                Origin: "/Admin/MediaSources"), cancellationToken);

            var message = health.IsReachable
                ? $"{entity.Name} was saved and a scan was queued."
                : $"{entity.Name} was saved. The folder is currently unavailable and will be retried.";
            return MediaAdminCommandResult<Guid>.Success(entity.Id, message);
        }
        catch (MediaSourceNotFoundException)
        {
            return MediaAdminCommandResult<Guid>.Failure(
                "The media source no longer exists.",
                MediaAdminErrorCodes.NotFound);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure<Guid>();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _logger.LogWarning(ex,
                "A concurrent media source create or update violated a unique constraint. TraceId={TraceId}",
                TraceId);
            return MediaAdminCommandResult<Guid>.Failure(
                "Another media source already uses the same stable key. Reload and review the current sources.",
                MediaAdminErrorCodes.DuplicateSource);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected<Guid>(ex, "saving media source");
        }
    }

    public async Task<MediaAdminCommandResult<MediaSourceTestResult>> TestAsync(
        Guid? sourceId,
        string? concurrencyToken,
        MediaSourceAdminInput? input,
        CancellationToken cancellationToken)
    {
        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaView, cancellationToken))
        {
            return Forbidden<MediaSourceTestResult>();
        }

        if (!_options.IsExternalSourceFeatureEnabled)
        {
            return MediaAdminCommandResult<MediaSourceTestResult>.Failure(
                "External folders are disabled in production configuration.",
                MediaAdminErrorCodes.SourceDisabled);
        }

        try
        {
            string path;
            bool includeSubfolders;
            string[] extensions;
            MediaLibrarySource? source = null;

            if (sourceId.HasValue)
            {
                source = await _db.Sources.SingleOrDefaultAsync(
                    item => item.Id == sourceId.Value && !item.IsDeleted,
                    cancellationToken);
                if (source is null || source.SourceType != MediaLibrarySourceType.FileSystem)
                {
                    return MediaAdminCommandResult<MediaSourceTestResult>.Failure(
                        "The media source was not found.",
                        MediaAdminErrorCodes.NotFound);
                }

                if (!string.IsNullOrWhiteSpace(concurrencyToken) && !MatchesConcurrency(source, concurrencyToken))
                {
                    return ConcurrencyFailure<MediaSourceTestResult>();
                }

                path = source.RootPath ?? string.Empty;
                includeSubfolders = source.IncludeSubfolders;
                extensions = ParseExtensions(source.AllowedExtensionsJson, json: true);
            }
            else
            {
                input ??= new MediaSourceAdminInput();
                var validation = ValidateAndNormalize(input, validateName: false);
                if (validation.Count > 0)
                {
                    return MediaAdminCommandResult<MediaSourceTestResult>.Failure(
                        "Correct the highlighted source details.",
                        MediaAdminErrorCodes.InvalidInput,
                        fieldErrors: validation);
                }

                path = input.RootPath;
                includeSubfolders = input.IncludeSubfolders;
                extensions = ParseExtensions(input.AllowedExtensions);
            }

            var health = await _healthService.TestAsync(path, includeSubfolders, extensions, cancellationToken);
            string? nextToken = concurrencyToken;
            if (source is not null && await _access.IsAuthorizedAsync(AdminPolicies.MediaConfigure, cancellationToken))
            {
                var before = Snapshot(source);
                ApplyHealth(source, health);
                source.UpdatedAtUtc = _time.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                nextToken = MediaSourceAdminConcurrency.Create(source);
                await AuditBestEffortAsync(new AdminAuditEntry(
                    "MediaSourceTested",
                    "MediaLibrarySource",
                    source.Id.ToString(),
                    Before: before,
                    After: Snapshot(source),
                    Outcome: health.IsReachable ? "Succeeded" : "Unavailable",
                    Message: health.IsReachable ? "Media source connection test succeeded." : "Media source connection test reported unavailable.",
                    Origin: "/Admin/MediaSources"), cancellationToken);
            }

            return MediaAdminCommandResult<MediaSourceTestResult>.Success(
                new MediaSourceTestResult(
                    health.IsReachable,
                    health.PathKind,
                    SafeMessage(health.Message, 1024),
                    sourceId,
                    nextToken),
                health.IsReachable
                    ? $"Connection successful ({health.PathKind}). {SafeMessage(health.Message, 512)}"
                    : $"Connection failed ({health.PathKind}). {SafeMessage(health.Message, 512)}");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure<MediaSourceTestResult>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected<MediaSourceTestResult>(ex, "testing media source");
        }
    }

    public Task<MediaAdminCommandResult> RequestScanAsync(
        Guid id,
        string concurrencyToken,
        CancellationToken cancellationToken) =>
        MutateSourceAsync(
            id,
            concurrencyToken,
            "MediaSourceScanRequested",
            source =>
            {
                if (!source.IsEnabled)
                {
                    return MediaAdminCommandResult.Failure(
                        "Enable the source before requesting a scan.",
                        MediaAdminErrorCodes.SourceDisabled);
                }

                source.ScanRequestedAtUtc = _time.UtcNow;
                source.ScanStatus = "Queued";
                return MediaAdminCommandResult.Success($"A scan has been queued for {source.Name}.");
            },
            cancellationToken,
            allowConfigurationManaged: true);

    public Task<MediaAdminCommandResult> SetStateAsync(
        Guid id,
        string concurrencyToken,
        bool enabled,
        bool visible,
        CancellationToken cancellationToken) =>
        MutateSourceAsync(
            id,
            concurrencyToken,
            enabled ? "MediaSourceActivated" : "MediaSourceDeactivated",
            source =>
            {
                source.IsEnabled = enabled;
                source.IsVisibleInLibrary = visible;
                source.ScanStatus = enabled ? "Queued" : "Disabled";
                source.ScanRequestedAtUtc = enabled ? _time.UtcNow : null;
                return MediaAdminCommandResult.Success($"{source.Name} was updated.");
            },
            cancellationToken);

    public Task<MediaAdminCommandResult> DisconnectAsync(
        Guid id,
        string concurrencyToken,
        CancellationToken cancellationToken) =>
        MutateSourceAsync(
            id,
            concurrencyToken,
            "MediaSourceDisconnected",
            source =>
            {
                source.IsEnabled = false;
                source.IsVisibleInLibrary = false;
                source.IsDeleted = true;
                source.DisconnectedAtUtc = _time.UtcNow;
                source.ScanRequestedAtUtc = null;
                source.ScanStatus = "Disconnected";
                return MediaAdminCommandResult.Success(
                    $"{source.Name} was disconnected. No original file was changed or deleted.");
            },
            cancellationToken);

    private async Task<MediaAdminCommandResult> MutateSourceAsync(
        Guid id,
        string concurrencyToken,
        string action,
        Func<MediaLibrarySource, MediaAdminCommandResult> mutate,
        CancellationToken cancellationToken,
        bool allowConfigurationManaged = false)
    {
        if (!await _access.IsAuthorizedAsync(AdminPolicies.MediaConfigure, cancellationToken))
        {
            return MediaAdminCommandResult.Failure(
                "You are not authorised to change media sources.",
                MediaAdminErrorCodes.Forbidden);
        }

        if (!_options.IsExternalSourceFeatureEnabled)
        {
            return MediaAdminCommandResult.Failure(
                "External folders are disabled in production configuration.",
                MediaAdminErrorCodes.SourceDisabled);
        }

        try
        {
            var source = await _db.Sources.SingleOrDefaultAsync(
                item => item.Id == id && !item.IsDeleted,
                cancellationToken);
            if (source is null)
            {
                return MediaAdminCommandResult.Failure(
                    "The media source was not found.",
                    MediaAdminErrorCodes.NotFound);
            }

            if (source.SourceType != MediaLibrarySourceType.FileSystem
                || (source.IsConfigurationManaged && !allowConfigurationManaged))
            {
                return MediaAdminCommandResult.Failure(
                    "This source is managed by configuration and cannot be changed here.",
                    MediaAdminErrorCodes.SourceManagedByConfiguration);
            }

            if (!MatchesConcurrency(source, concurrencyToken))
            {
                return ConcurrencyFailure();
            }

            var before = Snapshot(source);
            var result = mutate(source);
            if (!result.Succeeded)
            {
                return result;
            }

            source.UpdatedAtUtc = _time.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await AuditBestEffortAsync(new AdminAuditEntry(
                action,
                "MediaLibrarySource",
                source.Id.ToString(),
                Before: before,
                After: Snapshot(source),
                Message: result.UserMessage,
                Origin: "/Admin/MediaSources"), cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unexpected(ex, action);
        }
    }

    private IReadOnlyDictionary<string, string[]> ValidateAndNormalize(
        MediaSourceAdminInput input,
        bool validateName)
    {
        input.Name = CollapseWhitespace(input.Name);
        input.Key = input.Key?.Trim() ?? string.Empty;
        input.RootPath = input.RootPath?.Trim() ?? string.Empty;
        input.AllowedExtensions = input.AllowedExtensions?.Trim() ?? string.Empty;

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var messages))
            {
                messages = new List<string>();
                errors[key] = messages;
            }
            messages.Add(message);
        }

        if (validateName && string.IsNullOrWhiteSpace(input.Name))
        {
            Add("Input.Name", "Name is required.");
        }
        else if (input.Name.Length > 160)
        {
            Add("Input.Name", "Name cannot exceed 160 characters.");
        }

        if (input.Key.Length > 64)
        {
            Add("Input.Key", "Stable key cannot exceed 64 characters.");
        }

        if (string.IsNullOrWhiteSpace(input.RootPath))
        {
            Add("Input.RootPath", "Enter a fully-qualified local or UNC folder path.");
        }
        else
        {
            try
            {
                input.RootPath = _pathResolver.ResolveRoot(input.RootPath);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or ArgumentException
                                       or NotSupportedException
                                       or PathTooLongException)
            {
                Add("Input.RootPath", SafeMessage(ex.Message, 512));
            }
        }

        if (input.ScanIntervalMinutes is < 1 or > 10080)
        {
            Add("Input.ScanIntervalMinutes", "Scan interval must be between 1 and 10080 minutes.");
        }

        if (ParseExtensions(input.AllowedExtensions).Length == 0)
        {
            Add("Input.AllowedExtensions", "Provide at least one file extension.");
        }

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.Distinct().ToArray(), StringComparer.Ordinal);
    }

    private static MediaSourceAdminInput ToInput(MediaLibrarySource source) => new()
    {
        Id = source.Id,
        ConcurrencyToken = MediaSourceAdminConcurrency.Create(source),
        Name = source.Name,
        Key = source.Key,
        RootPath = source.RootPath ?? string.Empty,
        IsEnabled = source.IsEnabled,
        IsVisibleInLibrary = source.IsVisibleInLibrary,
        IncludeSubfolders = source.IncludeSubfolders,
        ScanIntervalMinutes = source.ScanIntervalMinutes,
        AllowedExtensions = string.Join(", ", ParseExtensions(source.AllowedExtensionsJson, json: true))
    };

    private static string[] ParseExtensions(string value, bool json = false)
    {
        IEnumerable<string> values;
        if (json)
        {
            try
            {
                values = JsonSerializer.Deserialize<string[]>(value) ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                values = Array.Empty<string>();
            }
        }
        else
        {
            values = (value ?? string.Empty).Split(
                new[] { ',', ';', '\r', '\n', ' ' },
                StringSplitOptions.RemoveEmptyEntries);
        }

        return MediaSourceBootstrapper.NormalizeExtensions(values);
    }

    private static void ApplyHealth(MediaLibrarySource entity, FileSystemSourceHealth health)
    {
        entity.LastHealthCheckedAtUtc = health.CheckedAtUtc;
        entity.HealthStatus = health.IsReachable ? "Reachable" : "Unavailable";
        entity.HealthMessage = SafeMessage(health.Message, 2048);
        if (health.IsReachable)
        {
            entity.LastError = null;
        }
    }

    private static bool MatchesConcurrency(MediaLibrarySource source, string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && string.Equals(MediaSourceAdminConcurrency.Create(source), token, StringComparison.Ordinal);

    private static string CollapseWhitespace(string? value) =>
        string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string SafeMessage(string? value, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private SourceAuditSnapshot Snapshot(MediaLibrarySource source) => new(
        source.Name,
        source.Key,
        source.IsEnabled,
        source.IsVisibleInLibrary,
        source.IncludeSubfolders,
        source.ScanIntervalMinutes,
        source.ScanStatus,
        source.HealthStatus,
        source.IsDeleted,
        PathKind: SafePathKind(source.RootPath),
        PathFingerprint: Fingerprint(source.RootPath));

    private string SafePathKind(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? "Not configured" : _pathResolver.DescribePathKind(path);
        }
        catch
        {
            return "File-system folder";
        }
    }

    private static string? Fingerprint(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.Trim()));
        return Convert.ToHexString(hash)[..16];
    }

    private async Task AuditBestEffortAsync(AdminAuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.RecordAsync(entry, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Media administration mutation {Action} succeeded but its audit event could not be written for {EntityId}.",
                entry.Action,
                entry.EntityId);
        }
    }

    private string? TraceId => _httpContextAccessor.HttpContext?.TraceIdentifier;

    private MediaAdminCommandResult Unexpected(Exception ex, string operation)
    {
        _logger.LogError(ex, "Unexpected failure while {Operation}. TraceId={TraceId}", operation, TraceId);
        return MediaAdminCommandResult.Failure(
            $"The operation could not be completed. Reference {TraceId ?? "unavailable"}.",
            MediaAdminErrorCodes.UnexpectedFailure,
            TraceId);
    }

    private MediaAdminCommandResult<T> Unexpected<T>(Exception ex, string operation)
    {
        _logger.LogError(ex, "Unexpected failure while {Operation}. TraceId={TraceId}", operation, TraceId);
        return MediaAdminCommandResult<T>.Failure(
            $"The operation could not be completed. Reference {TraceId ?? "unavailable"}.",
            MediaAdminErrorCodes.UnexpectedFailure,
            TraceId);
    }

    private static MediaAdminCommandResult<T> Forbidden<T>() =>
        MediaAdminCommandResult<T>.Failure(
            "You are not authorised to perform this media administration operation.",
            MediaAdminErrorCodes.Forbidden);

    private static MediaAdminCommandResult<T> ConcurrencyFailure<T>() =>
        MediaAdminCommandResult<T>.Failure(
            "This media source was changed by another administrator. Reload and review the latest values.",
            MediaAdminErrorCodes.ConcurrencyConflict);

    private static MediaAdminCommandResult ConcurrencyFailure() =>
        MediaAdminCommandResult.Failure(
            "This media source was changed by another administrator. Reload and review the latest values.",
            MediaAdminErrorCodes.ConcurrencyConflict);

    private static MediaAdminCommandResult<T> FieldFailure<T>(
        string field,
        string message,
        string errorCode) =>
        MediaAdminCommandResult<T>.Failure(
            "Correct the highlighted source details.",
            errorCode,
            fieldErrors: new Dictionary<string, string[]> { { field, new string[] { message } } });

    private sealed record SourceAuditSnapshot(
        string Name,
        string Key,
        bool IsEnabled,
        bool IsVisibleInLibrary,
        bool IncludeSubfolders,
        int ScanIntervalMinutes,
        string ScanStatus,
        string HealthStatus,
        bool IsDeleted,
        string PathKind,
        string? PathFingerprint);

    private sealed class MediaSourceNotFoundException : Exception { }
}
