using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Admin.MediaSources;

[Authorize(Roles = "Admin,HoD")]
public sealed class IndexModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly IFileSystemSourceHealthService _healthService;
    private readonly IFileSystemPathResolver _pathResolver;

    public IndexModel(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        IFileSystemSourceHealthService healthService,
        IFileSystemPathResolver pathResolver)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    [BindProperty]
    public SourceInput Input { get; set; } = new();

    public IReadOnlyList<SourceRow> Sources { get; private set; } = Array.Empty<SourceRow>();
    public int PendingJobs { get; private set; }
    public int FailedJobs { get; private set; }
    public bool CatalogueAvailable { get; private set; } = true;
    public bool ExternalSourcesEnabled => _options.IsExternalSourceFeatureEnabled;
    public bool IsEditing => Input.Id.HasValue;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? WarningMessage { get; set; }

    public async Task OnGetAsync(Guid? edit, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (!CatalogueAvailable || !edit.HasValue)
        {
            return;
        }

        var source = await _db.Sources.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == edit.Value, cancellationToken);
        if (source is null || source.SourceType != MediaLibrarySourceType.FileSystem)
        {
            WarningMessage = "The requested external source was not found.";
            return;
        }

        Input = SourceInput.FromEntity(source);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        NormalizeInput(Input);
        ValidateInput(Input);
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var normalizedKey = MediaSourceBootstrapper.NormalizeKey(
            string.IsNullOrWhiteSpace(Input.Key) ? Input.Name : Input.Key);
        var duplicate = await _db.Sources.AnyAsync(source =>
            source.Key == normalizedKey
            && (!Input.Id.HasValue || source.Id != Input.Id.Value), cancellationToken);
        if (duplicate)
        {
            ModelState.AddModelError("Input.Key", "Another media source already uses this key.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var existingRoots = await _db.Sources
            .AsNoTracking()
            .Where(source => source.SourceType == MediaLibrarySourceType.FileSystem
                             && !source.IsDeleted
                             && source.RootPath != null
                             && (!Input.Id.HasValue || source.Id != Input.Id.Value))
            .Select(source => source.RootPath!)
            .ToListAsync(cancellationToken);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (existingRoots.Any(root => string.Equals(root, Input.RootPath, pathComparison)))
        {
            ModelState.AddModelError("Input.RootPath", "This folder is already connected as another media source.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        MediaLibrarySource entity;
        if (Input.Id.HasValue)
        {
            entity = await _db.Sources.SingleOrDefaultAsync(source => source.Id == Input.Id.Value, cancellationToken)
                     ?? throw new InvalidOperationException("The media source no longer exists.");
            if (entity.SourceType != MediaLibrarySourceType.FileSystem || entity.IsConfigurationManaged)
            {
                return Forbid();
            }
        }
        else
        {
            entity = new MediaLibrarySource
            {
                Id = Guid.NewGuid(),
                SourceType = MediaLibrarySourceType.FileSystem,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ScanStatus = "Never",
                HealthStatus = "Unknown"
            };
            _db.Sources.Add(entity);
        }

        var extensions = ParseExtensions(Input.AllowedExtensions);
        entity.Key = normalizedKey;
        entity.Name = Input.Name.Trim();
        entity.RootPath = Input.RootPath;
        entity.IsEnabled = Input.IsEnabled;
        entity.IsVisibleInLibrary = Input.IsVisibleInLibrary;
        entity.IsReadOnly = true;
        entity.IncludeSubfolders = Input.IncludeSubfolders;
        entity.ScanIntervalMinutes = Input.ScanIntervalMinutes;
        entity.AllowedExtensionsJson = JsonSerializer.Serialize(extensions);
        entity.IsConfigurationManaged = false;
        entity.IsDeleted = false;
        entity.DisconnectedAtUtc = null;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var health = await _healthService.TestAsync(
            entity.RootPath,
            entity.IncludeSubfolders,
            extensions,
            cancellationToken);
        ApplyHealth(entity, health);

        if (entity.IsEnabled)
        {
            entity.ScanRequestedAtUtc = DateTimeOffset.UtcNow;
            entity.ScanStatus = health.IsReachable ? "Queued" : "Waiting for source";
        }

        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = health.IsReachable
            ? $"{entity.Name} was saved and a scan was queued."
            : $"{entity.Name} was saved. The folder is currently unavailable and will be retried.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        string path;
        bool includeSubfolders;
        string[] extensions;
        MediaLibrarySource? source = null;

        if (id.HasValue)
        {
            source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == id.Value, cancellationToken);
            if (source is null || source.SourceType != MediaLibrarySourceType.FileSystem)
            {
                return NotFound();
            }

            path = source.RootPath ?? string.Empty;
            includeSubfolders = source.IncludeSubfolders;
            extensions = ParseExtensions(source.AllowedExtensionsJson, json: true);
        }
        else
        {
            NormalizeInput(Input);
            ValidateInput(Input, validateName: false);
            if (!ModelState.IsValid)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            path = Input.RootPath;
            includeSubfolders = Input.IncludeSubfolders;
            extensions = ParseExtensions(Input.AllowedExtensions);
        }

        var health = await _healthService.TestAsync(path, includeSubfolders, extensions, cancellationToken);
        if (source is not null)
        {
            ApplyHealth(source, health);
            source.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (health.IsReachable)
        {
            StatusMessage = $"Connection successful ({health.PathKind}). {health.Message}";
        }
        else
        {
            WarningMessage = $"Connection failed ({health.PathKind}). {health.Message}";
        }

        if (id.HasValue)
        {
            return RedirectToPage(new { edit = id.Value });
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostScanAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        var source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.SourceType != MediaLibrarySourceType.FileSystem || source.IsDeleted)
        {
            return BadRequest();
        }

        if (!source.IsEnabled)
        {
            WarningMessage = "Enable the source before requesting a scan.";
            return RedirectToPage();
        }

        source.ScanRequestedAtUtc = DateTimeOffset.UtcNow;
        source.ScanStatus = "Queued";
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = $"A scan has been queued for {source.Name}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetStateAsync(
        Guid id,
        bool enabled,
        bool visible,
        CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        var source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.SourceType != MediaLibrarySourceType.FileSystem || source.IsConfigurationManaged)
        {
            return Forbid();
        }

        source.IsEnabled = enabled;
        source.IsVisibleInLibrary = visible;
        source.ScanStatus = enabled ? "Queued" : "Disabled";
        source.ScanRequestedAtUtc = enabled ? DateTimeOffset.UtcNow : null;
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = $"{source.Name} was updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ExternalSourcesEnabled)
        {
            WarningMessage = "External folders are disabled in production configuration.";
            return RedirectToPage();
        }

        var source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.SourceType != MediaLibrarySourceType.FileSystem || source.IsConfigurationManaged)
        {
            return Forbid();
        }

        source.IsEnabled = false;
        source.IsVisibleInLibrary = false;
        source.IsDeleted = true;
        source.DisconnectedAtUtc = DateTimeOffset.UtcNow;
        source.ScanRequestedAtUtc = null;
        source.ScanStatus = "Disconnected";
        source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = $"{source.Name} was disconnected. No original file was changed or deleted.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryFailedAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await _db.ProcessingJobs
            .Where(job => job.Status == MediaProcessingJobStatus.Failed
                          || job.Status == MediaProcessingJobStatus.DeadLetter)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, MediaProcessingJobStatus.Pending)
                .SetProperty(job => job.AttemptCount, 0)
                .SetProperty(job => job.AvailableAfterUtc, now)
                .SetProperty(job => job.LockedBy, (string?)null)
                .SetProperty(job => job.LockExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(job => job.FailureCode, (string?)null)
                .SetProperty(job => job.FailureMessage, (string?)null)
                .SetProperty(job => job.UpdatedAtUtc, now), cancellationToken);

        StatusMessage = $"{count} failed media processing job(s) were queued again.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Sources = await _db.Sources
                .AsNoTracking()
                .Where(source => !source.IsDeleted)
                .OrderBy(source => source.SourceType)
                .ThenBy(source => source.Name)
                .Select(source => new SourceRow(
                    source.Id,
                    source.Name,
                    source.Key,
                    source.SourceType,
                    source.IsEnabled,
                    source.IsVisibleInLibrary,
                    source.IsReadOnly,
                    source.IsConfigurationManaged,
                    source.RootPath,
                    source.ScanStatus,
                    source.HealthStatus,
                    source.HealthMessage,
                    source.IndexedAssetCount,
                    source.LastSuccessfulScanAtUtc,
                    source.LastHealthCheckedAtUtc,
                    source.LastError))
                .ToListAsync(cancellationToken);

            PendingJobs = await _db.ProcessingJobs.CountAsync(
                job => job.Status == MediaProcessingJobStatus.Pending
                       || job.Status == MediaProcessingJobStatus.Running,
                cancellationToken);
            FailedJobs = await _db.ProcessingJobs.CountAsync(
                job => job.Status == MediaProcessingJobStatus.Failed
                       || job.Status == MediaProcessingJobStatus.DeadLetter,
                cancellationToken);
        }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException or InvalidOperationException or TimeoutException)
        {
            CatalogueAvailable = false;
            Sources = Array.Empty<SourceRow>();
            WarningMessage = "The optional media catalogue is not available. Core PRISM Photos is unaffected.";
        }
    }

    private void ValidateInput(SourceInput input, bool validateName = true)
    {
        if (validateName && string.IsNullOrWhiteSpace(input.Name))
        {
            ModelState.AddModelError("Input.Name", "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(input.RootPath))
        {
            ModelState.AddModelError("Input.RootPath", "Enter a fully-qualified local or UNC folder path.");
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
                ModelState.AddModelError("Input.RootPath", ex.Message);
            }
        }

        if (input.ScanIntervalMinutes is < 1 or > 10080)
        {
            ModelState.AddModelError("Input.ScanIntervalMinutes", "Scan interval must be between 1 and 10080 minutes.");
        }

        if (ParseExtensions(input.AllowedExtensions).Length == 0)
        {
            ModelState.AddModelError("Input.AllowedExtensions", "Provide at least one file extension.");
        }
    }

    private static void NormalizeInput(SourceInput input)
    {
        input.Name = input.Name?.Trim() ?? string.Empty;
        input.Key = input.Key?.Trim() ?? string.Empty;
        input.RootPath = input.RootPath?.Trim() ?? string.Empty;
        input.AllowedExtensions = input.AllowedExtensions?.Trim() ?? string.Empty;
    }

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
            values = value.Split(new[] { ',', ';', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        return MediaSourceBootstrapper.NormalizeExtensions(values);
    }

    private static void ApplyHealth(MediaLibrarySource entity, FileSystemSourceHealth health)
    {
        entity.LastHealthCheckedAtUtc = health.CheckedAtUtc;
        entity.HealthStatus = health.IsReachable ? "Reachable" : "Unavailable";
        entity.HealthMessage = health.Message.Length <= 2048 ? health.Message : health.Message[..2048];
        if (health.IsReachable)
        {
            entity.LastError = null;
        }
    }

    public sealed class SourceInput
    {
        public Guid? Id { get; set; }

        [MaxLength(160)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(2048)]
        public string RootPath { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;
        public bool IsVisibleInLibrary { get; set; } = true;
        public bool IncludeSubfolders { get; set; } = true;
        public int ScanIntervalMinutes { get; set; } = 30;
        public string AllowedExtensions { get; set; } = string.Join(", ", MediaSourceDefaults.AllowedExtensions);

        public static SourceInput FromEntity(MediaLibrarySource source)
            => new()
            {
                Id = source.Id,
                Name = source.Name,
                Key = source.Key,
                RootPath = source.RootPath ?? string.Empty,
                IsEnabled = source.IsEnabled,
                IsVisibleInLibrary = source.IsVisibleInLibrary,
                IncludeSubfolders = source.IncludeSubfolders,
                ScanIntervalMinutes = source.ScanIntervalMinutes,
                AllowedExtensions = string.Join(", ", IndexModel.ParseExtensions(source.AllowedExtensionsJson, json: true))
            };
    }

    public sealed record SourceRow(
        Guid Id,
        string Name,
        string Key,
        MediaLibrarySourceType Type,
        bool IsEnabled,
        bool IsVisibleInLibrary,
        bool IsReadOnly,
        bool IsConfigurationManaged,
        string? RootPath,
        string Status,
        string HealthStatus,
        string? HealthMessage,
        long AssetCount,
        DateTimeOffset? LastSuccessfulScanAtUtc,
        DateTimeOffset? LastHealthCheckedAtUtc,
        string? LastError);
}
