using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Imaging;
using ProjectManagement.Services.Storage;
using SkiaSharp;

namespace ProjectManagement.Features.Collage;

/// <summary>What a change to the stickers came to: done, or the reason in plain words.</summary>
public sealed record CollageStickerOutcome(bool Succeeded, string Message, CollageSticker? Sticker = null)
{
    public static CollageStickerOutcome Done(string message, CollageSticker? sticker = null) => new(true, message, sticker);

    public static CollageStickerOutcome Refused(string message) => new(false, message);
}

/// <summary>
/// The "Crests and logos" stickers of Collage Studio: uploading, naming, ordering, retiring and
/// restoring them, and finding their pictures. See docs/ux/collage-studio-library-and-unit-stickers.md.
/// </summary>
public interface ICollageStickerService
{
    /// <summary>The stickers shown in the picker, in order.</summary>
    Task<IReadOnlyList<CollageSticker>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Every sticker, the active ones first and then the retired ones, for administration.</summary>
    Task<IReadOnlyList<CollageSticker>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<CollageSticker?> FindAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The absolute path of a sticker's PNG, or null when the file is missing.</summary>
    string? PicturePathOf(CollageSticker sticker);

    Task<CollageStickerOutcome> AddAsync(Stream content, string fileName, string name, string? searchWords, string? actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refuses an upload whose request is over the page's size limit, before it is read, with the
    /// same message and audit entry as a picture over 5 MB.
    /// </summary>
    Task<CollageStickerOutcome> RefuseOversizeRequestAsync(long requestBytes, string? actorUserId, CancellationToken cancellationToken = default);

    Task<CollageStickerOutcome> RenameAsync(int id, string name, string? searchWords, string? actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Moves an active sticker one place earlier (<paramref name="up"/>) or later.</summary>
    Task<CollageStickerOutcome> MoveAsync(int id, bool up, string? actorUserId, CancellationToken cancellationToken = default);

    Task<CollageStickerOutcome> RetireAsync(int id, string? actorUserId, CancellationToken cancellationToken = default);

    Task<CollageStickerOutcome> RestoreAsync(int id, string? actorUserId, CancellationToken cancellationToken = default);
}

public sealed class CollageStickerService : ICollageStickerService
{
    /// <summary>The folder under the upload root that holds the sticker pictures.</summary>
    public const string Folder = "collage-stickers";

    public const long MaxFileBytes = 5_000_000;
    public const int MaxActive = 60;
    public const int MaxSide = 1024;

    // Enough for a large scanned crest; a bigger picture is refused before it is decoded.
    internal const long MaxDecodePixels = 40_000_000;

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    private const string AuditOrigin = "Admin.CollageStickers";
    private const string LockKey = "collage-stickers";

    private readonly ApplicationDbContext _db;
    private readonly IUploadRootProvider _uploadRoot;
    private readonly IUploadPathResolver _paths;
    private readonly IAdminAuditService _audit;
    private readonly IClock _clock;
    private readonly IVirusScanner? _virusScanner;
    private readonly ILogger<CollageStickerService> _logger;
    private readonly IMemoryCache? _cache;
    private readonly IServiceScopeFactory? _scopes;

    /// <summary>What the administrator is told when the upload scanner refuses or fails on a picture.</summary>
    public const string ScanRefusedMessage = "The picture did not pass the upload check, so it was not added. Try another picture, or ask the system administrator.";

    public const string TooLargeMessage = "The picture is larger than 5 MB. Choose a smaller one.";
    public const string WrongTypeMessage = "Use a PNG, JPEG or WebP picture. A PNG with a clear background looks best.";
    public const string TooManyPixelsMessage = "The picture has too many pixels. Choose a smaller one.";
    public const string UnreadableMessage = "The picture could not be read. Try saving it again as a PNG.";

    /// <summary>A missing sticker file is named in the log at most once in this time, not on every request for it.</summary>
    internal static readonly TimeSpan MissingFileWarningInterval = TimeSpan.FromHours(1);

    /// <summary>A picture file is only called left over once it is this old, so an upload still saving is never named.</summary>
    internal static readonly TimeSpan LeftoverAge = TimeSpan.FromHours(1);

    public CollageStickerService(
        ApplicationDbContext db,
        IUploadRootProvider uploadRoot,
        IUploadPathResolver paths,
        IAdminAuditService audit,
        IClock clock,
        IVirusScanner? virusScanner = null,
        ILogger<CollageStickerService>? logger = null,
        IMemoryCache? cache = null,
        IServiceScopeFactory? scopes = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _uploadRoot = uploadRoot ?? throw new ArgumentNullException(nameof(uploadRoot));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _virusScanner = virusScanner;
        _logger = logger ?? NullLogger<CollageStickerService>.Instance;
        _cache = cache;
        _scopes = scopes;
    }

    public async Task<IReadOnlyList<CollageSticker>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await _db.CollageStickers.AsNoTracking()
            .Where(s => !s.IsRetired)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CollageSticker>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await _db.CollageStickers.AsNoTracking()
            .OrderBy(s => s.IsRetired).ThenBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

    public Task<CollageSticker?> FindAsync(int id, CancellationToken cancellationToken = default) =>
        _db.CollageStickers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public string? PicturePathOf(CollageSticker sticker)
    {
        ArgumentNullException.ThrowIfNull(sticker);
        try
        {
            var path = _paths.ToAbsolute(sticker.StorageKey);
            if (File.Exists(path)) return path;
            if (ShouldWarnMissing(sticker))
            {
                _logger.LogWarning(
                    "Collage sticker {StickerId} ('{StickerName}') has no picture file at {StorageKey}; collages that use it show nothing in its place. This is logged at most once an hour for each sticker.",
                    sticker.Id, sticker.Name, sticker.StorageKey);
            }
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Collage sticker {StickerId} has a storage key that is not a valid upload path: {StorageKey}.", sticker.Id, sticker.StorageKey);
            return null;
        }
    }

    /// <summary>
    /// True when this sticker's missing file has not been named in the log within the last hour.
    /// Every collage that uses the sticker asks for its picture, so without this one missing file
    /// would fill the log. Without a cache (some tests) every request is logged.
    /// </summary>
    private bool ShouldWarnMissing(CollageSticker sticker)
    {
        if (_cache is null) return true;
        var key = (typeof(CollageStickerService), "missing-file", sticker.Id, sticker.StorageKey);
        if (_cache.TryGetValue(key, out _)) return false;
        _cache.Set(key, true, MissingFileWarningInterval);
        return true;
    }

    public async Task<CollageStickerOutcome> AddAsync(
        Stream content,
        string fileName,
        string name,
        string? searchWords,
        string? actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var cleanName = CleanName(name);
        if (cleanName is null) return CollageStickerOutcome.Refused($"Give the sticker a name of up to {CollageSticker.NameMaxLength} letters.");
        var words = CleanWords(searchWords);
        if (words.Length > CollageSticker.SearchWordsMaxLength) return CollageStickerOutcome.Refused($"Keep the search words to {CollageSticker.SearchWordsMaxLength} letters.");

        var active = await _db.CollageStickers.CountAsync(s => !s.IsRetired, cancellationToken);
        if (active >= MaxActive) return CollageStickerOutcome.Refused($"There are already {MaxActive} stickers in use. Retire one first.");

        // Read at most one byte more than allowed, so a large upload is refused without holding it.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxFileBytes)
            {
                return await RefuseAuditedAsync(TooLargeMessage, "The picture is larger than 5 MB.", cleanName, fileName, buffer.Length, actorUserId, cancellationToken);
            }
        }
        if (buffer.Length == 0) return CollageStickerOutcome.Refused("Choose a picture to upload.");

        var bytes = new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);
        var info = PrismImageCodec.Identify(bytes);
        if (info is null || !AllowedTypes.Contains(info.MimeType))
        {
            return await RefuseAuditedAsync(WrongTypeMessage, "The file is not a PNG, JPEG or WebP picture.", cleanName, fileName, buffer.Length, actorUserId, cancellationToken);
        }
        if ((long)info.Width * info.Height > MaxDecodePixels)
        {
            return await RefuseAuditedAsync(TooManyPixelsMessage, $"The picture is {info.Width} by {info.Height} pixels, over the limit of {MaxDecodePixels:N0}.", cleanName, fileName, buffer.Length, actorUserId, cancellationToken);
        }

        if (_virusScanner is not null)
        {
            buffer.Position = 0;
            try
            {
                await _virusScanner.ScanAsync(buffer, fileName ?? "sticker", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A scanner says no by throwing, and a broken scanner throws too. Either way nothing
                // is stored: the administrator is told plainly, and the refusal is in the audit log.
                _logger.LogWarning(ex, "Collage sticker upload {FileName} ('{StickerName}') was refused by the upload scanner.", fileName, cleanName);
                await _audit.RecordAsync(new AdminAuditEntry(
                    "CollageStickerRefused",
                    nameof(CollageSticker),
                    EntityId: null,
                    After: new { Name = cleanName, FileName = Path.GetFileName(fileName ?? string.Empty), Bytes = buffer.Length },
                    Reason: "The upload scanner refused the picture or could not check it.",
                    Outcome: "Refused",
                    Level: "Warning",
                    ActorUserId: actorUserId,
                    Origin: AuditOrigin,
                    Message: $"A collage sticker picture for '{cleanName}' was refused by the upload check."), cancellationToken);
                return CollageStickerOutcome.Refused(ScanRefusedMessage);
            }
        }

        // The stored picture is drawn afresh from the pixels: nothing the uploader sent is kept.
        byte[] png;
        int width;
        int height;
        try
        {
            using var decoded = PrismImageCodec.DecodeOriented(bytes, MaxDecodePixels);
            using var sized = decoded.Width > MaxSide || decoded.Height > MaxSide
                ? PrismImageCodec.ResizeToFit(decoded, MaxSide, MaxSide)
                : decoded.Copy();
            png = PrismImageCodec.EncodePng(sized);
            width = sized.Width;
            height = sized.Height;
        }
        catch (PrismImageException)
        {
            return await RefuseAuditedAsync(UnreadableMessage, "The picture could not be read.", cleanName, fileName, buffer.Length, actorUserId, cancellationToken);
        }

        var folder = Path.Combine(_uploadRoot.RootPath, Folder);
        Directory.CreateDirectory(folder);
        var fileStem = Guid.NewGuid().ToString("N");
        var finalPath = Path.Combine(folder, fileStem + ".png");
        var tempPath = Path.Combine(folder, fileStem + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(tempPath, png, cancellationToken);
            File.Move(tempPath, finalPath);
        }
        catch
        {
            // Cancelled, disk full or the move refused: nothing half-written is left behind.
            TryDelete(tempPath);
            TryDelete(finalPath);
            throw;
        }

        // If the process stops before the row below is saved, this line names the file left behind;
        // the next upload's check (LogLeftoverFiles) names it again once it is an hour old.
        _logger.LogInformation("Collage sticker picture {StorageKey} written for '{StickerName}'; saving its record.", $"{Folder}/{fileStem}.png", cleanName);

        var storageKey = $"{Folder}/{fileStem}.png";
        var commitSent = false;
        CollageStickerOutcome outcome;
        try
        {
            outcome = await ChangeAsync(async () =>
            {
                // Counted again under the lock: the count above only saved decoding a refused upload.
                if (await _db.CollageStickers.CountAsync(s => !s.IsRetired, cancellationToken) >= MaxActive)
                {
                    return CollageStickerOutcome.Refused($"There are already {MaxActive} stickers in use. Retire one first.");
                }

                var now = _clock.UtcNow;
                var sticker = new CollageSticker
                {
                    Name = cleanName,
                    SearchWords = words.Length == 0 ? null : words,
                    StorageKey = storageKey,
                    Width = width,
                    Height = height,
                    Version = 1,
                    SortOrder = await NextSortOrderAsync(cancellationToken),
                    CreatedByUserId = actorUserId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _db.CollageStickers.Add(sticker);
                await _db.SaveChangesAsync(cancellationToken);
                await RecordAsync("CollageStickerAdded", sticker, null, actorUserId, $"Added the collage sticker '{sticker.Name}'.", cancellationToken);
                return CollageStickerOutcome.Done($"Added '{sticker.Name}'.", sticker);
            }, cancellationToken, onCommitSending: () => commitSent = true);
        }
        catch (Exception ex)
        {
            if (!commitSent)
            {
                // The transaction was never committed, so nothing was saved and the picture has
                // nothing pointing at it.
                TryDelete(finalPath);
            }
            else
            {
                // COMMIT was sent and then failed, perhaps only on the way back: the row may be
                // saved. The file goes only when a fresh look shows no row uses it.
                await DeleteIfUnsavedAsync(storageKey, finalPath, ex);
            }
            throw;
        }

        if (!outcome.Succeeded)
        {
            TryDelete(finalPath);
            return outcome;
        }

        await LogLeftoverFilesAsync(folder);
        return outcome;
    }

    /// <summary>
    /// After a failed COMMIT: deletes the new picture only when a fresh database context shows that
    /// no sticker row uses it. When the row is there, or the check itself fails, the file is kept
    /// and logged; a kept file nothing uses is named later by <see cref="LogLeftoverFilesAsync"/>.
    /// </summary>
    private async Task DeleteIfUnsavedAsync(string storageKey, string finalPath, Exception failure)
    {
        bool? saved = null;
        if (_scopes is not null)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                saved = await db.CollageStickers.AsNoTracking().AnyAsync(s => s.StorageKey == storageKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Collage sticker picture {StorageKey}: could not check whether its record was saved after the commit failed.", storageKey);
            }
        }

        if (saved == false)
        {
            TryDelete(finalPath);
            return;
        }

        _logger.LogWarning(
            failure,
            saved == true
                ? "Collage sticker picture {StorageKey} was kept: the commit reported a failure, but its record was saved."
                : "Collage sticker picture {StorageKey} was kept: the commit reported a failure and it is not known whether its record was saved. If no sticker uses it, a later upload names it in the log.",
            storageKey);
    }

    public Task<CollageStickerOutcome> RefuseOversizeRequestAsync(long requestBytes, string? actorUserId, CancellationToken cancellationToken = default) =>
        RefuseAuditedAsync(TooLargeMessage, $"The upload ({requestBytes:N0} bytes) is over the page's request limit.", null, null, requestBytes, actorUserId, cancellationToken);

    /// <summary>A refused upload: the plain message for the administrator and a Warning entry in the audit log.</summary>
    private async Task<CollageStickerOutcome> RefuseAuditedAsync(string message, string reason, string? name, string? fileName, long bytes, string? actorUserId, CancellationToken cancellationToken)
    {
        await _audit.RecordAsync(new AdminAuditEntry(
            "CollageStickerRefused",
            nameof(CollageSticker),
            EntityId: null,
            After: new { Name = name, FileName = Path.GetFileName(fileName ?? string.Empty), Bytes = bytes },
            Reason: reason,
            Outcome: "Refused",
            Level: "Warning",
            ActorUserId: actorUserId,
            Origin: AuditOrigin,
            Message: string.IsNullOrEmpty(name)
                ? "A collage sticker picture was refused: " + reason
                : $"A collage sticker picture for '{name}' was refused: {reason}"), cancellationToken);
        return CollageStickerOutcome.Refused(message);
    }

    /// <summary>
    /// Names, in the log, picture files in the sticker folder that no sticker uses and that are
    /// over an hour old: what an upload leaves when the process stops between writing the file and
    /// saving its row. Nothing is deleted. It runs after a successful upload, which is rare, and
    /// never fails it.
    /// </summary>
    internal async Task LogLeftoverFilesAsync(string folder)
    {
        try
        {
            var cutoff = _clock.UtcNow.UtcDateTime - LeftoverAge;
            var old = Directory.EnumerateFiles(folder)
                .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Where(path => File.GetLastWriteTimeUtc(path) < cutoff)
                .Select(path => Path.GetFileName(path))
                .ToList();
            if (old.Count == 0) return;

            var keys = await _db.CollageStickers.AsNoTracking().Select(s => s.StorageKey).ToListAsync(CancellationToken.None);
            var used = new HashSet<string>(keys.Select(key => Path.GetFileName(key)), StringComparer.OrdinalIgnoreCase);
            var leftovers = old.Where(name => !used.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();
            if (leftovers.Count == 0) return;

            _logger.LogWarning(
                "Collage sticker folder {Folder} holds {Count} picture file(s) that no sticker uses: {Files}. An upload stopped before its record was saved; the files can be deleted.",
                folder, leftovers.Count, string.Join(", ", leftovers.Take(20)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        {
            _logger.LogDebug(ex, "Collage sticker folder {Folder} could not be checked for left-over files.", folder);
        }
    }

    public async Task<CollageStickerOutcome> RenameAsync(int id, string name, string? searchWords, string? actorUserId, CancellationToken cancellationToken = default)
    {
        var cleanName = CleanName(name);
        if (cleanName is null) return CollageStickerOutcome.Refused($"Give the sticker a name of up to {CollageSticker.NameMaxLength} letters.");
        var words = CleanWords(searchWords);
        if (words.Length > CollageSticker.SearchWordsMaxLength) return CollageStickerOutcome.Refused($"Keep the search words to {CollageSticker.SearchWordsMaxLength} letters.");

        return await ChangeAsync(async () =>
        {
            var sticker = await _db.CollageStickers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (sticker is null) return CollageStickerOutcome.Refused("That sticker is no longer there.");
            var before = Snapshot(sticker);
            var oldName = sticker.Name;
            sticker.Name = cleanName;
            sticker.SearchWords = words.Length == 0 ? null : words;
            sticker.UpdatedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await RecordAsync("CollageStickerUpdated", sticker, before, actorUserId, $"Renamed the collage sticker '{oldName}' to '{sticker.Name}'.", cancellationToken);
            return CollageStickerOutcome.Done("Saved.", sticker);
        }, cancellationToken);
    }

    public Task<CollageStickerOutcome> MoveAsync(int id, bool up, string? actorUserId, CancellationToken cancellationToken = default) =>
        ChangeAsync(async () =>
        {
            var active = await _db.CollageStickers
                .Where(s => !s.IsRetired)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToListAsync(cancellationToken);
            var index = active.FindIndex(s => s.Id == id);
            if (index < 0) return CollageStickerOutcome.Refused("That sticker is no longer in the picker.");
            var other = up ? index - 1 : index + 1;
            if (other < 0 || other >= active.Count) return CollageStickerOutcome.Done("It is already there.", active[index]);

            var oldOrder = active.ToDictionary(s => s.Id, s => s.SortOrder);
            (active[index], active[other]) = (active[other], active[index]);
            // Numbered afresh, so equal or missing numbers from older rows cannot keep two in one place.
            var now = _clock.UtcNow;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].SortOrder == i + 1) continue;
                active[i].SortOrder = i + 1;
                active[i].UpdatedAtUtc = now;
            }
            await _db.SaveChangesAsync(cancellationToken);
            var moved = active[other];

            // One entry for the move, with every sticker whose place changed (the renumbering
            // included): the old places in Before and the new ones in After.
            var changed = active.Where(s => oldOrder[s.Id] != s.SortOrder).ToList();
            await _audit.RecordAsync(new AdminAuditEntry(
                "CollageStickerReordered",
                nameof(CollageSticker),
                moved.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Before: new { Order = changed.Select(s => new { s.Id, s.Name, SortOrder = oldOrder[s.Id] }).ToArray() },
                After: new { Order = changed.Select(s => new { s.Id, s.Name, s.SortOrder }).ToArray() },
                ActorUserId: actorUserId,
                Origin: AuditOrigin,
                Message: $"Moved the collage sticker '{moved.Name}' {(up ? "earlier" : "later")} in the picker."), cancellationToken);
            return CollageStickerOutcome.Done("Moved.", moved);
        }, cancellationToken);

    public Task<CollageStickerOutcome> RetireAsync(int id, string? actorUserId, CancellationToken cancellationToken = default) =>
        ChangeAsync(async () =>
        {
            var sticker = await _db.CollageStickers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (sticker is null) return CollageStickerOutcome.Refused("That sticker is no longer there.");
            if (sticker.IsRetired) return CollageStickerOutcome.Done("It is already retired.", sticker);
            var before = Snapshot(sticker);
            sticker.IsRetired = true;
            sticker.UpdatedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await RecordAsync("CollageStickerRetired", sticker, before, actorUserId, $"Retired the collage sticker '{sticker.Name}'. Collages that use it keep it.", cancellationToken);
            return CollageStickerOutcome.Done($"Retired '{sticker.Name}'. Collages that already use it keep it.", sticker);
        }, cancellationToken);

    public Task<CollageStickerOutcome> RestoreAsync(int id, string? actorUserId, CancellationToken cancellationToken = default) =>
        ChangeAsync(async () =>
        {
            var sticker = await _db.CollageStickers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (sticker is null) return CollageStickerOutcome.Refused("That sticker is no longer there.");
            if (!sticker.IsRetired) return CollageStickerOutcome.Done("It is already in the picker.", sticker);
            if (await _db.CollageStickers.CountAsync(s => !s.IsRetired, cancellationToken) >= MaxActive)
            {
                return CollageStickerOutcome.Refused($"There are already {MaxActive} stickers in use. Retire one first.");
            }
            var before = Snapshot(sticker);
            sticker.IsRetired = false;
            sticker.SortOrder = await NextSortOrderAsync(cancellationToken);
            sticker.UpdatedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await RecordAsync("CollageStickerRestored", sticker, before, actorUserId, $"Put the collage sticker '{sticker.Name}' back in the picker.", cancellationToken);
            return CollageStickerOutcome.Done($"'{sticker.Name}' is back in the picker, at the end.", sticker);
        }, cancellationToken);

    /// <summary>
    /// Runs one change to the stickers and its audit entry as one database transaction, so a
    /// change is never kept without its entry (the audit service saves through the same context).
    /// On PostgreSQL every change first takes one transaction-scoped advisory lock, so two
    /// administrators cannot both find room for the sixtieth sticker or renumber the picker at once.
    /// A refused change has written nothing; a failure rolls everything back.
    /// </summary>
    private async Task<CollageStickerOutcome> ChangeAsync(Func<Task<CollageStickerOutcome>> change, CancellationToken cancellationToken, Action? onCommitSending = null)
    {
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            if (_db.Database.IsNpgsql())
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({LockKey}))", cancellationToken);
            }

            var outcome = await change();
            if (transaction is not null)
            {
                // From here a failure no longer proves that nothing was saved.
                onCommitSending?.Invoke();
                await transaction.CommitAsync(cancellationToken);
            }
            return outcome;
        }
        catch
        {
            // Rolled back: forget the unsaved edits so this request cannot save them later.
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<int> NextSortOrderAsync(CancellationToken cancellationToken) =>
        (await _db.CollageStickers.Where(s => !s.IsRetired).MaxAsync(s => (int?)s.SortOrder, cancellationToken) ?? 0) + 1;

    /// <summary>The name with spaces tidied, or null when it is empty or too long.</summary>
    internal static string? CleanName(string? name)
    {
        var clean = string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length == 0 || clean.Length > CollageSticker.NameMaxLength ? null : clean;
    }

    internal static string CleanWords(string? words) =>
        string.Join(' ', (words ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static object Snapshot(CollageSticker sticker) => new { sticker.Name, sticker.SearchWords, sticker.SortOrder, sticker.IsRetired };

    private Task RecordAsync(string action, CollageSticker sticker, object? before, string? actorUserId, string message, CancellationToken cancellationToken) =>
        _audit.RecordAsync(new AdminAuditEntry(
            action,
            nameof(CollageSticker),
            sticker.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Before: before,
            After: Snapshot(sticker),
            ActorUserId: actorUserId,
            Origin: AuditOrigin,
            Message: message), cancellationToken);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
